using System;
using System.Threading.Tasks;
using System.Windows;
using Autofac;
using RequiemGlamPatcher.Models;
using RequiemGlamPatcher.Services;
using RequiemGlamPatcher.ViewModels;
using RequiemGlamPatcher.Views;
using Serilog;

namespace RequiemGlamPatcher;

public partial class App : Application
{
    private IContainer? _container;
    private ILoggingService? _loggingService;

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
        builder.RegisterType<MutagenService>().As<IMutagenService>().SingleInstance();
        builder.RegisterType<PatchingService>().As<IPatchingService>().SingleInstance();
        builder.RegisterType<MatchingService>().As<IMatchingService>().SingleInstance();

        // Register ViewModels
        builder.RegisterType<MainViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<SettingsViewModel>().AsSelf().SingleInstance();

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
            var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".config", "theme.json");
            if (!System.IO.File.Exists(configPath))
            {
                return;
            }

            var json = System.IO.File.ReadAllText(configPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("EnableTheme", out var enableThemeElement))
            {
                return;
            }

            var enableTheme = enableThemeElement.GetBoolean();
            if (!enableTheme && Current.Resources.MergedDictionaries.Count > 0)
            {
                Current.Resources.MergedDictionaries.Clear();
            }
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
