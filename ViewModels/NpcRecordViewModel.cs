using Boutique.Models;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class NpcRecordViewModel(NpcRecord npcRecord) : SelectableRecordViewModel<NpcRecord>(npcRecord)
{
  [Reactive] private string? _conflictingFileName;

  [Reactive] private bool _hasConflict;

  public NpcRecord NpcRecord => Record;
}
