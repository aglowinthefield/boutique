using System.Windows.Controls;
using System.Windows.Input;
using Boutique.ViewModels;

namespace Boutique.Views;

public partial class DistributionFilterTabsView
{
    public DistributionFilterTabsView() => InitializeComponent();

    private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || sender is not DataGrid dataGrid)
            return;

        foreach (var item in dataGrid.SelectedItems)
        {
            if (item is ISelectableRecordViewModel selectable)
                selectable.IsSelected = !selectable.IsSelected;
        }

        e.Handled = true;
    }
}
