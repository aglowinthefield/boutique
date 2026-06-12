using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

/// <summary>
/// References a leveled list either by a pending draft (created in the same session) or by an existing
/// FormKey already in the load order. Used both for nesting lists inside lists and embedding lists in outfits.
/// </summary>
public sealed class LeveledListReference
{
  private readonly string _existingName;

  public LeveledListReference(LeveledListDraftViewModel pendingDraft) =>
    PendingDraft = pendingDraft ?? throw new ArgumentNullException(nameof(pendingDraft));

  public LeveledListReference(FormKey existingFormKey, string displayName)
  {
    ExistingFormKey = existingFormKey;
    _existingName   = displayName;
  }

  public LeveledListDraftViewModel? PendingDraft { get; }

  public FormKey? ExistingFormKey { get; }

  public string DisplayName => PendingDraft?.EditorId ?? _existingName;

  public string DedupKey => PendingDraft?.Id.ToString() ?? ExistingFormKey?.ToString() ?? DisplayName;

  public LeveledListRef ToRef()
  {
    if (PendingDraft != null)
    {
      return PendingDraft.FormKey.HasValue
               ? new LeveledListRef(PendingDraft.FormKey)
               : new LeveledListRef(DraftId: PendingDraft.Id);
    }

    return new LeveledListRef(ExistingFormKey);
  }
}

public partial class LeveledListEntryViewModel : ReactiveObject
{
  private readonly ArmorRecordViewModel?  _armor;
  private readonly LeveledListReference?  _listRef;

  [Reactive] private short _count = 1;
  [Reactive] private short _level = 1;
  [Reactive] private bool  _isConflicting;

  private LeveledListEntryViewModel(
    ArmorRecordViewModel? armor,
    LeveledListReference? listRef,
    short level,
    short count,
    Action<LeveledListEntryViewModel> remove)
  {
    _armor        = armor;
    _listRef      = listRef;
    _level        = level;
    _count        = count;
    RemoveCommand = ReactiveCommand.Create(() => remove(this));
  }

  public static LeveledListEntryViewModel ForArmor(
    ArmorRecordViewModel armor,
    Action<LeveledListEntryViewModel> remove,
    short level = 1,
    short count = 1) =>
    new(armor, null, level, count, remove);

  public static LeveledListEntryViewModel ForList(
    LeveledListReference listRef,
    Action<LeveledListEntryViewModel> remove,
    short level = 1,
    short count = 1) =>
    new(null, listRef, level, count, remove);

  public bool IsLeveledList => _listRef != null;

  public string DisplayName => _armor?.DisplayName ?? _listRef?.DisplayName ?? "(unknown)";

  public string SlotSummary => _armor?.SlotSummary ?? "leveled list";

  public BipedObjectFlag SlotMask => _armor?.SlotMask ?? 0;

  public string DedupKey => _armor?.Armor.FormKey.ToString() ?? _listRef!.DedupKey;

  public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

  public LeveledListEntryRequest ToRequest()
  {
    if (_listRef != null)
    {
      var reference = _listRef.ToRef();
      return new LeveledListEntryRequest(reference.ExistingFormKey, Level, Count, reference.DraftId);
    }

    return new LeveledListEntryRequest(_armor!.Armor.FormKey, Level, Count);
  }
}

public partial class LeveledListDraftViewModel : ReactiveObject
{
  private static readonly BipedObjectFlag[] BipedFlags = Enum.GetValues<BipedObjectFlag>()
                                                            .Where(f => f != 0 && ((uint)f & ((uint)f - 1)) == 0)
                                                            .ToArray();

  private readonly ObservableCollection<LeveledListEntryViewModel> _entries = [];
  private readonly Action<LeveledListDraftViewModel>               _removeDraft;

  [Reactive] private FormKey? _formKey;
  [Reactive] private bool     _isExpanded = true;
  [Reactive] private bool     _useAll     = true;

  private string _editorId = string.Empty;

