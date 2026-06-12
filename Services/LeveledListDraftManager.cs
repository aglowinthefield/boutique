using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using DynamicData;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using Serilog;

namespace Boutique.Services;

/// <summary>
/// Manages the leveled list creation queue, tracking draft leveled lists and their entries (armors or
/// nested leveled lists). EditorIDs are auto-generated from the first entry and kept unique in the queue.
/// </summary>
public sealed class LeveledListDraftManager : ReactiveObject, IDisposable
{
  private const string EditorIdPrefix = "LItemBTQ";

  private readonly CompositeDisposable                   _disposables = new();
  private readonly ILogger                               _logger;
  private readonly SourceList<LeveledListDraftViewModel> _source = new();

  public LeveledListDraftManager(ILoggingService loggingService)
  {
    _logger = loggingService.ForContext<LeveledListDraftManager>();

    _disposables.Add(
      _source.Connect()
             .ObserveOn(RxApp.MainThreadScheduler)
             .Bind(out var drafts)
             .Subscribe());
    Drafts = drafts;

    _disposables.Add(
      _source.Connect()
             .Select(_ => _source.Count > 0)
             .Subscribe(hasDrafts => HasDrafts = hasDrafts));
  }

  public ReadOnlyObservableCollection<LeveledListDraftViewModel> Drafts { get; }

  public bool HasDrafts
  {
    get => field;
    private set => this.RaiseAndSetIfChanged(ref field, value);
  }

  public bool SuppressAutoSave { get; set; }

  public void Dispose()
  {
    _disposables.Dispose();
    _source.Dispose();
    GC.SuppressFinalize(this);
  }

  public event Action<string>? StatusChanged;
  public event Action? DraftModified;

  public LeveledListDraftViewModel? CreateDraft(IReadOnlyList<ArmorRecordViewModel> pieces)
  {
    var distinct = pieces.GroupBy(p => p.Armor.FormKey).Select(g => g.First()).ToList();
    if (distinct.Count == 0)
    {
      RaiseStatus("Select at least one armor to create a leveled list.");
      return null;
    }

    var editorId = EnsureUniqueEditorId(GenerateEditorId(distinct[0]));
    var draft    = new LeveledListDraftViewModel(editorId, RemoveDraft);
    Attach(draft);

    foreach (var piece in distinct)
    {
      draft.AddArmorEntry(piece);
    }

    _source.Add(draft);

    RaiseStatus($"Added leveled list '{draft.EditorId}' with {distinct.Count} entry/entries.");
    _logger.Information("Added leveled list {EditorId} with {EntryCount} entries.", draft.EditorId, distinct.Count);

    return draft;
  }

  /// <summary>Adds a nested leveled list as an entry of <paramref name="parent"/>.</summary>
  public bool AddNestedList(LeveledListDraftViewModel parent, LeveledListDraftViewModel child)
  {
    if (ReferenceEquals(parent, child))
    {
      RaiseStatus("A leveled list cannot contain itself.");
      return false;
    }

    var reference = new LeveledListReference(child);
    if (parent.ContainsEntry(reference.DedupKey))
    {
      RaiseStatus($"'{child.EditorId}' is already in '{parent.EditorId}'.");
      return false;
    }

    parent.AddListEntry(reference);
    RaiseStatus($"Added '{child.EditorId}' to leveled list '{parent.EditorId}'.");
    return true;
  }

  public bool AddArmorsToDraft(LeveledListDraftViewModel draft, IReadOnlyList<ArmorRecordViewModel> pieces)
  {
    var added = 0;
    foreach (var piece in pieces.GroupBy(p => p.Armor.FormKey).Select(g => g.First()))
    {
      if (draft.ContainsEntry(piece.Armor.FormKey.ToString()))
      {
        continue;
      }

      draft.AddArmorEntry(piece);
      added++;
    }

    if (added == 0)
    {
      RaiseStatus($"No new entries added to '{draft.EditorId}'.");
      return false;
    }

    RaiseStatus($"Added {added} entry/entries to '{draft.EditorId}'.");
    return true;
  }

  public void RemoveDraft(LeveledListDraftViewModel draft)
  {
    Detach(draft);
    if (!_source.Remove(draft))
    {
      return;
    }

    RaiseStatus($"Removed leveled list '{draft.EditorId}'.");
    _logger.Information("Removed leveled list draft {EditorId}.", draft.EditorId);
    RaiseDraftModified();
  }

  /// <summary>Imports existing leveled lists from the load order as editable override drafts.</summary>
  public int AddDraftsFromLeveledItems(
    IEnumerable<ILeveledItemGetter> leveledItems,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    bool skipExistingFormKeys = true)
  {
    var existingKeys = _source.Items
                              .Where(d => d.FormKey.HasValue)
                              .Select(d => d.FormKey!.Value)
                              .ToHashSet();

    var added = 0;
    foreach (var leveledItem in leveledItems)
    {
      if (skipExistingFormKeys && existingKeys.Contains(leveledItem.FormKey))
      {
        continue;
      }

      var editorId = InputPatterns.Identifier.SanitizeOrDefault(
        leveledItem.EditorID ?? leveledItem.FormKey.ToString(),
        "LeveledList");
      editorId = EnsureUniqueEditorId(editorId);

      var draft = new LeveledListDraftViewModel(editorId, RemoveDraft)
                  {
                    FormKey = leveledItem.FormKey,
                    UseAll  = leveledItem.Flags.HasFlag(LeveledItem.Flag.UseAll)
                  };
      Attach(draft);
      PopulateFromLeveledItem(draft, leveledItem, linkCache);
      _source.Add(draft);

      existingKeys.Add(leveledItem.FormKey);
      added++;
    }

    if (added > 0)
    {
      _logger.Information("Imported {Count} existing leveled list(s).", added);
    }

    return added;
  }

