using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Boutique.ViewModels;

namespace Boutique.Views;

public partial class DistributionEntryEditorView
{
    public DistributionEntryEditorView()
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

    private void ToggleNpcNegation_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is StackPanel panel && panel.Tag is NpcRecordViewModel npcVm)
        {
            npcVm.IsExcluded = !npcVm.IsExcluded;
            var itemsControl = FindVisualParent<ItemsControl>(panel);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.UpdateEntryNpcs();
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

    private void ToggleFactionNegation_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is StackPanel panel && panel.Tag is FactionRecordViewModel factionVm)
        {
            factionVm.IsExcluded = !factionVm.IsExcluded;
            var itemsControl = FindVisualParent<ItemsControl>(panel);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.UpdateEntryFactions();
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

    private void ToggleKeywordNegation_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is StackPanel panel && panel.Tag is KeywordRecordViewModel keywordVm)
        {
            keywordVm.IsExcluded = !keywordVm.IsExcluded;
            var itemsControl = FindVisualParent<ItemsControl>(panel);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.UpdateEntryKeywords();
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

    private void ToggleRaceNegation_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is StackPanel panel && panel.Tag is RaceRecordViewModel raceVm)
        {
            raceVm.IsExcluded = !raceVm.IsExcluded;
            var itemsControl = FindVisualParent<ItemsControl>(panel);
            if (itemsControl?.DataContext is DistributionEntryViewModel entryVm)
            {
                entryVm.UpdateEntryRaces();
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

    private static T? FindVisualParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T t)
            {
                return t;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
}
