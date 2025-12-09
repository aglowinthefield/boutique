using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Boutique.ViewModels;

public class RaceRecordViewModel : ReactiveObject
{
    private readonly string _searchCache;

    public RaceRecordViewModel(RaceRecord raceRecord)
    {
        RaceRecord = raceRecord;
        _searchCache = $"{DisplayName} {EditorID} {ModDisplayName} {FormKeyString}".ToLowerInvariant();
    }

    public RaceRecord RaceRecord { get; }

    public string EditorID => RaceRecord.EditorID ?? "(No EditorID)";
    public string DisplayName => RaceRecord.DisplayName;
    public string ModDisplayName => RaceRecord.ModDisplayName;
    public string FormKeyString => RaceRecord.FormKeyString;
    public FormKey FormKey => RaceRecord.FormKey;

    [Reactive] public bool IsSelected { get; set; }

    public bool MatchesSearch(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return true;

        return _searchCache.Contains(searchTerm.Trim().ToLowerInvariant());
    }
}