  public void ClearDraftsFromOtherPlugins(ModKey targetModKey)
  {
    var toRemove = _source.Items
                          .Where(d => d.FormKey.HasValue && d.FormKey.Value.ModKey != targetModKey)
                          .ToList();

    foreach (var draft in toRemove)
    {
      Detach(draft);
      _source.Remove(draft);
    }
  }

  public bool HasUnsavedChanges() => _source.Items.Any(d => d.HasEntries);

  public List<LeveledListCreationRequest> BuildSaveRequests() =>
    _source.Items
           .Where(d => d.HasEntries)
           .Select(d => new LeveledListCreationRequest(
                     d.EditorId,
                     [.. d.GetEntries().Select(e => e.ToRequest())],
                     ComputeFlags(d.UseAll),
                     d.FormKey,
                     d.Id))
           .ToList();

  public void ProcessSaveResults(IReadOnlyList<LeveledListCreationResult> results)
  {
    foreach (var result in results)
    {
      var draft = _source.Items.FirstOrDefault(d =>
        string.Equals(d.EditorId, result.EditorId, StringComparison.OrdinalIgnoreCase));

      if (draft != null && !draft.FormKey.HasValue)
      {
        draft.FormKey = result.FormKey;
      }
    }
  }

  private static void PopulateFromLeveledItem(
    LeveledListDraftViewModel draft,
    ILeveledItemGetter leveledItem,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    foreach (var entry in leveledItem.Entries ?? [])
    {
      var refKey = entry.Data?.Reference.FormKeyNullable;
      if (!refKey.HasValue || refKey.Value == FormKey.Null)
      {
        continue;
      }

      var level = entry.Data?.Level ?? 1;
      var count = entry.Data?.Count ?? 1;

      if (!linkCache.TryResolve<IItemGetter>(refKey.Value, out var item))
      {
        continue;
      }

      switch (item)
      {
        case IArmorGetter armor:
          draft.AddArmorEntry(new ArmorRecordViewModel(armor, linkCache), level, count);
          break;
        case ILeveledItemGetter nested:
          draft.AddListEntry(
            new LeveledListReference(nested.FormKey, nested.EditorID ?? nested.FormKey.ToString()),
            level,
            count);
          break;
      }
    }
  }

  private void Attach(LeveledListDraftViewModel draft)
  {
    draft.PropertyChanged += OnDraftPropertyChanged;
    draft.EntryAdded      += OnEntryAdded;
    draft.EntryRemoved    += OnEntryRemoved;
  }

  private void Detach(LeveledListDraftViewModel draft)
  {
    draft.PropertyChanged -= OnDraftPropertyChanged;
    draft.EntryAdded      -= OnEntryAdded;
    draft.EntryRemoved    -= OnEntryRemoved;
    foreach (var entry in draft.GetEntries())
    {
      entry.PropertyChanged -= OnEntryPropertyChanged;
    }
  }

  private void OnEntryAdded(LeveledListEntryViewModel entry)
  {
    entry.PropertyChanged += OnEntryPropertyChanged;
    RaiseDraftModified();
  }

  private void OnEntryRemoved(LeveledListEntryViewModel entry)
  {
    entry.PropertyChanged -= OnEntryPropertyChanged;
    RaiseDraftModified();
  }

  private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(LeveledListDraftViewModel.EditorId) or nameof(LeveledListDraftViewModel.UseAll))
    {
      RaiseDraftModified();
    }
  }

  private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(LeveledListEntryViewModel.Level) or nameof(LeveledListEntryViewModel.Count))
    {
      RaiseDraftModified();
    }
  }

  private static LeveledItem.Flag ComputeFlags(bool useAll)
  {
    var flags = LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer;
    if (useAll)
    {
      flags |= LeveledItem.Flag.UseAll;
    }

    return flags;
  }

  private static string GenerateEditorId(ArmorRecordViewModel firstPiece)
  {
    var baseName = InputPatterns.Identifier.Sanitize(firstPiece.DisplayName);
    return string.IsNullOrEmpty(baseName) ? EditorIdPrefix : EditorIdPrefix + baseName;
  }

  private string EnsureUniqueEditorId(string baseName)
  {
    var candidate   = baseName;
    var suffixIndex = 0;

    while (_source.Items.Any(d => string.Equals(d.EditorId, candidate, StringComparison.OrdinalIgnoreCase)))
    {
      suffixIndex++;
      candidate = baseName + AlphabetSuffix(suffixIndex);
    }

    return candidate;
  }

  private void RaiseStatus(string message) => StatusChanged?.Invoke(message);

  private void RaiseDraftModified()
  {
    if (SuppressAutoSave)
    {
      return;
    }

    DraftModified?.Invoke();
  }

  private static string AlphabetSuffix(int index)
  {
    if (index <= 0)
    {
      return string.Empty;
    }

    var builder = new StringBuilder();
    while (index > 0)
    {
      index--;
      builder.Insert(0, (char)('A' + (index % 26)));
      index /= 26;
    }

    return builder.ToString();
  }
}
