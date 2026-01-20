using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using Boutique.ViewModels;

namespace Boutique.Views;

public partial class DistributionEntryListView
{
    public DistributionEntryListView() => InitializeComponent();

    private void RemoveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DistributionEntryViewModel entryVm)
        {
            var targetName = entryVm.TargetDisplayName;
            var result = MessageBox.Show(
                $"Are you sure you want to remove this entry?\n\n{targetName}",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                entryVm.RemoveCommand.Execute().Subscribe();
            }
        }
    }
}
