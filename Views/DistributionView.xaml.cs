using System.Reactive;
using System.Reactive.Linq;
using System.Windows;
using Boutique.Services;
using Boutique.ViewModels;
using ReactiveUI;

namespace Boutique.Views;

public partial class DistributionView
{
    private IDisposable? _previewSubscription;

    public DistributionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DisposePreviewSubscription();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DisposePreviewSubscription();

        if (e.NewValue is not DistributionViewModel viewModel)
            return;

        _previewSubscription = viewModel.ShowPreview.RegisterHandler(async interaction =>
        {
            await Dispatcher.InvokeAsync(() =>
            {
                var owner = Window.GetWindow(this);
                var themeService = ThemeService.Current;
                if (themeService == null) return;

                var window = new OutfitPreviewWindow(interaction.Input, themeService) { Owner = owner };
                window.Show();
            });

            interaction.SetOutput(Unit.Default);
        });

        // Trigger refresh and NPC scan if view is already loaded
        if (IsLoaded) TriggerInitialLoadIfNeeded(viewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Automatically load distribution files and scan NPCs when the view first loads
        if (DataContext is DistributionViewModel viewModel) TriggerInitialLoadIfNeeded(viewModel);
    }

    private static void TriggerInitialLoadIfNeeded(DistributionViewModel viewModel)
    {
        // First, ensure distribution files are loaded (uses cache, doesn't force refresh)
        if (viewModel.Files.Count == 0 && !viewModel.IsLoading)
        {
            // Subscribe to command completion to trigger NPC scan after cache loads
            viewModel.EnsureLoadedCommand.Execute()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => TriggerNpcScanIfNeeded(viewModel));
        }
        else
        {
            // If files already loaded, trigger NPC scan immediately
            TriggerNpcScanIfNeeded(viewModel);
        }
    }

    private static void TriggerNpcScanIfNeeded(DistributionViewModel viewModel)
    {
        // Only scan if NPCs haven't been loaded yet and we're not already loading
        if (viewModel.AvailableNpcs.Count == 0 && !viewModel.IsLoading) _ = viewModel.ScanNpcsCommand.Execute();
    }

    private void DisposePreviewSubscription()
    {
        _previewSubscription?.Dispose();
        _previewSubscription = null;
    }
}
