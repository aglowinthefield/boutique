using System.Resources;
using System.Text;
using System.Windows;
using Autofac;
using Boutique.Models;
using Boutique.Services;
using Boutique.ViewModels;
using Boutique.Views;
using Serilog;

[assembly: NeutralResourcesLanguage("en")]

namespace Boutique;

// WPF Application disposes resources in OnExit, which is the proper pattern for WPF apps
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
public partial class App
#pragma warning restore CA1001
{
  private LoggingService? _loggingService;

  public IContainer? Container { get; private set; }

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    _loggingService = new LoggingService();
    ConfigureExceptionLogging();
    Log.Information("Application startup invoked.");

    var builder = new ContainerBuilder();

    builder.RegisterInstance(_loggingService).As<ILoggingService>().SingleInstance();
    builder.Register(ctx => ctx.Resolve<ILoggingService>().Logger).As<ILogger>().SingleInstance();

    builder.RegisterType<PatcherSettings>().AsSelf().SingleInstance();
    builder.RegisterType<MutagenService>().SingleInstance();
    builder.RegisterType<GameAssetLocator>().SingleInstance();
    builder.RegisterType<PatchingService>().SingleInstance();
    builder.RegisterType<ArmorPreviewService>().SingleInstance();
    builder.RegisterType<DistributionScannerService>().SingleInstance();
    builder.RegisterType<DistributionFileEditorService>().SingleInstance();
    builder.RegisterType<NpcOutfitResolutionService>().SingleInstance();
    builder.RegisterType<KeywordDistributionResolver>().SingleInstance();
    builder.RegisterType<DistributionConflictDetectionService>().SingleInstance();
    builder.RegisterType<GameDataCacheService>().SingleInstance();
    builder.RegisterType<OutfitDraftManager>().SingleInstance();
    builder.RegisterType<DistributionEntryHydrationService>().SingleInstance();
    builder.RegisterType<DistributionFilePathService>().SingleInstance();
    builder.RegisterType<ThemeService>().SingleInstance();
    builder.RegisterType<GuiSettingsService>().SingleInstance();
    builder.RegisterType<LocalizationService>().SingleInstance();
    builder.RegisterType<AutoUpdateService>().SingleInstance();

    builder.RegisterType<MainViewModel>().AsSelf().SingleInstance();
    builder.RegisterType<SettingsViewModel>().AsSelf().SingleInstance();
    builder.RegisterType<DistributionViewModel>().AsSelf().SingleInstance();
    builder.RegisterType<OutfitCreatorViewModel>().AsSelf().SingleInstance();

    builder.RegisterType<MainWindow>().AsSelf();

    Container = builder.Build();

    var themeService = Container.Resolve<ThemeService>();
    themeService.Initialize();

    var localizationService = Container.Resolve<LocalizationService>();
    localizationService.Initialize();

    var mainWindow = Container.Resolve<MainWindow>();
    mainWindow.Show();

    Log.Information("Main window displayed.");

    if (GuiSettingsService.Current?.AutoUpdateEnabled == true)
    {
      var autoUpdateService = Container.Resolve<AutoUpdateService>();
      _ = Task.Run(async () =>
      {
        await Task.Delay(1500);
        Current.Dispatcher.Invoke(() => autoUpdateService.CheckForUpdates());
      });
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
