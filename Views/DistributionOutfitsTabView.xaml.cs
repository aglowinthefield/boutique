using System.Windows;
using System.Windows.Controls;

namespace Boutique.Views;

public partial class DistributionOutfitsTabView
{
  public DistributionOutfitsTabView()
  {
    InitializeComponent();
  }

  private void OverflowButton_Click(object sender, RoutedEventArgs e)
  {
    if (sender is not Button { ContextMenu: { } menu } button)
    {
      return;
    }

    menu.PlacementTarget = button;
    menu.IsOpen          = true;
  }
}
