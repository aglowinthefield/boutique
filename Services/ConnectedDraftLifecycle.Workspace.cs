using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;

namespace Boutique.Services;

public sealed partial class ConnectedDraftLifecycle
{
  private readonly Dictionary<Guid, Action> _workspaceReaders = [];
  private readonly Dictionary<Guid, Action> _workspaceReceipts = [];
  private readonly Dictionary<Guid, FormKey> _persistedReferences = [];

  public void EnsureEditable() => RequireEditable();

  private DraftReference WorkspaceReference(DraftReference reference)
  {
    if (reference.DraftId is not { } id)
    {
      return reference;
    }

    if (_persistedReferences.TryGetValue(id, out var key))
    {
      return new(key);
    }

    if (_drafts.TryGetValue(id, out var draft) && draft.Current.OutputIdentity is { } identity)
    {
      return new(identity.FormKey);
    }

    return reference;
  }

  public void Bind(OutfitDraftViewModel draft)
  {
    RequireEditable();
    DraftEntry[] Read() => draft.GetPieces().Select(p => new DraftEntry(new(p.Armor.FormKey)))
      .Concat(draft.GetLeveledListReferences().Select(r =>
      {
        var reference = r.ToRef();
        return new DraftEntry(WorkspaceReference(new(reference.ExistingFormKey, reference.DraftId)));
      })).ToArray();
    var initial = Read();
    if (draft.FormKey is { } source)
    {
      Import(DraftRecordKind.Outfit, source, draft.Id);
    }
    else
    {
      AddOutfit(draft.EditorId, initial.Select(e => e.Reference), draft.Id);
    }

    draft.Name = GetDraft(draft.Id).EditorId;
    var raw = GetDraft(draft.Id).Entries;
    var safe = raw.Count == initial.Length && raw.Select(e => e.Reference).SequenceEqual(initial.Select(e => e.Reference)) &&
      initial.Select(e => e.Reference).Distinct().Count() == initial.Length;
    var slots = raw.Select(e => (e.Reference, Slot: e.RawSlot)).ToArray();
    draft.EnsureCanEdit = RequireEditable;
    draft.EnsureCanEditContent = () =>
    {
      RequireEditable();
      if (!safe)
      {
        throw new InvalidOperationException("This outfit contains opaque, duplicate or reordered source slots. Only renaming is safe in this projection.");
      }
    };
    _workspaceReaders[draft.Id] = () =>
    {
      Rename(draft.Id, draft.EditorId);
      if (!safe)
      {
        return;
      }

      SetEntries(draft.Id, Read().Select(e =>
      {
        var slot = Array.FindIndex(slots, s => s.Reference == e.Reference);
        return e with { RawSlot = slot >= 0 ? slots[slot].Slot : null };
      }));
    };
    _workspaceReceipts[draft.Id] = () =>
    {
      if (!_drafts.TryGetValue(draft.Id, out var state))
      {
        return;
      }

      draft.ApplyCommittedIdentity(state.Current.OutputIdentity?.FormKey ?? draft.FormKey);
      if (draft.FormKey is { } key)
      {
        _persistedReferences[draft.Id] = key;
      }

      slots = state.Current.Entries.Select(e => (e.Reference, Slot: e.RawSlot)).ToArray();
    };
  }

  public void Bind(LeveledListDraftViewModel draft)
  {
    RequireEditable();
    DraftEntry Read(LeveledListEntryViewModel entry)
    {
      var r = entry.ToRequest();
      return new(WorkspaceReference(new(r.ItemFormKey, r.DraftListId)), r.Level, r.Count);
    }

    var initial = draft.GetEntries();
    if (draft.FormKey is { } source)
    {
      Import(DraftRecordKind.LeveledList, source, draft.Id);
    }
    else
    {
      AddList(draft.EditorId, initial.Select(Read), draft.UseAll, draft.Id);
    }

    draft.EditorId = GetDraft(draft.Id).EditorId;
    draft.UseAll = GetDraft(draft.Id).UseAll;
    var raw = GetDraft(draft.Id).Entries;
    var safe = raw.Count == initial.Count && raw.Select(e => e with { RawSlot = null }).SequenceEqual(initial.Select(Read));
    var slots = initial.Select((entry, i) => (entry, Slot: i < raw.Count ? raw[i].RawSlot : null)).ToDictionary(p => p.entry, p => p.Slot);
    draft.EnsureCanEdit = RequireEditable;
    draft.EnsureCanEditContent = () =>
    {
      RequireEditable();
      if (!safe)
      {
        throw new InvalidOperationException("This list contains opaque source entries. Only rename and UseAll are safe in this projection.");
      }
    };
    _workspaceReaders[draft.Id] = () =>
    {
      Rename(draft.Id, draft.EditorId);
      SetUseAll(draft.Id, draft.UseAll);
      if (safe)
      {
        SetEntries(draft.Id, draft.GetEntries().Select(e => Read(e) with { RawSlot = slots.TryGetValue(e, out var i) ? i : null }));
      }
    };
    _workspaceReceipts[draft.Id] = () =>
    {
      if (!_drafts.TryGetValue(draft.Id, out var state))
      {
        return;
      }

      draft.ApplyCommittedIdentity(state.Current.OutputIdentity?.FormKey ?? draft.FormKey);
      if (draft.FormKey is { } key)
      {
        _persistedReferences[draft.Id] = key;
      }

      if (!safe)
      {
        return;
      }

      slots.Clear();
      var current = draft.GetEntries();
      for (var i = 0; i < current.Count && i < state.Current.Entries.Count; i++)
      {
        slots[current[i]] = state.Current.Entries[i].RawSlot;
      }
    };
  }
}
