using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using Boutique.ViewModels;
using ReactiveUI;

namespace Boutique.Views;

public partial class DistributionEntryListView
{
  private IDisposable? _selectedEntrySubscription;

  public DistributionEntryListView()
  {
    InitializeComponent();
    DataContextChanged += OnDataContextChanged;
  }

  private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
  {
    _selectedEntrySubscription?.Dispose();

    if (e.NewValue is DistributionEditTabViewModel vm)
    {
      _selectedEntrySubscription = vm
        .WhenAnyValue(x => x.SelectedEntry)
        .ObserveOn(RxApp.MainThreadScheduler)
        .Subscribe(entry =>
        {
          if (entry != null)
          {
            EntryListBox.ScrollIntoView(entry);
          }
        });
    }
  }

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
