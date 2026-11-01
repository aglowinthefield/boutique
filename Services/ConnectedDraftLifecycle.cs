using System.IO;
using System.Security.Cryptography;
using Boutique.Models;
using Mutagen.Bethesda.Plugins;

namespace Boutique.Services;

/// <summary>
/// Owns the connected workspace, structural baselines, output association, explicit deletion intentions,
/// and the commit/recovery gate. Returned snapshots are immutable; mutations must cross this interface.
/// </summary>
public sealed partial class ConnectedDraftLifecycle(PatchingService patching)
{
  private sealed class Draft(ConnectedDraftState current, ConnectedDraftState? baseline)
  {
    public ConnectedDraftState Current = current;
    public ConnectedDraftState? Baseline = baseline;
  }

  private readonly Dictionary<Guid, Draft> _drafts = [];
  private readonly HashSet<OutputRecordIdentity> _deletions = [];
  private readonly object _sync = new();
  private string? _outputPath;
  private string? _fingerprint;
  private bool _busy;
  private bool _recovery;

  public event Action? StateChanged;
  public bool IsBusy => _busy;
  public bool NeedsRefresh => _recovery;
  public bool CanMutate => !_busy && !_recovery;
  public string? OutputPath => _outputPath;
  public bool HasPendingDeletions => _deletions.Count != 0;
  public bool HasUnsavedChanges
  {
    get
    {
      lock (_sync)
      {
        if (CanMutate)
        {
          PullWorkspace();
        }

        return HasPendingDeletions || _drafts.Values.Any(d => Snapshot(d).IsDirty);
      }
    }
  }

  private void PullWorkspace()
  {
    foreach (var (id, read) in _workspaceReaders)
    {
      if (_drafts.TryGetValue(id, out var draft) && (draft.Current.OutputIdentity is not { } identity || !_deletions.Contains(identity)))
      {
        read();
      }
    }
  }

  public IReadOnlyList<ConnectedDraftState> Drafts => _drafts.Values.Select(Snapshot).ToArray();
  public ConnectedDraftState GetDraft(Guid id) => Snapshot(_drafts[id]);

