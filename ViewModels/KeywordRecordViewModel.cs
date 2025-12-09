using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Boutique.ViewModels;

public class KeywordRecordViewModel : ReactiveObject
{
    private readonly string _searchCache;

    public KeywordRecordViewModel(KeywordRecord keywordRecord)
    {
        KeywordRecord = keywordRecord;
        _searchCache = $"{DisplayName} {EditorID} {ModDisplayName} {FormKeyString}".ToLowerInvariant();
    }

    public KeywordRecord KeywordRecord { get; }

    public string EditorID => KeywordRecord.EditorID ?? "(No EditorID)";
    public string DisplayName => KeywordRecord.DisplayName;
    public string ModDisplayName => KeywordRecord.ModDisplayName;
    public string FormKeyString => KeywordRecord.FormKeyString;
    public FormKey FormKey => KeywordRecord.FormKey;

    [Reactive] public bool IsSelected { get; set; }

    public bool MatchesSearch(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return true;

        return _searchCache.Contains(searchTerm.Trim().ToLowerInvariant());
    }
}

