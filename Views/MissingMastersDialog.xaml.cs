using System.Windows;
using Boutique.Models;
using Boutique.Resources;
using Boutique.Services;

namespace Boutique.Views;

public partial class MissingMastersDialog : Window
{
  public MissingMastersDialog(MissingMastersResult result)
  {
    InitializeComponent();

    if (ThemeService.Current is { } themeService)
    {
      RootScaleTransform.ScaleX = themeService.CurrentFontScale;
      RootScaleTransform.ScaleY = themeService.CurrentFontScale;

      SourceInitialized += (_, _) => themeService.ApplyTitleBarTheme(this);
    }

    var viewModels = result.MissingMasters
                           .Select(m => new MissingMasterViewModel(m))
                           .ToList();

    MissingMastersItemsControl.ItemsSource = viewModels;

    var totalOutfits = result.AllAffectedOutfits.Count;
    var totalMasters = result.MissingMasters.Count;
    SummaryText.Text = LocalizationService.GetFormatted(
      LocalizationKeys.View.MissingMastersSummaryFormat,
      "{0} outfit(s) will be removed if you clean the patch. {1} missing master(s) need to be added back to keep them.",
      totalOutfits,
      totalMasters);
  }

  public bool CleanPatch { get; private set; }

  private void AddMastersButton_Click(object sender, RoutedEventArgs e)
  {
    CleanPatch   = false;
    DialogResult = false;
    Close();
  }

  private void CleanPatchButton_Click(object sender, RoutedEventArgs e)
  {
    CleanPatch   = true;
    DialogResult = true;
    Close();
  }
}

public class MissingMasterViewModel
{
  public MissingMasterViewModel(MissingMasterInfo info)
  {
    MasterFileName  = info.MissingMaster.FileName;
    AffectedOutfits = [.. info.AffectedOutfits.Select(o => new AffectedOutfitViewModel(o))];
  }

  public string MasterFileName { get; }

  public IReadOnlyList<AffectedOutfitViewModel> AffectedOutfits { get; }
}

public class AffectedOutfitViewModel(AffectedOutfitInfo info)
{
  public string DisplayName { get; } = info.EditorId ?? info.FormKey.ToString();

  public int OrphanedCount { get; } = info.OrphanedArmorFormKeys.Count;
}
