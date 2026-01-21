using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class OutfitRecordViewModel : ReactiveObject
{
    private readonly string _searchCache;

    [Reactive] private bool _isSelected;

    /// <summary>
    ///     Number of NPCs that have this outfit distributed to them.
    ///     Updated by the parent ViewModel.
    /// </summary>
    [Reactive] private int _npcCount;

    public OutfitRecordViewModel(IOutfitGetter outfit)
    {
        Outfit = outfit;
        EditorID = outfit.EditorID ?? "(No EditorID)";
        FormKey = outfit.FormKey;
        FormKeyString = outfit.FormKey.ToString();
        ModDisplayName = outfit.FormKey.ModKey.FileName;
        _searchCache = $"{EditorID} {ModDisplayName} {FormKeyString}".ToLowerInvariant();
    }

    public IOutfitGetter Outfit { get; }

    public string EditorID { get; }
    public FormKey FormKey { get; }
    public string FormKeyString { get; }
    public string ModDisplayName { get; }

    public bool MatchesSearch(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return true;

        return _searchCache.Contains(searchTerm.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
