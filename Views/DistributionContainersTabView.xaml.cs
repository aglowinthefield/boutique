using System.Windows;
using System.Windows.Controls;
using Boutique.ViewModels;

namespace Boutique.Views;

public partial class DistributionContainersTabView : UserControl
{
  public DistributionContainersTabView()
  {
    InitializeComponent();
    Loaded += OnLoaded;
  }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    Loaded -= OnLoaded;

    if (DataContext is DistributionContainersTabViewModel viewModel)
    {
      await viewModel.EnsureContainersLoadedAsync();
    }
  }
}
