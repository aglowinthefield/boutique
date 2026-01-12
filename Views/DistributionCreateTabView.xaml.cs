using System.Windows;
using System.Windows.Controls;
using Boutique.ViewModels;

namespace Boutique.Views;

public partial class DistributionCreateTabView
{
    public DistributionCreateTabView()
    {
        InitializeComponent();
    }

    private void FilterableSelector_DropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is DistributionEditTabViewModel viewModel)
        {
            viewModel.EnsureOutfitsLoaded();
        }
    }

    private void RemoveNpc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is NpcRecordViewModel npcVm)
        {
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

    private void RemoveClass_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ClassRecordViewModel classVm)
        {
            var itemsControl = FindVisualParent<ItemsControl>(button);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.RemoveClass(classVm);
            }
        }
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
