using System.Windows;
using Boutique.Models;
using Boutique.Services;

namespace Boutique.Views;

public partial class MissingMastersDialog : Window
{
    public bool CleanPatch { get; private set; }

    public MissingMastersDialog(MissingMastersResult result)
    {
        InitializeComponent();

        if (ThemeService.Current is { } themeService)
        {
            RootScaleTransform.ScaleX = themeService.CurrentFontScale;
            RootScaleTransform.ScaleY = themeService.CurrentFontScale;
        }

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
        AffectedOutfits = [.. info.AffectedOutfits.Select(o => new AffectedOutfitViewModel(o))];
    }
}

public class AffectedOutfitViewModel(AffectedOutfitInfo info)
{
    public string DisplayName { get; } = info.EditorId ?? info.FormKey.ToString();

    public int OrphanedCount { get; } = info.OrphanedArmorFormKeys.Count;
}
