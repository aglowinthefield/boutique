using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class OutfitRecordViewModel : ReactiveObject, ISelectableRecordViewModel
{
  private readonly string _searchCache;

  [Reactive] private bool _isExcluded;

  [Reactive] private bool _isSelected;

  /// <summary>
  ///   Number of NPCs that have this outfit distributed to them.
  ///   Updated by the parent ViewModel.
  /// </summary>
  [Reactive] private int _npcCount;

  public OutfitRecordViewModel(IOutfitGetter outfit, bool containsLeveledItems = false, ModKey? sourceMod = null)
  {
    Outfit               = outfit;
    EditorID             = outfit.EditorID ?? "(No EditorID)";
    FormKey              = outfit.FormKey;
    FormKeyString        = outfit.FormKey.ToString();
    SourceMod            = sourceMod ?? outfit.FormKey.ModKey;
    ModDisplayName       = SourceMod.FileName;
    ContainsLeveledItems = containsLeveledItems;
    _searchCache         = $"{EditorID} {ModDisplayName} {FormKeyString}".ToLowerInvariant();
  }

  public IOutfitGetter Outfit { get; }
  public bool ContainsLeveledItems { get; }

  public string EditorID { get; }
  public string DisplayName => EditorID;
  public FormKey FormKey { get; }
  public string FormKeyString { get; }

  /// <summary>Mod supplying the winning override of this outfit.</summary>
  public ModKey SourceMod { get; }
  public string ModDisplayName { get; }

  public bool MatchesSearch(string searchTerm)
  {
    if (string.IsNullOrWhiteSpace(searchTerm))
    {
      return true;
    }

    return _searchCache.Contains(searchTerm.Trim(), StringComparison.OrdinalIgnoreCase);
  }
}
