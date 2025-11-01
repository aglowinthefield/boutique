using System.Windows;
using Autofac;
using RequiemGlamPatcher.Models;
using RequiemGlamPatcher.Services;
using RequiemGlamPatcher.ViewModels;
using RequiemGlamPatcher.Views;

namespace RequiemGlamPatcher;

public partial class App : Application
{
    private IContainer? _container;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure dependency injection
        var builder = new ContainerBuilder();

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

        // Show main window
        var mainWindow = _container.Resolve<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _container?.Dispose();
        base.OnExit(e);
    }
}