  public LeveledListDraftViewModel(
    string editorId,
    Action<LeveledListDraftViewModel> removeDraft)
  {
    _removeDraft = removeDraft ?? throw new ArgumentNullException(nameof(removeDraft));
    SetEditorIdInternal(editorId);

    _entries.CollectionChanged += EntriesOnCollectionChanged;
    Entries                    =  new ReadOnlyObservableCollection<LeveledListEntryViewModel>(_entries);

    this.WhenAnyValue(x => x.FormKey)
        .Subscribe(_ =>
        {
          this.RaisePropertyChanged(nameof(FormIdDisplay));
          this.RaisePropertyChanged(nameof(Header));
        });

    this.WhenAnyValue(x => x.UseAll).Subscribe(_ => RefreshConflicts());

    RemoveSelfCommand = ReactiveCommand.Create(() => _removeDraft(this));
  }

  public Guid Id { get; } = Guid.NewGuid();

  public event Action<LeveledListEntryViewModel>? EntryAdded;
  public event Action<LeveledListEntryViewModel>? EntryRemoved;

  public string EditorId
  {
    get => _editorId;
    set => SetEditorIdInternal(value);
  }

  public ReadOnlyObservableCollection<LeveledListEntryViewModel> Entries { get; }

  public bool HasEntries => _entries.Count > 0;

  public int EntryCount => _entries.Count;

  public string FormIdDisplay => FormKey.HasValue ? $"0x{FormKey.Value.ID:X8}" : "Pending";

  public string Header => $"{EditorId} — FormID {FormIdDisplay}";

  public ReactiveCommand<Unit, Unit> RemoveSelfCommand { get; }

  public IReadOnlyList<LeveledListEntryViewModel> GetEntries() => _entries.ToList();

  public LeveledListEntryViewModel AddArmorEntry(ArmorRecordViewModel armor, short level = 1, short count = 1)
  {
    var entry = LeveledListEntryViewModel.ForArmor(armor, RemoveEntry, level, count);
    _entries.Add(entry);
    EntryAdded?.Invoke(entry);
    return entry;
  }

  public LeveledListEntryViewModel AddListEntry(LeveledListReference reference, short level = 1, short count = 1)
  {
    var entry = LeveledListEntryViewModel.ForList(reference, RemoveEntry, level, count);
    _entries.Add(entry);
    EntryAdded?.Invoke(entry);
    return entry;
  }

  public bool ContainsEntry(string dedupKey) =>
    _entries.Any(e => string.Equals(e.DedupKey, dedupKey, StringComparison.OrdinalIgnoreCase));

  private void RemoveEntry(LeveledListEntryViewModel entry)
  {
    if (_entries.Remove(entry))
    {
      EntryRemoved?.Invoke(entry);
    }
  }

  private void EntriesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
  {
    this.RaisePropertyChanged(nameof(HasEntries));
    this.RaisePropertyChanged(nameof(EntryCount));
    RefreshConflicts();
  }

  private void RefreshConflicts()
  {
    foreach (var entry in _entries)
    {
      entry.IsConflicting = false;
    }

    if (!UseAll)
    {
      return;
    }

    var owners = new Dictionary<BipedObjectFlag, LeveledListEntryViewModel>();
    foreach (var entry in _entries)
    {
      var mask = entry.SlotMask;
      if (mask == 0)
      {
        continue;
      }

      foreach (var flag in BipedFlags)
      {
        if (!mask.HasFlag(flag))
        {
          continue;
        }

        if (owners.TryGetValue(flag, out var owner))
        {
          owner.IsConflicting = true;
          entry.IsConflicting = true;
        }
        else
        {
          owners[flag] = entry;
        }
      }
    }
  }

  private void SetEditorIdInternal(string? value)
  {
    var sanitized = InputPatterns.Identifier.Sanitize(value);
    if (string.IsNullOrEmpty(sanitized))
    {
      sanitized = string.IsNullOrEmpty(_editorId) ? "LeveledList" : _editorId;
    }

    if (string.Equals(sanitized, _editorId, StringComparison.Ordinal))
    {
      return;
    }

    this.RaiseAndSetIfChanged(ref _editorId, sanitized, nameof(EditorId));
    this.RaisePropertyChanged(nameof(Header));
  }
}
