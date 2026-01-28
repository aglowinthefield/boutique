using Boutique.Models;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class NpcRecordViewModel : SelectableRecordViewModel<NpcRecord>
{
    [Reactive] private string? _conflictingFileName;

    [Reactive] private bool _hasConflict;

    public NpcRecordViewModel(NpcRecord npcRecord)
        : base(npcRecord)
    {
    }

    public NpcRecord NpcRecord => Record;

    public string ConflictTooltip => HasConflict && !string.IsNullOrEmpty(ConflictingFileName)
        ? $"Conflict: This NPC already has an outfit distribution in '{ConflictingFileName}'"
        : string.Empty;
}
