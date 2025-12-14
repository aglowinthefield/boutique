using System.Globalization;
using System.IO;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _loggingService = new LoggingService();
        ConfigureExceptionLogging();
        Log.Information("Application startup invoked.");

        // Configure dependency injection
        var builder = new ContainerBuilder();

        builder.RegisterInstance(_loggingService).As<ILoggingService>().SingleInstance();
        builder.Register(ctx => ctx.Resolve<ILoggingService>().Logger).As<ILogger>().SingleInstance();

        // Register models
        builder.RegisterType<PatcherSettings>().AsSelf().SingleInstance();

        // Register services
        builder.RegisterType<MutagenService>().SingleInstance();
        builder.RegisterType<GameAssetLocator>().SingleInstance();
        builder.RegisterType<PatchingService>().SingleInstance();
        builder.RegisterType<MatchingService>().SingleInstance();
        builder.RegisterType<ArmorPreviewService>().SingleInstance();
        builder.RegisterType<DistributionDiscoveryService>().SingleInstance();
        builder.RegisterType<NpcScanningService>().SingleInstance();
        builder.RegisterType<DistributionFileWriterService>().SingleInstance();
        builder.RegisterType<NpcOutfitResolutionService>().SingleInstance();
        builder.RegisterType<SpidFilterMatchingService>().SingleInstance();
        builder.RegisterType<DistributionConflictDetectionService>().SingleInstance();
        builder.RegisterType<CrossSessionCacheService>().SingleInstance();
        builder.RegisterType<GameDataCacheService>().SingleInstance();

        // Register ViewModels
        builder.RegisterType<MainViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<DistributionViewModel>().AsSelf().SingleInstance();

        // Register Views
        builder.RegisterType<MainWindow>().AsSelf();

        _container = builder.Build();

        ApplyThemeToggle();

        // Show main window
        try
        {
            var mainWindow = _container.Resolve<MainWindow>();
            mainWindow.Show();
            Log.Information("Main window displayed.");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to show main window.");
            throw;
        }

        // Check for updates (deferred so the window renders first)
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500); // Let the UI render and initialize
            Current.Dispatcher.Invoke(CheckForUpdates);
        });
    }

    private static void CheckForUpdates()
    {
        try
        {
            // Configure AutoUpdater
            AutoUpdater.ShowSkipButton = true;
            AutoUpdater.ShowRemindLaterButton = true;
            AutoUpdater.ReportErrors = false; // Fail silently if no internet connection
            AutoUpdater.RunUpdateAsAdmin = false;
            AutoUpdater.HttpUserAgent = "Boutique-Updater"; // Required for GitHub API

            // Use custom parser for GitHub releases API
            AutoUpdater.ParseUpdateInfoEvent += ParseGitHubRelease;

            // GitHub releases API endpoint
            const string updateUrl = "https://api.github.com/repos/aglowinthefield/Boutique/releases/latest";

            AutoUpdater.Start(updateUrl);
            Log.Information("Update check initiated.");
        }
        catch (Exception ex)
        {
            // Don't crash the app if update check fails
            Log.Warning(ex, "Failed to check for updates.");
        }
    }

    private static void ParseGitHubRelease(ParseUpdateInfoEventArgs args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args.RemoteData);
            var root = doc.RootElement;

            // Get tag name (e.g., "0.0.1-alpha2" or "v1.0.0")
            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var version = tagName.TrimStart('v');

            // Get changelog URL
            var changelogUrl = root.GetProperty("html_url").GetString() ?? "";

            // Find the zip asset in the release
            string? downloadUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
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

            // Parse version - handle semantic versioning with pre-release tags
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

    /// <summary>
    /// Parses semantic version strings like "1.0.0", "0.0.1-alpha2", "v2.1.0-beta.1"
    /// Returns a Version object for comparison (pre-release info is stripped for comparison)
    /// </summary>
    private static Version? ParseSemanticVersion(string versionString)
    {
        // Remove 'v' prefix if present
        versionString = versionString.TrimStart('v');

        // Extract just the numeric part (before any hyphen for pre-release)
        var match = Regex.Match(versionString, @"^(\d+)\.(\d+)\.(\d+)");
        if (!match.Success)
            return null;

        var major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var patch = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        return new Version(major, minor, patch);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application exit.");
        _container?.Dispose();
        _loggingService?.Dispose();
        base.OnExit(e);
    }

    private static void ApplyThemeToggle()
    {
        try
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".config", "theme.json");
            if (!File.Exists(configPath))
                return;

            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("EnableTheme", out var enableThemeElement))
                return;

            var enableTheme = enableThemeElement.GetBoolean();
            if (!enableTheme && Current.Resources.MergedDictionaries.Count > 0)
                Current.Resources.MergedDictionaries.Clear();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply theme toggle configuration. Continuing with default resources.");
        }
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
