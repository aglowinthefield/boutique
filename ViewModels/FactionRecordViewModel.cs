using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Boutique.ViewModels;

public class FactionRecordViewModel : ReactiveObject
{
    private readonly string _searchCache;

    public FactionRecordViewModel(FactionRecord factionRecord)
    {
        FactionRecord = factionRecord;
        _searchCache = $"{DisplayName} {EditorID} {ModDisplayName} {FormKeyString}".ToLowerInvariant();
    }

    public FactionRecord FactionRecord { get; }

    public string EditorID => FactionRecord.EditorID ?? "(No EditorID)";
    public string DisplayName => FactionRecord.DisplayName;
    public string ModDisplayName => FactionRecord.ModDisplayName;
    public string FormKeyString => FactionRecord.FormKeyString;
    public FormKey FormKey => FactionRecord.FormKey;

    [Reactive] public bool IsSelected { get; set; }

    public bool MatchesSearch(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return true;

        return _searchCache.Contains(searchTerm.Trim().ToLowerInvariant());
    }
}

