using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class SelectableRecordViewModel<TRecord> : ReactiveObject, ISelectableRecordViewModel
    where TRecord : IGameRecord
{
    private string? _searchCache;

    [Reactive] private bool _isExcluded;

    [Reactive] private bool _isSelected;

    public SelectableRecordViewModel(TRecord record)
    {
        Record = record;
    }

    public TRecord Record { get; }

    public string EditorID => Record.EditorID ?? "(No EditorID)";
    public string DisplayName => Record.DisplayName;
    public string ModDisplayName => Record.ModDisplayName;
    public string FormKeyString => Record.FormKeyString;
    public FormKey FormKey => Record.FormKey;

    public bool MatchesSearch(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return true;
        }

        _searchCache ??= $"{DisplayName} {EditorID} {ModDisplayName} {FormKeyString}".ToLowerInvariant();
        return _searchCache.Contains(searchTerm.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