  public void SetOutputPath(string path)
  {
    lock (_sync)
    {
      RequireEditable();
      path = Path.GetFullPath(path);
      if (string.Equals(path, _outputPath, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      if (_drafts.Count != 0 || HasPendingDeletions)
      {
        throw new InvalidOperationException("Remove workspace drafts before switching the output path.");
      }

      var fingerprint = Fingerprint(path);
      _outputPath = path;
      _fingerprint = fingerprint;
    }
  }

  public Guid AddOutfit(string editorId, IEnumerable<DraftReference> items, Guid? draftId = null) =>
    Add(DraftRecordKind.Outfit, editorId, items.Select(r => new DraftEntry(r)), false, draftId);

  public Guid AddList(string editorId, IEnumerable<DraftEntry> entries, bool useAll = true, Guid? draftId = null) =>
    Add(DraftRecordKind.LeveledList, editorId, entries, useAll, draftId);

  private Guid Add(DraftRecordKind kind, string editorId, IEnumerable<DraftEntry> entries, bool useAll, Guid? id)
  {
    lock (_sync)
    {
      RequireEditable();
      var state = new ConnectedDraftState(id ?? Guid.NewGuid(), kind, editorId, Freeze(entries), useAll, null, null, false);
      _drafts.Add(state.DraftId, new Draft(state, null));
      return state.DraftId;
    }
  }

  /// <summary>Imports the actual output record if present, otherwise a complete source override.</summary>
  public Guid Import(DraftRecordKind kind, FormKey source, Guid? draftId = null)
  {
    lock (_sync)
    {
      RequireEditable();
      var path = RequirePath();
      var state = patching.ReadConnectedDraft(path, kind, source, draftId ?? Guid.NewGuid());
      _drafts.Add(state.DraftId, new Draft(state, state));
      return state.DraftId;
    }
  }

  public void Rename(Guid id, string editorId) => Update(id, d => d with { EditorId = editorId });
  public void SetUseAll(Guid id, bool value) => Update(id, d => d with { UseAll = value });
  public void SetEntries(Guid id, IEnumerable<DraftEntry> entries) => Update(id, d => d with { Entries = Freeze(entries) });

  private void Update(Guid id, Func<ConnectedDraftState, ConnectedDraftState> change)
  {
    lock (_sync)
    {
      RequireEditable();
      if (_drafts[id].Current.OutputIdentity is { } identity && _deletions.Contains(identity))
      {
        throw new InvalidOperationException("This record is queued for deletion.");
      }

      _drafts[id].Current = change(_drafts[id].Current);
    }
  }

  public void RemoveDraft(Guid id)
  {
    lock (_sync)
    {
      RequireEditable();
      PullWorkspace();
      var target = _drafts[id].Current;
      var incoming = _drafts.Values.Where(d => d.Current.DraftId != id &&
        d.Current.Entries.Any(e => e.Reference.DraftId == id)).ToArray();
      if (incoming.Length > 0 && target.OutputIdentity == null)
      {
        throw new InvalidOperationException("Referenced by: " + string.Join(", ", incoming.Select(d => d.Current.EditorId)));
      }

      if (target.OutputIdentity is { } identity)
      {
        foreach (var draft in incoming)
        {
          draft.Current = Normalize(draft.Current, new Dictionary<Guid, FormKey> { [id] = identity.FormKey });
          if (draft.Baseline != null)
          {
            draft.Baseline = Normalize(draft.Baseline, new Dictionary<Guid, FormKey> { [id] = identity.FormKey });
          }
        }
      }

      _drafts.Remove(id);
      _workspaceReaders.Remove(id);
      _workspaceReceipts.Remove(id);
    }
  }

  /// <summary>Call only after explicit UI confirmation. No disk mutation occurs here.</summary>
  public void QueueOutputRecordDeletion(Guid id)
  {
    lock (_sync)
    {
      RequireEditable();
      _deletions.Add(_drafts[id].Current.OutputIdentity
        ?? throw new InvalidOperationException("This draft has no record persisted in this output."));
    }

    StateChanged?.Invoke();
  }

  public async Task<ConnectedSaveResult> SaveAsync(SaveIntent intent)
  {
    ConnectedWriteDraft[] drafts;
    OutputRecordIdentity[] deletions;
    string path;
    lock (_sync)
    {
      if (_busy || _recovery)
      {
        return new(ConnectedSaveStatus.Blocked, "Save/refresh is already pending. Retry refresh first.");
      }

      if (intent == SaveIntent.Autosave && HasPendingDeletions)
      {
        return new(ConnectedSaveStatus.AutosaveDeferred, "Pending output deletions require explicit Save.");
      }

      try
      {
        PullWorkspace();
      }
      catch (Exception ex)
      {
        return new(ConnectedSaveStatus.Blocked, ex.Message);
      }
      if (!HasUnsavedChanges)
      {
        return new(ConnectedSaveStatus.NothingToSave, "Nothing to save.");
      }

      path = RequirePath();
      drafts = _drafts.Values.Select(d => new ConnectedWriteDraft(Snapshot(d), d.Baseline?.Entries)).ToArray();
      deletions = _deletions.ToArray();
      _busy = true;
    }

    try
    {
      StateChanged?.Invoke();
      var receipt = await patching.CommitConnectedAsync(path, _fingerprint, drafts, deletions);

      // The replacement succeeded. Reconcile before ANY refresh or caller notification can fail.
      lock (_sync)
      {
        foreach (var draft in _drafts.Values)
        {
          var state = Normalize(draft.Current, receipt.Identities);
          if (receipt.Identities.TryGetValue(state.DraftId, out var key))
          {
            state = state with
            {
              OutputIdentity = new(path, state.Kind, key),
              Entries = Freeze(state.Entries.Select((entry, index) => entry with { RawSlot = index }))
            };
            draft.Baseline = state;
          }
          else if (draft.Baseline != null)
          {
            draft.Baseline = Normalize(draft.Baseline, receipt.Identities);
          }

          draft.Current = state;
        }

        foreach (var deleted in receipt.Deletions)
        {
          _deletions.Remove(deleted);
          foreach (var id in _drafts.Where(p => p.Value.Current.OutputIdentity == deleted).Select(p => p.Key).ToArray())
          {
            _drafts.Remove(id);
            _workspaceReaders.Remove(id);
            _workspaceReceipts.Remove(id);
          }
        }

        _fingerprint = receipt.Fingerprint;
        _recovery = true;
      }

      try
      {
        if (Fingerprint(path) != _fingerprint)
        {
          throw new InvalidOperationException("Output changed externally after replacement.");
        }

        foreach (var apply in _workspaceReceipts.Values)
        {
          apply();
        }

        await patching.RefreshConnectedAsync(path);
        _recovery = false;
        return new(ConnectedSaveStatus.Saved, "Saved. Inspected output, open drafts and loaded plugins; unloaded plugins were not inspected.", receipt.Deletions);
      }
      catch (Exception ex)
      {
        return new(ConnectedSaveStatus.SavedButRefreshFailed, "Saved, but refresh failed: " + ex.Message, receipt.Deletions);
      }
    }
    catch (Exception ex)
    {
      _recovery = patching.OutputRefreshRequired;
      return new(ConnectedSaveStatus.WriteFailed, ex.Message);
    }
    finally
    {
      _busy = false;
      try
      {
        StateChanged?.Invoke();
      }
      catch
      { /* A notification cannot change the commit outcome. */
      }
    }
  }

  public async Task<ConnectedSaveResult> RetryRefreshAsync()
  {
    lock (_sync)
    {
      if (_busy)
      {
        return new(ConnectedSaveStatus.Blocked, "Save/refresh is already running.");
      }

      if (!_recovery)
      {
        return new(ConnectedSaveStatus.NothingToSave, "Refresh is not required.");
      }

      _busy = true;
    }

    try
    {
      foreach (var apply in _workspaceReceipts.Values)
      {
        apply();
      }

      if (Fingerprint(RequirePath()) != _fingerprint)
      {
        throw new InvalidOperationException("Output changed externally during recovery. Reopen the workspace; refresh cannot accept stale baselines.");
      }

      await patching.RefreshConnectedAsync(RequirePath(), requireOutput: _fingerprint != null);
      _recovery = false;
      return new(ConnectedSaveStatus.Saved, "Refresh recovered; no plugin was written.");
    }
    catch (Exception ex)
    {
      return new(ConnectedSaveStatus.SavedButRefreshFailed, "Refresh failed: " + ex.Message);
    }
    finally
    {
      _busy = false;
      try
      {
        StateChanged?.Invoke();
      }
      catch
      {
      }
    }
  }

  private ConnectedDraftState Snapshot(Draft d) => d.Current with
  {
    // A source import requests an output override even when its contents have not changed.
    // Keep its raw baseline for lossless copying; only an actual output identity can make it clean.
    IsDirty = d.Current.ImportSource.HasValue && d.Current.OutputIdentity == null ||
      (d.Baseline == null ? d.Current.Entries.Count != 0 :
        d.Current.EditorId != d.Baseline.EditorId ||
        d.Current.UseAll != d.Baseline.UseAll || !d.Current.Entries.SequenceEqual(d.Baseline.Entries))
  };

  private static ConnectedDraftState Normalize(ConnectedDraftState state, IReadOnlyDictionary<Guid, FormKey> map) =>
    state with
    {
      Entries = Freeze(state.Entries.Select(e => e.Reference.DraftId is { } id && map.TryGetValue(id, out var key)
      ? e with { Reference = new(key) } : e))
    };

  private static IReadOnlyList<DraftEntry> Freeze(IEnumerable<DraftEntry> entries) => Array.AsReadOnly(entries.ToArray());
  private string RequirePath() => _outputPath ?? throw new InvalidOperationException("Configure an output path first.");
  private void RequireEditable()
  {
    if (!CanMutate)
    {
      throw new InvalidOperationException("Connected drafts are frozen during save or refresh recovery.");
    }
  }

  internal static string? Fingerprint(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
}
