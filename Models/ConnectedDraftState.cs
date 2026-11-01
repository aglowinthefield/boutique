using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public enum DraftRecordKind
{
  Outfit,
  LeveledList
}
public enum SaveIntent
{
  Explicit,
  Autosave
}
public enum ConnectedSaveStatus
{
  NothingToSave,
  AutosaveDeferred,
  Blocked,
  WriteFailed,
  Saved,
  SavedButRefreshFailed
}
public sealed record OutputRecordIdentity(string OutputPath, DraftRecordKind Kind, FormKey FormKey);
public sealed record DraftReference(FormKey? FormKey = null, Guid? DraftId = null);

// RawSlot identifies the original entry object, not its reference (duplicates are legal).
public sealed record DraftEntry(DraftReference Reference, short Level = 1, short Count = 1, int? RawSlot = null);
public sealed record ConnectedDraftState(
  Guid DraftId, DraftRecordKind Kind, string EditorId, IReadOnlyList<DraftEntry> Entries,
  bool UseAll, FormKey? ImportSource, OutputRecordIdentity? OutputIdentity, bool IsDirty);
public sealed record ConnectedSaveResult(ConnectedSaveStatus Status, string Message,
  IReadOnlyList<OutputRecordIdentity>? DeletedRecords = null);

internal sealed record ConnectedWriteDraft(ConnectedDraftState State, IReadOnlyList<DraftEntry>? BaselineEntries);
internal sealed record ConnectedCommit(IReadOnlyDictionary<Guid, FormKey> Identities,
  IReadOnlyList<OutputRecordIdentity> Deletions)
{
  public string? Fingerprint { get; set; }
}
