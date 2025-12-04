using System;
using System.Linq;
using System.Reactive;
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
    }

    private void DisposePreviewSubscription()
    {
        _previewSubscription?.Dispose();
        _previewSubscription = null;
    }

    private void ToggleEditMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DistributionViewModel viewModel)
        {
            viewModel.IsEditMode = !viewModel.IsEditMode;
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
