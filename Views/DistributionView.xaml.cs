using System.Reactive;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DisposePreviewSubscription();
    }

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
                var window = new OutfitPreviewWindow(interaction.Input)
                {
                    Owner = owner
                };
                window.Show();
            });

            interaction.SetOutput(Unit.Default);
        });

        // Trigger refresh and NPC scan if view is already loaded
        if (IsLoaded)
        {
            TriggerInitialLoadIfNeeded(viewModel);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Automatically load distribution files and scan NPCs when the view first loads
        if (DataContext is DistributionViewModel viewModel)
        {
            TriggerInitialLoadIfNeeded(viewModel);
        }
    }

    private void TriggerInitialLoadIfNeeded(DistributionViewModel viewModel)
    {
        // First, refresh distribution files if they haven't been loaded yet
        if (viewModel.Files.Count == 0 && !viewModel.IsLoading)
        {
            // Start refresh and subscribe to wait for it to complete
            _ = viewModel.RefreshCommand.Execute();
            
            // Subscribe to wait for refresh to complete, then trigger NPC scan
            viewModel.WhenAnyValue(vm => vm.IsLoading)
                .Where(isLoading => !isLoading) // Wait for loading to complete
                .Take(1) // Only take the first completion
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    // Now trigger NPC scan after refresh completes
                    TriggerNpcScanIfNeeded(viewModel);
                });
        }
        else
        {
            // If refresh isn't needed, trigger NPC scan immediately
            TriggerNpcScanIfNeeded(viewModel);
        }
    }

    private void TriggerNpcScanIfNeeded(DistributionViewModel viewModel)
    {
        // Only scan if NPCs haven't been loaded yet and we're not already loading
        if (viewModel.AvailableNpcs.Count == 0 && !viewModel.IsLoading)
        {
            _ = viewModel.ScanNpcsCommand.Execute();
        }
    }

    private void DisposePreviewSubscription()
    {
        _previewSubscription?.Dispose();
        _previewSubscription = null;
    }

    private void ComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is DistributionViewModel viewModel)
        {
            viewModel.EnsureOutfitsLoaded();
        }
    }


    private void RemoveNpc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is NpcRecordViewModel npcVm)
        {
            // Find the parent DistributionEntryViewModel
            var itemsControl = FindVisualParent<ItemsControl>(button);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.RemoveNpc(npcVm);
            }
        }
    }

    private void RemoveFaction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is FactionRecordViewModel factionVm)
        {
            var itemsControl = FindVisualParent<ItemsControl>(button);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.RemoveFaction(factionVm);
            }
        }
    }

    private void RemoveKeyword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is KeywordRecordViewModel keywordVm)
        {
            var itemsControl = FindVisualParent<ItemsControl>(button);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.RemoveKeyword(keywordVm);
            }
        }
    }

    private void RemoveRace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RaceRecordViewModel raceVm)
        {
            var itemsControl = FindVisualParent<ItemsControl>(button);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.RemoveRace(raceVm);
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string? name = null) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
            {
                if (name == null || (child as FrameworkElement)?.Name == name)
                    return t;
            }

            var childOfChild = FindVisualChild<T>(child, name);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T t)
                return t;
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
}
