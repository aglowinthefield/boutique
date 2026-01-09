using System.Windows;
using Boutique.Models;

namespace Boutique.Views;

public partial class MissingMastersDialog : Window
{
    public bool CleanPatch { get; private set; }

    public MissingMastersDialog(MissingMastersResult result)
    {
        InitializeComponent();

        var viewModels = result.MissingMasters
            .Select(m => new MissingMasterViewModel(m))
            .ToList();

        MissingMastersItemsControl.ItemsSource = viewModels;

        var totalOutfits = result.AllAffectedOutfits.Count;
        var totalMasters = result.MissingMasters.Count;
        SummaryText.Text = $"{totalOutfits} outfit(s) will be removed if you clean the patch. " +
                           $"{totalMasters} missing master(s) need to be added back to keep them.";
    }

    private void AddMastersButton_Click(object sender, RoutedEventArgs e)
    {
        CleanPatch = false;
        DialogResult = false;
        Close();
    }

    private void CleanPatchButton_Click(object sender, RoutedEventArgs e)
    {
        CleanPatch = true;
        DialogResult = true;
        Close();
    }
}

public class MissingMasterViewModel
{
    public string MasterFileName { get; }
    public IReadOnlyList<AffectedOutfitViewModel> AffectedOutfits { get; }

    public MissingMasterViewModel(MissingMasterInfo info)
    {
        MasterFileName = info.MissingMaster.FileName;
        AffectedOutfits = info.AffectedOutfits
            .Select(o => new AffectedOutfitViewModel(o))
            .ToList();
    }
}

public class AffectedOutfitViewModel
{
    public string DisplayName { get; }
    public int OrphanedCount { get; }

    public AffectedOutfitViewModel(AffectedOutfitInfo info)
    {
        DisplayName = info.EditorId ?? info.FormKey.ToString();
        OrphanedCount = info.OrphanedArmorFormKeys.Count;
    }
}
