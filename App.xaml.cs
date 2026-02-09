using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Autofac;
using AutoUpdaterDotNET;
using Boutique.Models;
using Boutique.Services;
using Boutique.ViewModels;
using Boutique.Views;
using Serilog;

namespace Boutique;

// WPF Application disposes resources in OnExit, which is the proper pattern for WPF apps
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
public partial class App
#pragma warning restore CA1001
{
  private static string _pendingReleaseNotes = string.Empty;
  private static bool _forceShowUpdate;
  private LoggingService? _loggingService;

  public IContainer? Container { get; private set; }

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    _loggingService = new LoggingService();
    ConfigureExceptionLogging();
    Log.Information("Application startup invoked.");
    LogMO2Environment();

    var builder = new ContainerBuilder();

    builder.RegisterInstance(_loggingService).As<ILoggingService>().SingleInstance();
    builder.Register(ctx => ctx.Resolve<ILoggingService>().Logger).As<ILogger>().SingleInstance();

    builder.RegisterType<PatcherSettings>().AsSelf().SingleInstance();
    builder.RegisterType<MutagenService>().SingleInstance();
    builder.RegisterType<GameAssetLocator>().SingleInstance();
    builder.RegisterType<PatchingService>().SingleInstance();
    builder.RegisterType<ArmorPreviewService>().SingleInstance();
    builder.RegisterType<DistributionDiscoveryService>().SingleInstance();
    builder.RegisterType<DistributionFileWriterService>().SingleInstance();
    builder.RegisterType<NpcOutfitResolutionService>().SingleInstance();
    builder.RegisterType<KeywordDistributionResolver>().SingleInstance();
    builder.RegisterType<SpidFilterMatchingService>().SingleInstance();
    builder.RegisterType<DistributionConflictDetectionService>().SingleInstance();
    builder.RegisterType<GameDataCacheService>().SingleInstance();
    builder.RegisterType<OutfitDraftManager>().SingleInstance();
    builder.RegisterType<DistributionEntryHydrationService>().SingleInstance();
    builder.RegisterType<DistributionFilePathService>().SingleInstance();
    builder.RegisterType<ThemeService>().SingleInstance();
    builder.RegisterType<TutorialService>().SingleInstance();
    builder.RegisterType<GuiSettingsService>().SingleInstance();
    builder.RegisterType<LocalizationService>().SingleInstance();

    builder.RegisterType<MainViewModel>().AsSelf().SingleInstance();
    builder.RegisterType<SettingsViewModel>().AsSelf().SingleInstance();
    builder.RegisterType<DistributionViewModel>().AsSelf().SingleInstance();
    builder.RegisterType<OutfitCreatorViewModel>().AsSelf().SingleInstance();

    builder.RegisterType<MainWindow>().AsSelf();

    Container = builder.Build();

    var themeService = Container.Resolve<ThemeService>();
    themeService.Initialize();

    try
    {
      var localizationService = Container.Resolve<LocalizationService>();
      localizationService.Initialize();

      var mainWindow = Container.Resolve<MainWindow>();
      mainWindow.Show();

      Log.Information("Main window displayed.");
    }
    catch (Exception ex)
    {
      Log.Fatal(ex, "Failed to show main window.");
      throw;
    }

    if (GuiSettingsService.Current?.AutoUpdateEnabled == true)
    {
      _ = Task.Run(async () =>
      {
        await Task.Delay(1500);
        Current.Dispatcher.Invoke(() => CheckForUpdates());
      });
    }
  }

  public static void CheckForUpdates(bool forceShow = false)
  {
    if (!forceShow && GuiSettingsService.Current?.AutoUpdateEnabled != true)
    {
      return;
    }

    _forceShowUpdate = forceShow;

    try
    {
      AutoUpdater.ReportErrors = forceShow;
      AutoUpdater.RunUpdateAsAdmin = false;
      AutoUpdater.HttpUserAgent = "Boutique-Updater";
      AutoUpdater.InstallationPath = AppDomain.CurrentDomain.BaseDirectory;

      var installedVersion = GetInstalledVersion();
      if (installedVersion != null)
      {
        AutoUpdater.InstalledVersion = installedVersion;
        Log.Information("Current installed version: {Version}", installedVersion);
      }

      AutoUpdater.ParseUpdateInfoEvent -= ParseGitHubReleases;
      AutoUpdater.ParseUpdateInfoEvent += ParseGitHubReleases;
      AutoUpdater.CheckForUpdateEvent -= OnCheckForUpdate;
      AutoUpdater.CheckForUpdateEvent += OnCheckForUpdate;
      const string updateUrl = "https://api.github.com/repos/aglowinthefield/Boutique/releases";

      AutoUpdater.Start(updateUrl);
      Log.Information("Update check initiated (forceShow: {ForceShow}).", forceShow);
    }
    catch (Exception ex)
    {
      Log.Warning(ex, "Failed to check for updates.");
    }
  }

  private static void OnCheckForUpdate(UpdateInfoEventArgs args)
  {
    if (args.IsUpdateAvailable)
    {
      var dialog = new UpdateDialog
      {
        CurrentVersion = (AutoUpdater.InstalledVersion ?? new Version(0, 0, 0)).ToString(),
        LatestVersion = args.CurrentVersion,
        ReleaseNotes = _pendingReleaseNotes,
        DownloadUrl = args.DownloadURL,
        Owner = Current.MainWindow,
        DataContext = null
      };
      dialog.DataContext = dialog;

      if (dialog.ShowDialog() == true && dialog.Result == UpdateResult.Update)
      {
        try
        {
          if (AutoUpdater.DownloadUpdate(args))
          {
            Current.Shutdown();
          }
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Failed to download update.");
          MessageBox.Show(
            $"Failed to download update: {ex.Message}",
            "Update Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        }
      }
    }
    else if (_forceShowUpdate)
    {
      MessageBox.Show(
        "You are running the latest version.",
        "No Update Available",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
    }
  }

  private static Version? GetInstalledVersion()
  {
    var assembly = Assembly.GetExecutingAssembly();
    var infoVersion = assembly
      .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
      .OfType<AssemblyInformationalVersionAttribute>()
      .FirstOrDefault()?.InformationalVersion;

    return string.IsNullOrEmpty(infoVersion) ? assembly.GetName().Version : ParseSemanticVersion(infoVersion);
  }

  private static void ParseGitHubReleases(ParseUpdateInfoEventArgs args)
  {
    try
    {
      using var doc = JsonDocument.Parse(args.RemoteData);
      var releases = doc.RootElement;

      if (releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() == 0)
      {
        Log.Warning("No releases found in GitHub response.");
        return;
      }

      var installedVersion = AutoUpdater.InstalledVersion ?? new Version(0, 0, 0);
      var newerReleases = new List<(Version Version, string Tag, string Body, string? DownloadUrl)>();

      foreach (var release in releases.EnumerateArray())
      {
        var tagName = release.GetProperty("tag_name").GetString() ?? string.Empty;
        var parsedVersion = ParseSemanticVersion(tagName);
        if (parsedVersion == null || parsedVersion <= installedVersion)
        {
          continue;
        }

        var body = release.TryGetProperty("body", out var bodyProp)
          ? bodyProp.GetString() ?? string.Empty
          : string.Empty;

        string? downloadUrl = null;
        if (release.TryGetProperty("assets", out var assets))
        {
          foreach (var asset in assets.EnumerateArray())
          {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
              downloadUrl = asset.GetProperty("browser_download_url").GetString();
              break;
            }
          }
        }

        newerReleases.Add((parsedVersion, tagName, body, downloadUrl));
      }

      if (newerReleases.Count == 0)
      {
        Log.Information("No newer releases found. Current version: {Version}", installedVersion);
        return;
      }

      newerReleases.Sort((a, b) => b.Version.CompareTo(a.Version));
      var latest = newerReleases[0];

      if (string.IsNullOrEmpty(latest.DownloadUrl))
      {
        Log.Warning("No zip asset found in latest release {Tag}.", latest.Tag);
        return;
      }

      var sb = new StringBuilder();
      foreach (var (_, tag, body, _) in newerReleases)
      {
        sb.AppendLine($"═══ {tag} ═══");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(body) ? "(No release notes)" : body.Trim());
        sb.AppendLine();
      }

      _pendingReleaseNotes = sb.ToString().TrimEnd();

      args.UpdateInfo = new UpdateInfoEventArgs
      {
        CurrentVersion = latest.Version.ToString(),
        DownloadURL = latest.DownloadUrl,
        Mandatory = new Mandatory { Value = false }
      };

      Log.Information(
        "Found {Count} newer release(s). Latest: {Version}",
        newerReleases.Count,
        latest.Version);
    }
    catch (Exception ex)
    {
      Log.Warning(ex, "Failed to parse GitHub releases info.");
    }
  }

  private static Version? ParseSemanticVersion(string versionString)
  {
    versionString = versionString.TrimStart('v');
    var match = Regex.Match(versionString, @"^(\d+)\.(\d+)\.(\d+)");
    if (!match.Success)
    {
      return null;
    }

    var major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
    var patch = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

    return new Version(major, minor, patch);
  }

  private static void LogMO2Environment()
  {
    var mo2Vars = new[]
    {
      "MO_DATAPATH", "MO_GAMEPATH", "MO_PROFILE", "MO_PROFILEDIR", "MO_MODSDIR", "USVFS_LOGFILE", "VIRTUAL_STORE"
    };

    var detected = mo2Vars
      .Select(v => (Name: v, Value: Environment.GetEnvironmentVariable(v)))
      .Where(x => !string.IsNullOrEmpty(x.Value))
      .ToList();

    if (detected.Count > 0)
    {
      Log.Information(
        "MO2 environment detected: {Variables}",
        string.Join(", ", detected.Select(x => $"{x.Name}={x.Value}")));
    }
    else
    {
      Log.Debug("No MO2 environment variables detected - running standalone or MO2 env vars not set");
    }
  }

  protected override void OnExit(ExitEventArgs e)
  {
    Log.Information("Application exit.");
    Container?.Dispose();
    _loggingService?.Dispose();
    base.OnExit(e);
  }

  private void ConfigureExceptionLogging()
  {
    AppDomain.CurrentDomain.UnhandledException += (_, args) =>
      Log.Fatal(args.ExceptionObject as Exception, "AppDomain unhandled exception.");

    DispatcherUnhandledException += (_, args) =>
    {
      Log.Fatal(args.Exception, "Dispatcher unhandled exception.");
      args.Handled = true;
    };

    TaskScheduler.UnobservedTaskException += (_, args) =>
    {
      Log.Fatal(args.Exception, "Unobserved task exception.");
      args.SetObserved();
    };
  }
}
