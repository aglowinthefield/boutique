using System.Globalization;
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
    private IContainer? _container;
    private LoggingService? _loggingService;

    public IContainer? Container => _container;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using (StartupProfiler.Instance.BeginOperation("CodePageRegistration"))
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        _loggingService = new LoggingService();
        ConfigureExceptionLogging();
        Log.Information("Application startup invoked.");
        LogMO2Environment();

        using (StartupProfiler.Instance.BeginOperation("ContainerBuild"))
        {
            var builder = new ContainerBuilder();

            builder.RegisterInstance(_loggingService).As<ILoggingService>().SingleInstance();
            builder.Register(ctx => ctx.Resolve<ILoggingService>().Logger).As<ILogger>().SingleInstance();

            builder.RegisterType<PatcherSettings>().AsSelf().SingleInstance();
            builder.RegisterType<MutagenService>().SingleInstance();
            builder.RegisterType<GameAssetLocator>().SingleInstance();
            builder.RegisterType<PatchingService>().SingleInstance();
            builder.RegisterType<ArmorPreviewService>().SingleInstance();
            builder.RegisterType<DistributionDiscoveryService>().SingleInstance();
            builder.RegisterType<NpcScanningService>().SingleInstance();
            builder.RegisterType<DistributionFileWriterService>().SingleInstance();
            builder.RegisterType<NpcOutfitResolutionService>().SingleInstance();
            builder.RegisterType<KeywordDistributionResolver>().SingleInstance();
            builder.RegisterType<SpidFilterMatchingService>().SingleInstance();
            builder.RegisterType<DistributionConflictDetectionService>().SingleInstance();
            builder.RegisterType<GameDataCacheService>().SingleInstance();
            builder.RegisterType<ThemeService>().SingleInstance();
            builder.RegisterType<TutorialService>().SingleInstance();
            builder.RegisterType<GuiSettingsService>().SingleInstance();
            builder.RegisterType<LocalizationService>().SingleInstance();

            builder.RegisterType<MainViewModel>().AsSelf().SingleInstance();
            builder.RegisterType<SettingsViewModel>().AsSelf().SingleInstance();
            builder.RegisterType<DistributionViewModel>().AsSelf().SingleInstance();

            builder.RegisterType<MainWindow>().AsSelf();

            _container = builder.Build();
        }

        using (StartupProfiler.Instance.BeginOperation("ThemeInitialization"))
        {
            var themeService = _container.Resolve<ThemeService>();
            themeService.Initialize();
        }

        try
        {
            using (StartupProfiler.Instance.BeginOperation("LocalizationInitialization"))
            {
                var localizationService = _container.Resolve<LocalizationService>();
                localizationService.Initialize();
            }

            using (StartupProfiler.Instance.BeginOperation("MainWindowCreation"))
            {
                var mainWindow = _container.Resolve<MainWindow>();
                mainWindow.Show();
            }

            Log.Information("Main window displayed.");
            StartupProfiler.Instance.EndOperation("AppStartup");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to show main window.");
            throw;
        }

        if (FeatureFlags.AutoUpdateEnabled)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                Current.Dispatcher.Invoke(CheckForUpdates);
            });
        }
    }

    private static void CheckForUpdates()
    {
        if (!FeatureFlags.AutoUpdateEnabled)
        {
            return;
        }

        try
        {
            AutoUpdater.ShowSkipButton = true;
            AutoUpdater.ShowRemindLaterButton = true;
            AutoUpdater.ReportErrors = false;
            AutoUpdater.RunUpdateAsAdmin = false;
            AutoUpdater.HttpUserAgent = "Boutique-Updater";

            AutoUpdater.ParseUpdateInfoEvent += ParseGitHubRelease;
            const string updateUrl = "https://api.github.com/repos/aglowinthefield/Boutique/releases/latest";

            AutoUpdater.Start(updateUrl);
            Log.Information("Update check initiated.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for updates.");
        }
    }

    private static void ParseGitHubRelease(ParseUpdateInfoEventArgs args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args.RemoteData);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var version = tagName.TrimStart('v');
            var changelogUrl = root.GetProperty("html_url").GetString() ?? string.Empty;

            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets))
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

            if (string.IsNullOrEmpty(downloadUrl))
            {
                Log.Warning("No zip asset found in GitHub release.");
                return;
            }

            var parsedVersion = ParseSemanticVersion(version);
            if (parsedVersion == null)
            {
                Log.Warning("Could not parse version from tag: {Tag}", tagName);
                return;
            }

            args.UpdateInfo = new UpdateInfoEventArgs
            {
                CurrentVersion = parsedVersion.ToString(),
                ChangelogURL = changelogUrl,
                DownloadURL = downloadUrl,
                Mandatory = new Mandatory { Value = false }
            };

            Log.Information("Found update: {Version} at {Url}", version, downloadUrl);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse GitHub release info.");
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
            "MO_DATAPATH", "MO_GAMEPATH", "MO_PROFILE", "MO_PROFILEDIR", "MO_MODSDIR", "USVFS_LOGFILE",
            "VIRTUAL_STORE"
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
        _container?.Dispose();
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
