using Boutique.Models;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class NpcRecordViewModel(NpcRecord npcRecord) : SelectableRecordViewModel<NpcRecord>(npcRecord)
{
  [Reactive] private string? _conflictingFileName;

  [Reactive] private bool _hasConflict;

  [Reactive] private string? _overlappingFileName;

  [Reactive] private bool _hasOverlap;

  public NpcRecord NpcRecord => Record;
}
