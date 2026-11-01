using System.IO;
using Boutique.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public partial class PatchingService
{
  private readonly IConnectedDraftEnvironment _draftEnvironment = draftEnvironment ?? new MutagenConnectedDraftEnvironment(mutagenService);
  private readonly SemaphoreSlim _outputTransaction = new(1, 1);
  private IDisposable AcquireOutputTransaction()
  {
    RequireOutputWritesReady();
    if (!_outputTransaction.Wait(0))
    {
      throw new InvalidOperationException("Another output transaction is already running.");
    }

    try
    {
      RequireOutputWritesReady();
    }
    catch
    {
      _outputTransaction.Release();
      throw;
    }
    return System.Reactive.Disposables.Disposable.Create(() => _outputTransaction.Release());
  }

  private int _connectedWriting;
  private volatile bool _outputRefreshRequired;
  internal bool OutputRefreshRequired => _outputRefreshRequired;

  private void RequireOutputWritesReady()
  {
    if (_outputRefreshRequired || Volatile.Read(ref _connectedWriting) != 0)
    {
      throw new InvalidOperationException("Output writes are blocked until connected save/refresh recovery completes.");
    }
  }

  internal ConnectedDraftState ReadConnectedDraft(string path, DraftRecordKind kind, FormKey source, Guid id)
  {
    SkyrimMod? output = File.Exists(path) ? SkyrimMod.CreateFromBinary(path, _draftEnvironment.Release) : null;
    var persisted = output?.EnumerateMajorRecords().FirstOrDefault(r => r.FormKey == source && MatchesKind(r, kind));
    var record = persisted ?? FindSource(source, kind);
    var entries = record switch
    {
      IOutfitGetter outfit => (outfit.Items ?? []).Select((r, i) => new DraftEntry(new(r.FormKey), RawSlot: i)).ToArray(),
      ILeveledItemGetter list => (list.Entries ?? []).Select((e, i) =>
        new DraftEntry(new(e.Data?.Reference.FormKey ?? FormKey.Null), e.Data?.Level ?? 1, e.Data?.Count ?? 1, i)).ToArray(),
      _ => throw new InvalidOperationException("Unsupported draft record.")
    };
    return new(id, kind, record.EditorID ?? source.ToString(), Array.AsReadOnly(entries),
      record is ILeveledItemGetter l && l.Flags.HasFlag(LeveledItem.Flag.UseAll), source,
      persisted != null ? new(path, kind, source) : null, false);
  }

  private IMajorRecordGetter FindSource(FormKey key, DraftRecordKind kind) =>
    _draftEnvironment.LoadedPlugins.SelectMany(m => m.EnumerateMajorRecords())
      .FirstOrDefault(r => r.FormKey == key && MatchesKind(r, kind))
    ?? throw new InvalidOperationException($"Source record {key} is unavailable; refusing a lossy override.");

  private static bool MatchesKind(IMajorRecordGetter record, DraftRecordKind kind) =>
    kind == DraftRecordKind.Outfit ? record is IOutfitGetter : record is ILeveledItemGetter;

  internal async Task<ConnectedCommit> CommitConnectedAsync(string path, string? fingerprint,
    IReadOnlyList<ConnectedWriteDraft> drafts, IReadOnlyList<OutputRecordIdentity> deletions)
  {
    using var transaction = AcquireOutputTransaction();
    if (Interlocked.CompareExchange(ref _connectedWriting, 1, 0) != 0)
    {
      throw new InvalidOperationException("A connected save is already running.");
    }

    try
    {
      return await Task.Run(() => BuildAndCommitConnected(path, fingerprint, drafts, deletions));
    }
    finally
    {
      Interlocked.Exchange(ref _connectedWriting, 0);
    }
  }

  private ConnectedCommit BuildAndCommitConnected(string path, string? fingerprint,
    IReadOnlyList<ConnectedWriteDraft> drafts, IReadOnlyList<OutputRecordIdentity> deletions)
  {
    if (ConnectedDraftLifecycle.Fingerprint(path) != fingerprint)
    {
      throw new InvalidOperationException("Output changed externally. Reopen the workspace before saving.");
    }

    var mod = File.Exists(path) ? SkyrimMod.CreateFromBinary(path, _draftEnvironment.Release)
      : new SkyrimMod(ModKey.FromFileName(Path.GetFileName(path)), _draftEnvironment.Release);
    EnsureMinimumFormId(mod);
    var deleted = deletions.Select(d => d.FormKey).ToHashSet();
    if (deletions.Any(d => !string.Equals(d.OutputPath, path, StringComparison.OrdinalIgnoreCase)))
    {
      throw new InvalidOperationException("Deletion belongs to a different output path.");
    }

    var changed = drafts.Where(d => d.State.IsDirty &&
      (d.State.OutputIdentity == null || !deletions.Contains(d.State.OutputIdentity))).ToArray();
    foreach (var draft in changed)
    {
      if (draft.State.Entries.Count == 0)
      {
        throw new InvalidOperationException($"'{draft.State.EditorId}' is empty. Remove the draft or explicitly delete its output record.");
      }
    }

    var map = drafts.Where(d => d.State.OutputIdentity != null)
      .ToDictionary(d => d.State.DraftId, d => d.State.OutputIdentity!.FormKey);
    var records = new Dictionary<Guid, IMajorRecord>();

    // Allocate every changed list before populating any reference (also handles forward references).
    foreach (var draft in changed.OrderBy(d => d.State.Kind == DraftRecordKind.LeveledList ? 0 : 1))
    {
      var state = draft.State;
      IMajorRecord record;
      if (state.OutputIdentity is { } identity)
      {
        record = mod.EnumerateMajorRecords().FirstOrDefault(r => r.FormKey == identity.FormKey && MatchesKind(r, state.Kind))
          ?? throw new InvalidOperationException($"Persisted record {identity.FormKey} is missing; EditorID fallback is forbidden.");
      }
      else if (state.ImportSource is { } source)
      {
        record = state.Kind == DraftRecordKind.Outfit
          ? mod.Outfits.GetOrAddAsOverride((IOutfitGetter)FindSource(source, state.Kind))
          : mod.LeveledItems.GetOrAddAsOverride((ILeveledItemGetter)FindSource(source, state.Kind));
      }
      else
      {
        record = state.Kind == DraftRecordKind.Outfit ? mod.Outfits.AddNew() : mod.LeveledItems.AddNew();
      }

      records.Add(state.DraftId, record);
      map[state.DraftId] = record.FormKey;
    }

    foreach (var draft in changed)
    {
      var record = records[draft.State.DraftId];
      record.EditorID = draft.State.EditorId;
      if (record is LeveledItem list)
      {
        list.Flags = draft.State.UseAll ? list.Flags | LeveledItem.Flag.UseAll : list.Flags & ~LeveledItem.Flag.UseAll;
        if (draft.BaselineEntries == null || !draft.State.Entries.SequenceEqual(draft.BaselineEntries))
        {
          ApplyListEntries(list, draft, map);
        }
      }
      else if (record is Outfit outfit && (draft.BaselineEntries == null || !draft.State.Entries.SequenceEqual(draft.BaselineEntries)))
      {
        outfit.Items = new(draft.State.Entries.Select(e => ResolveConnectedReference(e.Reference, map).ToLink<IOutfitTargetGetter>()));
      }
    }

    foreach (var deletion in deletions)
    {
      if (deletion.Kind == DraftRecordKind.Outfit)
      {
        var record = mod.Outfits.FirstOrDefault(r => r.FormKey == deletion.FormKey)
          ?? throw new InvalidOperationException($"Deletion target {deletion.FormKey} is missing.");
        mod.Outfits.Remove(record);
      }
      else
      {
        var record = mod.LeveledItems.FirstOrDefault(r => r.FormKey == deletion.FormKey)
          ?? throw new InvalidOperationException($"Deletion target {deletion.FormKey} is missing.");
        mod.LeveledItems.Remove(record);
      }
    }

    // Materialize all raw links before releasing overlays. Do not resolve or collapse to winners.
    var dependents = new SortedSet<string>(StringComparer.Ordinal);
    void Inspect(ISkyrimModGetter plugin)
    {
      foreach (var record in plugin.EnumerateMajorRecords())
      {
        foreach (var link in record.EnumerateFormLinks())
        {
          if (!link.IsNull && deleted.Contains(link.FormKey))
          {
            dependents.Add($"{plugin.ModKey}: {record.EditorID ?? record.FormKey.ToString()} ({record.FormKey}) → {link.FormKey}");
          }
        }
      }
    }

    Inspect(mod);
    foreach (var plugin in _draftEnvironment.LoadedPlugins)
    {
      if (plugin.ModKey != mod.ModKey)
      {
        Inspect(plugin);
      }
    }

    foreach (var draft in drafts.Where(d => d.State.OutputIdentity == null || !deletions.Contains(d.State.OutputIdentity)))
    {
      foreach (var entry in draft.State.Entries)
      {
        // Null raw slots are preserved opaque content, not a newly entered invalid reference.
        if (entry.Reference.FormKey == FormKey.Null && entry.RawSlot.HasValue)
        {
          continue;
        }

        var key = ResolveConnectedReference(entry.Reference, map);
        if (deleted.Contains(key))
        {
          dependents.Add($"Open draft '{draft.State.EditorId}' → {key}");
        }
      }
    }

    if (dependents.Count > 0)
    {
      throw new InvalidOperationException("Deletion blocked. Dependents:\n" + string.Join("\n", dependents) +
        "\nInspected actual output, open drafts and loaded plugins. Unloaded plugins are outside inspection scope.");
    }

    var masters = new HashSet<ModKey> { SkyrimMaster };
    foreach (var record in mod.EnumerateMajorRecords())
    {
      if (record.FormKey.ModKey != mod.ModKey)
      {
        masters.Add(record.FormKey.ModKey);
      }

      foreach (var link in record.EnumerateFormLinks())
      {
        if (!link.IsNull && link.FormKey.ModKey != mod.ModKey)
        {
          masters.Add(link.FormKey.ModKey);
        }
      }
    }

    EnsureMasters(mod, masters);
    CleanupMasterReferences(mod, masters);
    TryApplyEslFlag(mod);
    var receipt = new ConnectedCommit(changed.ToDictionary(d => d.State.DraftId, d => map[d.State.DraftId]), deletions.ToArray());

    // Verify again immediately before release/replace; never overwrite a known external edit.
    if (ConnectedDraftLifecycle.Fingerprint(path) != fingerprint)
    {
      throw new InvalidOperationException("Output changed externally during save.");
    }

    _outputRefreshRequired = true;
    _draftEnvironment.ReleaseHandles();
    WritePatchWithRetry(mod, path, masters, () =>
    {
      if (ConnectedDraftLifecycle.Fingerprint(path) != fingerprint)
      {
        throw new InvalidOperationException("Output changed externally before replacement.");
      }
    }, prepared: digest => receipt.Fingerprint = digest);
    return receipt; // Nothing fallible after the atomic replacement.
  }

  private static void ApplyListEntries(LeveledItem list, ConnectedWriteDraft draft, Dictionary<Guid, FormKey> map)
  {
    var original = (list.Entries ?? []).ToArray();
    var used = new HashSet<int>();
    var entries = new List<LeveledItemEntry>();
    foreach (var entry in draft.State.Entries)
    {
      LeveledItemEntry raw;
      if (entry.RawSlot is { } slot)
      {
        if (slot < 0 || slot >= original.Length || !used.Add(slot))
        {
          throw new InvalidOperationException("Cannot safely map preserved list entry slots. Reimport the record.");
        }

        raw = original[slot];
        if (raw.Data == null)
        {
          if (entry.Reference.FormKey != FormKey.Null || entry.Level != 1 || entry.Count != 1)
          {
            throw new InvalidOperationException("Cannot edit opaque list entry without losing data.");
          }

          entries.Add(raw);
          continue;
        }
      }
      else
      {
        raw = new LeveledItemEntry { Data = new LeveledItemEntryData() };
      }

      if (entry.RawSlot.HasValue && raw.Data!.Reference.IsNull && entry.Reference.FormKey == FormKey.Null)
      {
        if (entry.Level != raw.Data.Level || entry.Count != raw.Data.Count)
        {
          throw new InvalidOperationException("Cannot edit an opaque null-reference entry.");
        }

        entries.Add(raw);
        continue;
      }

      raw.Data!.Reference.SetTo(ResolveConnectedReference(entry.Reference, map));
      raw.Data.Level = entry.Level;
      raw.Data.Count = entry.Count;
      entries.Add(raw);
    }

    list.Entries = new(entries);
  }

  private static FormKey ResolveConnectedReference(DraftReference reference, IReadOnlyDictionary<Guid, FormKey> map)
  {
    if (reference.DraftId is { } id)
    {
      return map.TryGetValue(id, out var key) ? key : throw new InvalidOperationException($"Unresolved draft reference {id}.");
    }

    return reference.FormKey is { } form && form != FormKey.Null ? form
      : throw new InvalidOperationException("Unresolved entry reference; refusing to skip content.");
  }

  internal async Task RefreshConnectedAsync(string path, bool requireOutput = true)
  {
    await _draftEnvironment.RefreshAsync(path);
    _ = _draftEnvironment.LoadedPlugins;
    if (requireOutput)
    {
      using var output = SkyrimMod.CreateFromBinaryOverlay(path, _draftEnvironment.Release);
      foreach (var record in output.EnumerateMajorRecords())
      {
        _ = record.EnumerateFormLinks().Count();
      }
    }

    _outputRefreshRequired = false;
  }
}
