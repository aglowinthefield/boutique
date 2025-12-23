using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Boutique.ViewModels;

public class NpcRecordViewModel : ReactiveObject, ISelectableRecordViewModel
{
    private readonly string _searchCache;

    public NpcRecordViewModel(NpcRecord npcRecord)
    {
        NpcRecord = npcRecord;
        _searchCache = $"{DisplayName} {EditorID} {ModDisplayName} {FormKeyString}".ToLowerInvariant();

        // Update ConflictTooltip when HasConflict or ConflictingFileName changes
        this.WhenAnyValue(x => x.HasConflict, x => x.ConflictingFileName)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(ConflictTooltip)));
    }

    public NpcRecord NpcRecord { get; }

    public string EditorID => NpcRecord.EditorID ?? "(No EditorID)";
    public string DisplayName => NpcRecord.DisplayName;
    public string ModDisplayName => NpcRecord.ModDisplayName;
    public string FormKeyString => NpcRecord.FormKeyString;
    public FormKey FormKey => NpcRecord.FormKey;

    [Reactive] public bool IsSelected { get; set; }

    /// <summary>
    /// Indicates whether this NPC has a conflicting outfit distribution in an existing file.
    /// </summary>
    [Reactive] public bool HasConflict { get; set; }

    /// <summary>
    /// The name of the file that has a conflicting distribution for this NPC.
    /// </summary>
    [Reactive] public string? ConflictingFileName { get; set; }

    /// <summary>
    /// Tooltip text for the conflict warning.
    /// </summary>
    public string ConflictTooltip => HasConflict && !string.IsNullOrEmpty(ConflictingFileName)
        ? $"Conflict: This NPC already has an outfit distribution in '{ConflictingFileName}'"
        : string.Empty;

    public bool MatchesSearch(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return true;

        return _searchCache.Contains(searchTerm.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
