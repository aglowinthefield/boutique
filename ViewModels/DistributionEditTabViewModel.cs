using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Win32;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

public record PreviewLineHighlightRequest(int LineNumber);

public class PreviewLine
{
  public int LineNumber { get; init; }
  public string Content { get; init; } = string.Empty;
}

public partial class DistributionEditTabViewModel : ReactiveObject, IDisposable
{
  private readonly ArmorPreviewService                                 _armorPreviewService;
  private readonly GameDataCacheService                                _cache;
  private readonly IObservable<bool>                                   _canPaste;
  private readonly IObservable<bool>                                   _canSave;
  private readonly CompositeDisposable                                 _disposables               = new();
  private readonly Dictionary<DistributionEntryViewModel, CompositeDisposable> _entrySubscriptions = new();
  private readonly DistributionFilePathService                                  _filePathService;
  private readonly DistributionFileEditorService                                _fileWriterService;
  private readonly GuiSettingsService                                           _guiSettings;

  private readonly IObservable<bool>                                            _hasEntries;
  private readonly DistributionEntryHydrationService                            _hydrationService;
  private readonly ILogger                                                      _logger;
  private readonly MutagenService                                               _mutagenService;
  private readonly IObservable<bool>                                            _notLoading;
  private readonly SettingsViewModel                                            _settings;

  [ReactiveCollection] private ObservableCollection<DistributionFileSelectionItem> _availableDistributionFiles = [];

  [ReactiveCollection] private ObservableCollection<IOutfitGetter> _availableOutfits = [];

  [Reactive] private string _classSearchText = string.Empty;

  [Reactive] private bool _conflictsResolvedByFilename;

  [Reactive] private string _conflictSummary = string.Empty;

  [Reactive] private bool _hasOverlaps;

  [Reactive] private bool _overlapsResolvedByFilename;

  [Reactive] private string _overlapSummary = string.Empty;

  [Reactive] private CopiedNpcFilter? _copiedFilter;

  private ObservableCollection<DistributionEntryViewModel> _distributionEntries = [];

  [Reactive] private string _distributionFileContent = string.Empty;

  [Reactive] private string _distributionFilePath = string.Empty;

  private DistributionFileType _distributionFormat = DistributionFileType.SkyPatcher;

  /// <summary>
  ///   Organized dropdown items with headers and files for tree-ish display.
  /// </summary>
  [Reactive] private IReadOnlyList<GroupedDropdownItem> _dropdownItems = [];

  [Reactive] private string _factionSearchText = string.Empty;

  /// <summary>
  ///   True if any distribution entry has chance-based distribution enabled.
  ///   When true, SkyPatcher format is not available (it doesn't support chance).
  /// </summary>
  [Reactive] private bool _hasChanceBasedEntries;

  [Reactive] private bool _hasConflicts;

  /// <summary>
  ///   True if any distribution entry is an exclusive-group distribution.
  ///   When true, SkyPatcher format is not available (it doesn't support SPID ExclusiveGroup syntax).
  /// </summary>
  [Reactive] private bool _hasExclusiveGroupDistributions;

  [Reactive] private bool _hasIntraFileConflicts;

  /// <summary>
  ///   True if any distribution entry is a keyword distribution.
  ///   When true, SkyPatcher format is not available (it doesn't support keyword distributions).
  /// </summary>
  [Reactive] private bool _hasKeywordDistributions;

  [Reactive] private PreviewLineHighlightRequest? _highlightRequest;

  [Reactive] private string _intraFileConflictSummary = string.Empty;

  [Reactive] private bool _hasIntraFileOverlaps;

  [Reactive] private string _intraFileOverlapSummary = string.Empty;

  private bool _isBulkLoading;

  [Reactive] private bool _isCreatingNewFile;

  private bool _isInitialized;

  [Reactive] private bool _isLoading;

  private string? _justSavedFilePath;

  [Reactive] private string _keywordSearchText = string.Empty;

  private DistributionEntryViewModel? _lastChangedEntry;

  [Reactive] private string _locationSearchText = string.Empty;

  [Reactive] private string _npcSearchText = string.Empty;

  [Reactive] private string _outfitFilterSearchText = string.Empty;

  private bool _outfitsLoaded;

  [Reactive] private IReadOnlyList<DistributionParseError> _parseErrors = [];

  [Reactive] private IReadOnlyList<PreviewLine> _previewLines = [];

  [Reactive] private string _raceSearchText = string.Empty;

  [Reactive] private string _statusMessage = string.Empty;

  [Reactive] private string _suggestedFileName = string.Empty;

  public DistributionEditTabViewModel(
    DistributionFileEditorService fileWriterService,
    ArmorPreviewService armorPreviewService,
    MutagenService mutagenService,
    GameDataCacheService cache,
    SettingsViewModel settings,
    GuiSettingsService guiSettings,
    DistributionEntryHydrationService hydrationService,
    DistributionFilePathService filePathService,
    ILogger logger)
  {
    _fileWriterService   = fileWriterService;
    _armorPreviewService = armorPreviewService;
    _mutagenService      = mutagenService;
    _cache               = cache;
    _settings            = settings;
    _guiSettings         = guiSettings;
    _hydrationService    = hydrationService;
    _filePathService     = filePathService;
    _logger              = logger.ForContext<DistributionEditTabViewModel>();

    _mutagenService.PluginsChanged += OnPluginsChanged;
    _cache.CacheLoaded             += OnCacheLoaded;

    SetupFilterPipelines();
    SetupIntraFileConflictDetection();

    if (_cache.IsLoaded)
    {
      InitializeFromCache();
    }

    _distributionEntries.CollectionChanged += OnDistributionEntriesChanged;

    _hasEntries = this.WhenAnyValue(vm => vm.DistributionEntriesCount, count => count > 0);

    _notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);

    _canSave = this.WhenAnyValue(
      vm => vm.DistributionFilePath,
      vm => vm.IsCreatingNewFile,
      vm => vm.NewFileName,
      (path, isNew, newName) =>
        !string.IsNullOrWhiteSpace(path) || (isNew && !string.IsNullOrWhiteSpace(newName)));

    _canPaste = this.WhenAnyValue(
      vm => vm.HasCopiedFilter,
      vm => vm.SelectedEntry,
      (hasCopied, entry) => hasCopied && entry != null);

    this.WhenAnyValue(vm => vm.CopiedFilter)
        .Subscribe(_ => this.RaisePropertyChanged(nameof(HasCopiedFilter)));

    this.WhenAnyValue(vm => vm.DistributionFormat)
        .Skip(1)
        .Subscribe(_ =>
        {
          UpdateDistributionFilePathForFormat();
          UpdateFileContent();
        });

    this.WhenAnyValue(vm => vm.IsCreatingNewFile)
        .Where(isNew => isNew)
        .Subscribe(_ => DistributionFormat = DistributionFileType.SkyPatcher);

    this.WhenAnyValue(vm => vm.DistributionFilePath)
        .Subscribe(_ => this.RaisePropertyChanged(nameof(ActualFileName)));

    this.WhenAnyValue(vm => vm.DistributionFileContent)
        .Subscribe(_ => UpdatePreviewLines());

    this.WhenAnyValue(vm => vm.SelectedEntry)
        .Where(entry => entry != null && !_isBulkLoading)
        .Throttle(TimeSpan.FromMilliseconds(50))
        .ObserveOn(RxApp.MainThreadScheduler)
        .Subscribe(entry => RaiseHighlightForEntry(entry!));

    this.WhenAnyValue(vm => vm.SelectedEntry)
        .Throttle(TimeSpan.FromMilliseconds(100))
        .ObserveOn(RxApp.TaskpoolScheduler)
        .Subscribe(entry => UpdateMatchingNpcsForEntry(entry));
  }

  public ReadOnlyObservableCollection<ClassRecordViewModel> FilteredClasses { get; private set; } = null!;

  public ReadOnlyObservableCollection<FactionRecordViewModel> FilteredFactions { get; private set; } = null!;

  public ReadOnlyObservableCollection<KeywordRecordViewModel> FilteredKeywords { get; private set; } = null!;

  public ReadOnlyObservableCollection<LocationRecordViewModel> FilteredLocations { get; private set; } = null!;

  public ReadOnlyObservableCollection<NpcRecordViewModel> FilteredNpcs { get; private set; } = null!;

  public ReadOnlyObservableCollection<RaceRecordViewModel> FilteredRaces { get; private set; } = null!;

  public ReadOnlyObservableCollection<OutfitRecordViewModel> FilteredOutfitFilters { get; private set; } = null!;

  /// <summary>
  ///   Actual parse errors (excludes preserved lines like keyword distributions).
  /// </summary>
  public IReadOnlyList<DistributionParseError> ActualParseErrors =>
    ParseErrors.Where(e => !e.Reason.EndsWith("(preserved)", StringComparison.Ordinal)).ToList();

  public bool HasParseErrors => ActualParseErrors.Count > 0;

  public ObservableCollection<DistributionEntryViewModel> DistributionEntries
  {
    get => _distributionEntries;
    private set
    {
      var oldCollection = _distributionEntries;
      if (oldCollection != null)
      {
        oldCollection.CollectionChanged -= OnDistributionEntriesChanged;
      }

      this.RaiseAndSetIfChanged(ref _distributionEntries, value);
      if (value != null)
      {
        value.CollectionChanged += OnDistributionEntriesChanged;
        this.RaisePropertyChanged(nameof(DistributionEntriesCount));
      }
    }
  }

  private int DistributionEntriesCount => _distributionEntries.Count;

  public DistributionEntryViewModel? SelectedEntry
  {
    get => field;
    set
    {
      field?.IsSelected = false;
      this.RaiseAndSetIfChanged(ref field, value);
      value?.IsSelected = true;
    }
  }

  private IReadOnlyList<NpcMatchResult> _matchingNpcsForSelectedEntry = [];

  public int MatchingNpcsCount => _matchingNpcsForSelectedEntry.Count;

  public IReadOnlyList<NpcMatchResult> MatchingNpcsForSelectedEntry => _matchingNpcsForSelectedEntry;

  /// <summary>Available NPCs for distribution entry selection (from cache).</summary>
  public ReadOnlyObservableCollection<NpcRecordViewModel> AvailableNpcs => _cache.AllNpcRecords;

  /// <summary>Available factions for distribution entry selection (from cache).</summary>
  public ReadOnlyObservableCollection<FactionRecordViewModel> AvailableFactions => _cache.AllFactions;

  /// <summary>Available keywords for distribution entry selection (from cache).</summary>
  public ReadOnlyObservableCollection<KeywordRecordViewModel> AvailableKeywords => _cache.AllKeywords;

  /// <summary>Available races for distribution entry selection (from cache).</summary>
  public ReadOnlyObservableCollection<RaceRecordViewModel> AvailableRaces => _cache.AllRaces;

  /// <summary>Available classes for distribution entry selection (from cache).</summary>
  public ReadOnlyObservableCollection<ClassRecordViewModel> AvailableClasses => _cache.AllClasses;

  /// <summary>
  ///   The currently selected dropdown item. Headers are not selectable.
  /// </summary>
  public GroupedDropdownItem? SelectedDropdownItem
  {
    get
    {
      if (SelectedDistributionFile == null)
      {
        return null;
      }

      if (SelectedDistributionFile.IsNewFile)
      {
        return DropdownItems.OfType<GroupedDropdownAction>()
                            .FirstOrDefault(a => a.ActionId == DistributionDropdownOrganizer.NewFileActionId);
      }

      return DropdownItems.OfType<GroupedDropdownItem<DistributionFileInfo>>()
                          .FirstOrDefault(f => f.Value.FullPath == SelectedDistributionFile.File?.FullPath);
    }
    set
    {
      if (value is GroupedDropdownHeader)
      {
        return;
      }

      if (value is GroupedDropdownAction action && action.ActionId == DistributionDropdownOrganizer.NewFileActionId)
      {
        SelectedDistributionFile = AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
      }
      else if (value is GroupedDropdownItem<DistributionFileInfo> fileItem)
      {
        var match = AvailableDistributionFiles.FirstOrDefault(f =>
                                                                !f.IsNewFile && f.File?.FullPath ==
                                                                fileItem.Value.FullPath);
        if (match != null)
        {
          SelectedDistributionFile = match;
        }
      }

      this.RaisePropertyChanged();
    }
  }

  public DistributionFileSelectionItem? SelectedDistributionFile
  {
    get => field;
    set
    {
      var previous = field;
      this.RaiseAndSetIfChanged(ref field, value);

      if (value != null)
      {
        IsCreatingNewFile = value.IsNewFile;
        if (!value.IsNewFile && value.File != null)
        {
          DistributionFilePath = value.File.FullPath;
          NewFileName          = string.Empty;
          if (File.Exists(DistributionFilePath) && !IsLoading)
          {
            _logger.Debug("Auto-loading file: {Path}", DistributionFilePath);
            _ = LoadDistributionFileAsync();
          }
        }
        else if (value.IsNewFile)
        {
          if (previous != null && !previous.IsNewFile)
          {
            _isBulkLoading = true;
            try
            {
              DistributionEntries.Clear();
              ParseErrors = [];
              this.RaisePropertyChanged(nameof(ActualParseErrors));
              this.RaisePropertyChanged(nameof(HasParseErrors));
              ResetConflictState();
            }
            finally
            {
              _isBulkLoading = false;
            }

            this.RaisePropertyChanged(nameof(DistributionEntriesCount));
            UpdateFileContent();
            UpdateHasChanceBasedEntries();
            UpdateHasKeywordDistributions();
            UpdateHasExclusiveGroupDistributions();
          }

          if (string.IsNullOrWhiteSpace(NewFileName))
          {
            NewFileName = GenerateUniqueNewFileName();
          }

          var newPath = _filePathService.BuildPathFromNewFileName(NewFileName, DistributionFormat);
          if (newPath != null)
          {
            DistributionFilePath = newPath;
          }
        }
      }

      if (!Equals(previous, value))
      {
        this.RaisePropertyChanged(nameof(ShowNewFileNameInput));
        this.RaisePropertyChanged(nameof(SaveDistributionFileCommand));
      }
    }
  }

  public bool ShowNewFileNameInput => IsCreatingNewFile;

  public string NewFileName
  {
    get => field ?? string.Empty;
    set
    {
      this.RaiseAndSetIfChanged(ref field, value);
      if (IsCreatingNewFile)
      {
        var newPath = _filePathService.BuildPathFromNewFileName(value, DistributionFormat);
        if (newPath != null)
        {
          DistributionFilePath = newPath;
        }

        DetectConflicts();
      }
    }
  }

  /// <summary>
  ///   The actual filename that will be saved (derived from DistributionFilePath).
  ///   For SPID format, this includes the _DISTR suffix.
  /// </summary>
  public string ActualFileName => !string.IsNullOrEmpty(DistributionFilePath)
                                    ? Path.GetFileName(DistributionFilePath)
                                    : string.Empty;

  /// <summary>
  ///   The distribution file format (SPID or SkyPatcher).
  ///   Defaults to SkyPatcher for new files, or detected from existing files.
  /// </summary>
  public DistributionFileType DistributionFormat
  {
    get => _distributionFormat;
    set
    {
      if (_distributionFormat == value)
      {
        return;
      }

      _logger.Debug(
        "DistributionFormat changing from {OldFormat} to {NewFormat}, EntryCount={Count}",
        _distributionFormat,
        value,
        DistributionEntries.Count);

      this.RaiseAndSetIfChanged(ref _distributionFormat, value);
      UpdateFileContent();
    }
  }

  public IReadOnlyList<DistributionFileType> AvailableFormats { get; } =
    [DistributionFileType.SkyPatcher, DistributionFileType.Spid];

  public bool HasCopiedFilter => CopiedFilter != null;

  public Interaction<ArmorPreviewSceneCollection, Unit> ShowPreview { get; } = new();

  public bool IsInitialized => _mutagenService.IsInitialized;
  private ReadOnlyObservableCollection<DistributionFileViewModel> DistributionFiles => _cache.AllDistributionFiles;

  public void Dispose()
  {
    _disposables.Dispose();
    _mutagenService.PluginsChanged -= OnPluginsChanged;
    _cache.CacheLoaded             -= OnCacheLoaded;

    foreach (var sub in _entrySubscriptions.Values)
    {
      sub.Dispose();
    }

    _entrySubscriptions.Clear();
    GC.SuppressFinalize(this);
  }

  private void UpdatePreviewLines()
  {
    var lines = string.IsNullOrEmpty(DistributionFileContent)
                  ? []
                  : DistributionFileContent.Split('\n')
                                           .Select((line, index) =>
                                                     new PreviewLine
                                                     {
                                                       LineNumber = index + 1, Content = line.TrimEnd('\r')
                                                     })
                                           .ToList();
    PreviewLines = lines;
  }

  private void SetupIntraFileConflictDetection()
  {
    var entriesChanged = _distributionEntries
                         .ToObservableChangeSet()
                         .Publish();

    var npcsInEntriesChanged = entriesChanged
      .MergeMany(entry => entry.SelectedNpcs.ToObservableChangeSet());

    _disposables.Add(
      entriesChanged.Select(_ => Unit.Default).Merge(npcsInEntriesChanged.Select(_ => Unit.Default))
                    .Throttle(TimeSpan.FromMilliseconds(100))
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(_ => UpdateIntraFileConflicts()));

    _disposables.Add(entriesChanged.Connect());
  }

  private void UpdateIntraFileConflicts()
  {
    var outfitEntries = DistributionEntries
                        .Where(e => e.Type == DistributionType.Outfit && e.SelectedOutfit != null)
                        .ToList();

    if (_isBulkLoading || outfitEntries.Count < 2)
    {
      HasIntraFileConflicts    = false;
      IntraFileConflictSummary = string.Empty;
      HasIntraFileOverlaps     = false;
      IntraFileOverlapSummary  = string.Empty;
      return;
    }

    var result = IntraFileConflictDetectionService.Detect(
                   outfitEntries,
                   _cache.AllNpcs.ToList(),
                   _cache.SimulatedKeywordsByNpc);
    HasIntraFileConflicts    = result.HasConflicts;
    IntraFileConflictSummary = result.ConflictSummary;
    HasIntraFileOverlaps     = result.HasOverlaps;
    IntraFileOverlapSummary  = result.OverlapSummary;
  }

  private void SetupFilterPipelines()
  {
    _disposables.Add(
      FilterPipelineFactory.CreateSearchFilter(
        this.WhenAnyValue(vm => vm.NpcSearchText),
        _cache.AllNpcRecords,
        out var filteredNpcs));
    FilteredNpcs = filteredNpcs;

    _disposables.Add(
      FilterPipelineFactory.CreateSearchFilter(
        this.WhenAnyValue(vm => vm.FactionSearchText),
        _cache.AllFactions,
        out var filteredFactions));
    FilteredFactions = filteredFactions;

    _disposables.Add(
      FilterPipelineFactory.CreateSearchFilter(
        this.WhenAnyValue(vm => vm.KeywordSearchText),
        _cache.AllKeywords,
        out var filteredKeywords));
    FilteredKeywords = filteredKeywords;

    _disposables.Add(
      FilterPipelineFactory.CreateSearchFilter(
        this.WhenAnyValue(vm => vm.RaceSearchText),
        _cache.AllRaces,
        out var filteredRaces));
    FilteredRaces = filteredRaces;

    _disposables.Add(
      FilterPipelineFactory.CreateSearchFilter(
        this.WhenAnyValue(vm => vm.ClassSearchText),
        _cache.AllClasses,
        out var filteredClasses));
    FilteredClasses = filteredClasses;

    _disposables.Add(
      FilterPipelineFactory.CreateSearchFilter(
        this.WhenAnyValue(vm => vm.LocationSearchText),
        _cache.AllLocations,
        out var filteredLocations));
    FilteredLocations = filteredLocations;

    _disposables.Add(
      FilterPipelineFactory.CreateSearchFilter(
        this.WhenAnyValue(vm => vm.OutfitFilterSearchText),
        _cache.AllOutfitRecords,
        out var filteredOutfitFilters));
    FilteredOutfitFilters = filteredOutfitFilters;
  }

  private void OnDistributionEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
  {
    if (_isBulkLoading)
    {
      return;
    }

    _logger.Debug(
      "OnDistributionEntriesChanged: Action={Action}, NewItems={NewCount}, OldItems={OldCount}",
      e.Action,
      e.NewItems?.Count ?? 0,
      e.OldItems?.Count ?? 0);

    this.RaisePropertyChanged(nameof(DistributionEntriesCount));

    if (e.OldItems != null)
    {
      foreach (DistributionEntryViewModel entry in e.OldItems)
      {
        UnsubscribeFromEntryChanges(entry);
      }
    }

    if (e.NewItems != null)
    {
      foreach (DistributionEntryViewModel entry in e.NewItems)
      {
        SubscribeToEntryChanges(entry);
      }
    }

    UpdateFileContent();
    UpdateHasChanceBasedEntries();
    UpdateHasKeywordDistributions();
    UpdateHasExclusiveGroupDistributions();

    _logger.Debug("OnDistributionEntriesChanged completed");
  }

  private void SubscribeToEntryChanges(DistributionEntryViewModel entry)
  {
    var composite = new CompositeDisposable();

    composite.Add(
      Observable.FromEventPattern(entry, nameof(entry.EntryChanged))
                .Do(_ => _lastChangedEntry = entry)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => UpdateFileContent()));

    composite.Add(
      Observable.FromEventPattern(entry, nameof(entry.EntryChanged))
                .Throttle(TimeSpan.FromMilliseconds(300))
                .ObserveOn(RxApp.TaskpoolScheduler)
                .Subscribe(_ =>
                {
                  if (SelectedEntry == entry)
                  {
                    UpdateMatchingNpcsForEntry(entry);
                  }
                }));

    composite.Add(
      entry.WhenAnyValue(e => e.UseChance)
           .Subscribe(_ => UpdateHasChanceBasedEntries()));

    composite.Add(
      entry.WhenAnyValue(e => e.Type)
           .Subscribe(_ =>
           {
             UpdateHasKeywordDistributions();
             UpdateHasExclusiveGroupDistributions();
           }));

    _entrySubscriptions[entry] = composite;
  }

  private void UnsubscribeFromEntryChanges(DistributionEntryViewModel entry)
  {
    if (_entrySubscriptions.Remove(entry, out var composite))
    {
      composite.Dispose();
    }
  }

  private void UpdateHasChanceBasedEntries() =>
    HasChanceBasedEntries = DistributionEntries.Any(e => e.UseChance);

  private void UpdateHasKeywordDistributions()
  {
    HasKeywordDistributions = DistributionEntries.Any(e => e.Type == DistributionType.Keyword);
    EnsureSpidFormatForSpidOnlyEntries();
  }

  private void UpdateHasExclusiveGroupDistributions()
  {
    HasExclusiveGroupDistributions = DistributionEntries.Any(e => e.Type == DistributionType.ExclusiveGroup);
    EnsureSpidFormatForSpidOnlyEntries();
  }

  private void EnsureSpidFormatForSpidOnlyEntries()
  {
    if ((HasKeywordDistributions || HasExclusiveGroupDistributions) &&
        DistributionFormat == DistributionFileType.SkyPatcher)
    {
      DistributionFormat = DistributionFileType.Spid;
    }
  }

  [ReactiveCommand]
  private void AddDistributionEntry()
  {
    _logger.Debug("AddDistributionEntry called");
    try
    {
      _logger.Debug("Creating DistributionEntry");
      var entry = new DistributionEntry();

      _logger.Debug("Creating DistributionEntryViewModel");
      var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry, IsFormatChangingToSpid);

      _logger.Debug("Adding to DistributionEntries collection");
      DistributionEntries.Add(entryVm);

      _logger.Debug("Deferring SelectedEntry assignment");
      Application.Current?.Dispatcher.BeginInvoke(
        new Action(() =>
        {
          SelectedEntry = entryVm;
          _logger.Debug("SelectedEntry set");
        }),
        DispatcherPriority.Background);

      _logger.Debug("AddDistributionEntry completed successfully");
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to add distribution entry.");
      StatusMessage = $"Error adding entry: {ex.Message}";
    }
  }

  /// <typeparam name="T">The type of record view model.</typeparam>
  /// <param name="filteredItems">The filtered collection to get selected items from.</param>
  /// <param name="addToEntry">Function to add an item to the entry, returns true if added.</param>
  /// <param name="itemTypeName">The display name for the item type (e.g., "NPC", "faction").</param>
  private void AddSelectedCriteriaToEntry<T>(
    IEnumerable<T> filteredItems,
    Func<DistributionEntryViewModel, T, bool> addToEntry,
    string itemTypeName)
    where T : ISelectableRecordViewModel
  {
    if (SelectedEntry == null)
    {
      SelectedEntry = DistributionEntries.FirstOrDefault();
      if (SelectedEntry == null)
      {
        AddDistributionEntry();
        return;
      }
    }

    var selectedItems = filteredItems
                        .Where(item => item.IsSelected)
                        .ToList();

    if (selectedItems.Count == 0)
    {
      StatusMessage = $"No {itemTypeName}s selected. Check the boxes next to {itemTypeName}s you want to add.";
      return;
    }

    var addedCount = 0;
    foreach (var item in selectedItems)
    {
      if (addToEntry(SelectedEntry, item))
      {
        addedCount++;
      }
    }

    foreach (var item in selectedItems)
    {
      item.IsSelected = false;
    }

    if (addedCount > 0)
    {
      StatusMessage =
        $"Added {addedCount} {itemTypeName}(s) to entry: {SelectedEntry.SelectedOutfit?.EditorID ?? "(No outfit)"}";
      _logger.Debug("Added {Count} {ItemType}s to entry", addedCount, itemTypeName);
    }
    else
    {
      StatusMessage = $"All selected {itemTypeName}s are already in this entry.";
    }
  }

  [ReactiveCommand(CanExecute = nameof(_hasEntries))]
  private void AddSelectedNpcsToEntry() =>
    AddSelectedCriteriaToEntry(
      FilteredNpcs,
      (entry, npc) => entry.AddNpc(npc),
      "NPC");

  [ReactiveCommand(CanExecute = nameof(_hasEntries))]
  private void AddSelectedFactionsToEntry() =>
    AddSelectedCriteriaToEntry(
      FilteredFactions,
      (entry, faction) => entry.AddFaction(faction),
      "faction");

  [ReactiveCommand(CanExecute = nameof(_hasEntries))]
  private void AddSelectedKeywordsToEntry() =>
    AddSelectedCriteriaToEntry(
      FilteredKeywords,
      (entry, keyword) => entry.AddKeyword(keyword),
      "keyword");

  [ReactiveCommand(CanExecute = nameof(_hasEntries))]
  private void AddSelectedRacesToEntry() =>
    AddSelectedCriteriaToEntry(
      FilteredRaces,
      (entry, race) => entry.AddRace(race),
      "race");

  [ReactiveCommand(CanExecute = nameof(_hasEntries))]
  private void AddSelectedClassesToEntry() =>
    AddSelectedCriteriaToEntry(
      FilteredClasses,
      (entry, classVm) => entry.AddClass(classVm),
      "class");

  [ReactiveCommand(CanExecute = nameof(_hasEntries))]
  private void AddSelectedLocationsToEntry() =>
    AddSelectedCriteriaToEntry(
      FilteredLocations,
      (entry, location) => entry.AddLocation(location),
      "location");

  [ReactiveCommand(CanExecute = nameof(_hasEntries))]
  private void AddSelectedOutfitFiltersToEntry() =>
    AddSelectedCriteriaToEntry(
      FilteredOutfitFilters,
      (entry, outfit) => entry.AddOutfitFilter(outfit),
      "outfit");

  [ReactiveCommand(CanExecute = nameof(_canPaste))]
  private void PasteFilterToEntry()
  {
    if (CopiedFilter == null)
    {
      StatusMessage = "No filter to paste. Copy a filter from the NPCs tab first.";
      return;
    }

    if (SelectedEntry == null)
    {
      SelectedEntry = DistributionEntries.FirstOrDefault();
      if (SelectedEntry == null)
      {
        AddDistributionEntry();
        Application.Current?.Dispatcher.BeginInvoke(
          new Action(() =>
          {
            if (SelectedEntry != null)
            {
              ApplyFilterToEntry(SelectedEntry, CopiedFilter);
            }
          }),
          DispatcherPriority.Background);
        return;
      }
    }

    ApplyFilterToEntry(SelectedEntry, CopiedFilter);
  }

  private void ApplyFilterToEntry(DistributionEntryViewModel entry, CopiedNpcFilter filter)
  {
    var addedItems = _hydrationService.ApplyFilter(entry, filter);

    if (addedItems.Count > 0)
    {
      StatusMessage = $"Pasted filter to entry: added {addedItems.Count} filter(s)";
      _logger.Debug("Pasted filter to entry: {Items}", string.Join(", ", addedItems));
      UpdateFileContent();
    }
    else
    {
      StatusMessage = "Filter already applied or no applicable filters to paste.";
    }
  }

  [ReactiveCommand]
  private void RemoveDistributionEntry(DistributionEntryViewModel entryVm)
  {
    if (DistributionEntries.Remove(entryVm))
    {
      if (SelectedEntry == entryVm)
      {
        SelectedEntry = DistributionEntries.FirstOrDefault();
      }

      _logger.Debug("Removed distribution entry.");
    }
  }

  [ReactiveCommand]
  private void SelectEntry(DistributionEntryViewModel entryVm)
  {
    SelectedEntry = entryVm; // Property setter handles IsSelected updates
    _logger.Debug("Selected distribution entry: {Outfit}", entryVm?.SelectedOutfit?.EditorID ?? "(No outfit)");
  }

  [ReactiveCommand(CanExecute = nameof(_canSave))]
  private async Task SaveDistributionFileAsync()
  {
    if (string.IsNullOrWhiteSpace(DistributionFilePath))
    {
      StatusMessage = "Please select a file path.";
      return;
    }

    DetectConflicts();

    var finalFilePath = DistributionFilePath;
    var finalFileName = Path.GetFileName(DistributionFilePath);

    if (IsCreatingNewFile && HasConflicts && !string.IsNullOrEmpty(SuggestedFileName))
    {
      var sb = new StringBuilder();
      sb.AppendLine("⚠ Distribution Conflicts Detected");
      sb.AppendLine();
      sb.AppendLine(ConflictSummary);
      sb.AppendLine();
      sb.AppendLine("To ensure your new distributions take priority (load last), the filename will be changed to:");
      sb.AppendLine();
      sb.Append(CultureInfo.InvariantCulture, $"    {SuggestedFileName}").AppendLine();
      sb.AppendLine();
      sb.AppendLine("This 'Z' prefix ensures alphabetical sorting places your file after the conflicting files.");
      sb.AppendLine();
      sb.AppendLine("Do you want to continue with this filename?");

      var result = MessageBox.Show(
        sb.ToString(),
        "Conflicts Detected - Filename Change Required",
        MessageBoxButton.YesNoCancel,
        MessageBoxImage.Warning);

      if (result == MessageBoxResult.Cancel)
      {
        StatusMessage = "Save cancelled.";
        return;
      }

      if (result == MessageBoxResult.Yes)
      {
        var directory = Path.GetDirectoryName(DistributionFilePath);
        finalFileName = SuggestedFileName;
        if (!finalFileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
          finalFileName += ".ini";
        }

        finalFilePath = !string.IsNullOrEmpty(directory)
                          ? Path.Combine(directory, finalFileName)
                          : finalFileName;

        NewFileName = finalFileName;
      }
    }

    if (File.Exists(finalFilePath))
    {
      var result = MessageBox.Show(
        $"The file '{Path.GetFileName(finalFilePath)}' already exists.\n\nDo you want to overwrite it?",
        "Confirm Overwrite",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);

      if (result != MessageBoxResult.Yes)
      {
        StatusMessage = "Save cancelled.";
        return;
      }
    }

    try
    {
      IsLoading     = true;
      StatusMessage = "Saving distribution file...";

      // Write the preview text directly - it's already correctly formatted
      // This ensures the saved file matches exactly what the user sees in the preview
      var directory = Path.GetDirectoryName(finalFilePath);
      if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }

      await File.WriteAllTextAsync(finalFilePath, DistributionFileContent, Encoding.UTF8);

      StatusMessage = $"Successfully saved distribution file: {Path.GetFileName(finalFilePath)}";
      _logger.Information(
        "Saved distribution file: {FilePath} ({LineCount} lines)",
        finalFilePath,
        DistributionFileContent.Split('\n').Length);

      // Track the saved file path so RefreshAvailableDistributionFiles can select it
      _justSavedFilePath                    = finalFilePath;
      _guiSettings.LastDistributionFilePath = finalFilePath;
      DistributionFilePath                  = finalFilePath;

      FileSaved?.Invoke(finalFilePath);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to save distribution file.");
      StatusMessage = $"Error saving file: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  public event Action<string>? FileSaved;

  private async Task LoadDistributionFileAsync()
  {
    _logger.Debug(
      "LoadDistributionFileAsync called. DistributionFilePath: {Path}, Exists: {Exists}",
      DistributionFilePath,
      File.Exists(DistributionFilePath));

    if (string.IsNullOrWhiteSpace(DistributionFilePath) || !File.Exists(DistributionFilePath))
    {
      StatusMessage = "File does not exist. Please select a valid file.";
      _logger.Warning("Cannot load file - path is empty or file does not exist: {Path}", DistributionFilePath);
      return;
    }

    try
    {
      IsLoading     = true;
      StatusMessage = "Loading distribution file...";
      _logger.Information("Loading distribution file: {FilePath}", DistributionFilePath);

      var (entries, detectedFormat, parseErrors) =
        await _fileWriterService.LoadDistributionFileWithErrorsAsync(DistributionFilePath);
      DistributionFormat = detectedFormat;
      ParseErrors        = parseErrors;
      this.RaisePropertyChanged(nameof(ActualParseErrors));
      this.RaisePropertyChanged(nameof(HasParseErrors));

      await LoadAvailableOutfitsAsync();
      _isBulkLoading = true;
      try
      {
        DistributionEntries.Clear();
        ResetConflictState();
        var entryVms = await Task.Run(() =>
                                        entries.Select(entry => CreateEntryViewModel(entry)).ToList());
        foreach (var entryVm in entryVms)
        {
          DistributionEntries.Add(entryVm);
        }

        foreach (var entryVm in entryVms)
        {
          SubscribeToEntryChanges(entryVm);
        }
      }
      finally
      {
        _isBulkLoading = false;
      }

      this.RaisePropertyChanged(nameof(DistributionEntriesCount));
      UpdateFileContent();
      UpdateHasChanceBasedEntries();
      UpdateHasKeywordDistributions();
      UpdateHasExclusiveGroupDistributions();

      var statusMsg =
        $"Loaded {entries.Count} distribution entries from {Path.GetFileName(DistributionFilePath)}";
      if (parseErrors.Count > 0)
      {
        statusMsg += $" ({parseErrors.Count} line(s) could not be parsed)";
      }

      StatusMessage = statusMsg;
      _logger.Information(
        "Loaded distribution file: {FilePath} with {Count} entries, {ErrorCount} parse errors",
        DistributionFilePath,
        entries.Count,
        parseErrors.Count);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to load distribution file.");
      StatusMessage = $"Error loading file: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [ReactiveCommand(CanExecute = nameof(_notLoading))]
  private async Task ScanNpcsAsync()
  {
    try
    {
      IsLoading = true;
      if (!_mutagenService.IsInitialized)
      {
        var dataPath = _settings.SkyrimDataPath;
        if (string.IsNullOrWhiteSpace(dataPath))
        {
          StatusMessage = "Please set the Skyrim data path in Settings before scanning NPCs.";
          return;
        }

        if (!Directory.Exists(dataPath))
        {
          StatusMessage = $"Skyrim data path does not exist: {dataPath}";
          return;
        }

        StatusMessage = "Initializing Skyrim environment...";
        await _mutagenService.InitializeAsync(dataPath);
        this.RaisePropertyChanged(nameof(IsInitialized));
      }

      if (_cache is { IsLoaded: false, IsLoading: false })
      {
        StatusMessage = "Loading game data (NPCs, factions, keywords, races, classes)...";
        await _cache.LoadAsync();
      }
      else if (_cache.IsLoading)
      {
        StatusMessage = "Loading game data from plugins...";
        while (_cache.IsLoading)
        {
          await Task.Delay(100);
        }
      }

      StatusMessage =
        $"Loaded: {AvailableNpcs.Count:N0} NPCs, {AvailableFactions.Count:N0} factions, {AvailableRaces.Count:N0} races, {AvailableClasses.Count:N0} classes, {AvailableKeywords.Count:N0} keywords.";
      _logger.Information(
        "Game data loaded: {NpcCount} NPCs, {FactionCount} factions.",
        AvailableNpcs.Count,
        AvailableFactions.Count);
      await LoadAvailableOutfitsAsync();
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to scan NPCs.");
      StatusMessage = $"Error scanning NPCs: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [ReactiveCommand]
  private void SelectDistributionFilePath()
  {
    if (!IsCreatingNewFile)
    {
      return;
    }

    var dialog = new SaveFileDialog
                 {
                   Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*", DefaultExt = "ini", FileName = NewFileName
                 };

    // Use output folder as base if set, otherwise fall back to data path
    var targetDirectory = !string.IsNullOrWhiteSpace(_settings.OutputPatchPath) &&
                          Directory.Exists(_settings.OutputPatchPath)
                            ? _settings.OutputPatchPath
                            : _settings.SkyrimDataPath;

    if (!string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory))
    {
      var defaultDir = PathUtilities.GetSkyPatcherNpcPath(targetDirectory);
      dialog.InitialDirectory = Directory.Exists(defaultDir) ? defaultDir : targetDirectory;
    }

    if (dialog.ShowDialog() == true)
    {
      NewFileName          = Path.GetFileName(dialog.FileName);
      DistributionFilePath = dialog.FileName;
      _logger.Debug("Selected distribution file path: {FilePath}", DistributionFilePath);
    }
  }

  /// <summary>
  ///   Refreshes the file dropdown after a file is saved. Preserves current selection.
  ///   For initial setup, use InitializeFromCache() instead.
  /// </summary>
  private void RefreshAvailableDistributionFiles()
  {
    var previousSelected = SelectedDistributionFile;
    var justSaved        = _justSavedFilePath;
    _justSavedFilePath = null;

    _logger.Debug(
      "Refreshing distribution files dropdown. Previous selection: {Selection}, NewFileName: {NewFileName}, JustSaved: {JustSaved}",
      previousSelected?.DisplayName,
      NewFileName,
      justSaved);

    var files                    = _cache.AllDistributionFiles.ToList();
    var (items, dropdownItems)   = DistributionFileDropdownBuilder.Build(files);
    AvailableDistributionFiles   = new ObservableCollection<DistributionFileSelectionItem>(items);
    DropdownItems                = dropdownItems;
    this.RaisePropertyChanged(nameof(SelectedDropdownItem));

    var resolved = DistributionFileDropdownBuilder.ResolveSelection(
      items,
      justSaved,
      previousSelected,
      _guiSettings.LastDistributionFilePath);

    if (resolved != null && !resolved.IsNewFile && justSaved != null && resolved.File?.FullPath == justSaved)
    {
      _logger.Debug("Selecting just-saved file: {Path}", justSaved);
      IsCreatingNewFile        = false;
      NewFileName              = string.Empty;
      SelectedDistributionFile = resolved;
    }
    else
    {
      SelectedDistributionFile = resolved;
    }
  }

  private void UpdateDistributionFilePathForFormat()
  {
    var newPath = _filePathService.UpdatePathForFormat(
      DistributionFilePath,
      IsCreatingNewFile,
      NewFileName,
      DistributionFormat);

    if (newPath != null)
    {
      DistributionFilePath = newPath;
    }
  }

  private void UpdateFileContent()
  {
    try
    {
      var hasSpidOnlyEntries = DistributionEntries.Any(e =>
                                                         e.UseChance ||
                                                         e.Type == DistributionType.Keyword ||
                                                         e.Type == DistributionType.ExclusiveGroup);
      var effectiveFormat = hasSpidOnlyEntries ? DistributionFileType.Spid : DistributionFormat;

      _logger.Debug(
        "UpdateFileContent: {EntryCount} entries, effectiveFormat={Format}, hasSpidOnlyEntries={SpidOnly}",
        DistributionEntries.Count,
        effectiveFormat,
        hasSpidOnlyEntries);

      DistributionFileContent =
        DistributionFileFormatter.GenerateFileContent(DistributionEntries, effectiveFormat, ParseErrors);

      _logger.Debug("UpdateFileContent: Generated {LineCount} lines", DistributionFileContent.Split('\n').Length);
      DetectConflicts();

      RaiseHighlightRequestForChangedEntry();
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Error updating distribution file content");
      DistributionFileContent = $"; Error generating file content: {ex.Message}";
    }
  }

  private void RaiseHighlightRequestForChangedEntry()
  {
    var entryToHighlight = _lastChangedEntry ?? SelectedEntry;
    _lastChangedEntry = null;

    if (entryToHighlight == null || _isBulkLoading)
    {
      return;
    }

    var lineNumber = CalculateLineNumberForEntry(entryToHighlight);
    if (lineNumber < 0)
    {
      return;
    }

    var lines = DistributionFileContent.Split('\n');

    _ = lineNumber < lines.Length ? lines[lineNumber].TrimEnd('\r') : string.Empty;

    _logger.Information("RaiseHighlightRequest: line {LineNumber}", lineNumber);
    HighlightRequest = new PreviewLineHighlightRequest(lineNumber);
  }

  private int CalculateLineNumberForEntry(DistributionEntryViewModel targetEntry)
  {
    var lineNumber = DistributionFileFormatter.GenerateHeaderLines().Count;

    foreach (var entry in DistributionEntries)
    {
      var producesLine = EntryProducesLine(entry);

      if (entry == targetEntry)
      {
        return producesLine ? lineNumber : -1;
      }

      if (producesLine)
      {
        lineNumber++;
      }
    }

    return -1;
  }

  public void SelectEntryByLineNumber(int lineNumber)
  {
    var headerLineCount = DistributionFileFormatter.GenerateHeaderLines().Count;
    if (lineNumber < headerLineCount)
    {
      return;
    }

    var currentLine = headerLineCount;
    foreach (var entry in DistributionEntries)
    {
      var producesLine = EntryProducesLine(entry);

      if (!producesLine)
      {
        continue;
      }

      if (currentLine == lineNumber)
      {
        SelectedEntry = entry;
        return;
      }

      currentLine++;
    }
  }

  private static bool EntryProducesLine(DistributionEntryViewModel entry) =>
    entry.Type switch
    {
      DistributionType.Keyword => !string.IsNullOrWhiteSpace(entry.KeywordToDistribute),
      DistributionType.ExclusiveGroup => !string.IsNullOrWhiteSpace(entry.ExclusiveGroupName) &&
                                         !string.IsNullOrWhiteSpace(entry.ExclusiveGroupFormsText),
      _ => entry.SelectedOutfit != null
    };

  private void RaiseHighlightForEntry(DistributionEntryViewModel entry)
  {
    var effectiveFormat = DistributionEntries.Any(e => e.UseChance)
                            ? DistributionFileType.Spid
                            : DistributionFormat;

    var lineNumber = CalculateLineNumberForEntry(entry);
    if (lineNumber < 0)
    {
      return;
    }

    var lines       = DistributionFileContent.Split('\n');
    var lineContent = lineNumber < lines.Length ? lines[lineNumber].TrimEnd('\r') : string.Empty;
    HighlightRequest = new PreviewLineHighlightRequest(lineNumber);
  }

  private async Task LoadAvailableOutfitsAsync()
  {
    if (_outfitsLoaded && AvailableOutfits.Count > 0)
    {
      return;
    }

    if (_mutagenService.LinkCache is not { } linkCache)
    {
      if (AvailableOutfits.Count > 0)
      {
        AvailableOutfits.Clear();
      }

      _outfitsLoaded = false;
      return;
    }

    try
    {
      var outfits = await Task.Run(() =>
                                     linkCache.WinningOverrides<IOutfitGetter>().ToList());
      await MergeOutfitsFromPatchFileAsync(outfits);
      AvailableOutfits = new ObservableCollection<IOutfitGetter>(outfits);
      _outfitsLoaded   = true;
      _logger.Debug("Loaded {Count} available outfits.", outfits.Count);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to load available outfits.");
      AvailableOutfits.Clear();
    }
  }

  /// <summary>
  ///   Merges outfits from the patch file into the provided list if the patch exists
  ///   but isn't in the active load order (not enabled in plugins.txt).
  /// </summary>
  private async Task MergeOutfitsFromPatchFileAsync(List<IOutfitGetter> outfits)
  {
    var patchPath = _settings.FullOutputPath;
    if (string.IsNullOrEmpty(patchPath) || !File.Exists(patchPath))
    {
      return;
    }

    var patchOutfits     = await _mutagenService.LoadOutfitsFromPluginAsync(Path.GetFileName(patchPath));
    var existingFormKeys = outfits.Select(o => o.FormKey).ToHashSet();

    var newOutfits = patchOutfits.Where(o => !existingFormKeys.Contains(o.FormKey)).ToList();
    if (newOutfits.Count > 0)
    {
      outfits.AddRange(newOutfits);
      _logger.Information(
        "Added {Count} outfit(s) from patch file {Patch} (not in active load order).",
        newOutfits.Count,
        Path.GetFileName(patchPath));
    }
  }

  /// <summary>Triggers lazy loading of outfits when ComboBox opens.</summary>
  public void EnsureOutfitsLoaded()
  {
    if (!_outfitsLoaded)
    {
      _ = LoadAvailableOutfitsAsync();
    }
  }

  private async void OnPluginsChanged(object? sender, EventArgs e)
  {
    _logger.Debug("PluginsChanged event received in DistributionEditTabViewModel, invalidating outfits cache...");
    _outfitsLoaded = false;
    _logger.Information("Reloading available outfits...");
    await LoadAvailableOutfitsAsync();
  }

  private void OnCacheLoaded(object? sender, EventArgs e)
  {
    if (!_isInitialized)
    {
      _logger.Debug("CacheLoaded: First load, performing full initialization...");
      InitializeFromCache();
    }
    else
    {
      _logger.Debug("CacheLoaded: Already initialized, refreshing file list...");
      RefreshAvailableDistributionFiles();
    }
  }

  /// <summary>
  ///   Single initialization point after cache is fully loaded (first time only).
  ///   Linear flow: 1) populate filters, 2) populate file dropdown, 3) select &lt;New File&gt; with unique name.
  /// </summary>
  private void InitializeFromCache()
  {
    _logger.Debug("InitializeFromCache: Starting linear initialization...");
    var files                  = _cache.AllDistributionFiles.ToList();
    var (items, dropdownItems) = DistributionFileDropdownBuilder.Build(files);
    AvailableDistributionFiles = new ObservableCollection<DistributionFileSelectionItem>(items);
    DropdownItems              = dropdownItems;

    var lastFilePath = _guiSettings.LastDistributionFilePath;
    var resolved = DistributionFileDropdownBuilder.ResolveSelection(items, null, null, lastFilePath);

    if (resolved != null && !resolved.IsNewFile && resolved.File != null)
    {
      _logger.Debug("Restoring last saved file from settings: {Path}", resolved.File.FullPath);
      DistributionFilePath     = resolved.File.FullPath;
      SelectedDistributionFile = resolved;
      this.RaisePropertyChanged(nameof(SelectedDropdownItem));
      _isInitialized = true;
      this.RaisePropertyChanged(nameof(IsInitialized));
      _logger.Information(
        "Initialization complete: {NpcCount} NPCs, {FactionCount} factions, {KeywordCount} keywords, {RaceCount} races, {FileCount} files. Restored file: {FilePath}",
        FilteredNpcs.Count,
        FilteredFactions.Count,
        FilteredKeywords.Count,
        FilteredRaces.Count,
        files.Count,
        resolved.File.FullPath);
      _ = LoadDistributionFileAsync();
      return;
    }

    var newFileItem = AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
    if (newFileItem != null)
    {
      NewFileName = GenerateUniqueNewFileName();
      _logger.Debug("Generated unique filename: {FileName}", NewFileName);
      SelectedDistributionFile = newFileItem;
    }

    this.RaisePropertyChanged(nameof(SelectedDropdownItem));
    _isInitialized = true;
    this.RaisePropertyChanged(nameof(IsInitialized));
    _logger.Information(
      "Initialization complete: {NpcCount} NPCs, {FactionCount} factions, {KeywordCount} keywords, {RaceCount} races, {FileCount} files. NewFileName={NewFileName}",
      FilteredNpcs.Count,
      FilteredFactions.Count,
      FilteredKeywords.Count,
      FilteredRaces.Count,
      files.Count,
      NewFileName);
  }

  private string GenerateUniqueNewFileName()
  {
    const string baseName = "Boutique_Distribution";
    var existingFileNames = AvailableDistributionFiles
                            .Where(f => !f.IsNewFile && f.File != null)
                            .Select(f => f.File!.FileName)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (IsBaseNameAvailable(baseName, existingFileNames))
    {
      return $"{baseName}.ini";
    }

    for (var i = 2; i < 1000; i++)
    {
      var numberedBase = $"{baseName}_{i}";
      if (IsBaseNameAvailable(numberedBase, existingFileNames))
      {
        return $"{numberedBase}.ini";
      }
    }

    return $"{baseName}_{Guid.NewGuid():N}.ini";
  }

  private static bool IsBaseNameAvailable(string baseName, HashSet<string> existingFileNames) =>
    !existingFileNames.Contains($"{baseName}.ini") &&
    !existingFileNames.Contains($"{baseName}_DISTR.ini");

  [ReactiveCommand(CanExecute = nameof(_notLoading))]
  private async Task PreviewEntryAsync(DistributionEntryViewModel? entry)
  {
    if (entry == null || entry.SelectedOutfit == null)
    {
      StatusMessage = "No outfit selected for preview.";
      return;
    }

    if (!_mutagenService.IsInitialized ||
        _mutagenService.LinkCache is not { } linkCache)
    {
      StatusMessage = "Initialize Skyrim data path before previewing outfits.";
      return;
    }

    var outfit = entry.SelectedOutfit;
    var label  = outfit.EditorID ?? outfit.FormKey.ToString();

    var initialResult = OutfitResolver.GatherArmorPieces(outfit, linkCache, Environment.TickCount);
    if (initialResult.ArmorPieces.Count == 0)
    {
      StatusMessage = $"Outfit '{label}' has no armor pieces to preview.";
      return;
    }

    try
    {
      StatusMessage = $"Building preview for {label}...";

      var initialGender = entry.Gender switch
      {
        GenderFilter.Male   => GenderedModelVariant.Male,
        GenderFilter.Female => GenderedModelVariant.Female,
        _                   => GenderedModelVariant.Female
      };

      var metadata = new OutfitMetadata(
        label,
        outfit.FormKey.ModKey.FileName.String,
        false,
        initialResult.ContainsLeveledItems);
      var collection = new ArmorPreviewSceneCollection(
        1,
        0,
        [metadata],
        async (_, gender) =>
        {
          var result = OutfitResolver.GatherArmorPieces(outfit, linkCache, Environment.TickCount);
          var scene  = await _armorPreviewService.BuildPreviewAsync(result.ArmorPieces, gender);
          return scene with { OutfitLabel = label, SourceFile = outfit.FormKey.ModKey.FileName.String };
        },
        initialGender);

      await ShowPreview.Handle(collection);
      StatusMessage = $"Preview ready for {label}.";
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to preview outfit {Identifier}", label);
      StatusMessage = $"Failed to preview outfit: {ex.Message}";
    }
  }

  /// <summary>
  ///   Detects conflicts between the current distribution entries and existing distribution files.
  ///   Updates HasConflicts, ConflictSummary, and NPC conflict indicators.
  /// </summary>
  private void DetectConflicts()
  {
    if (!IsCreatingNewFile || DistributionEntries.Count == 0 || _mutagenService.LinkCache is not { } linkCache || DistributionFiles.Count == 0)
    {
      ResetConflictState(NewFileName);
      return;
    }

    var entryCountAtStart = DistributionEntries.Count;
    var entriesSnapshot   = DistributionEntries.ToList();
    Task.Run(() =>
    {
      try
      {
        var result = DistributionConflictDetectionService.DetectConflicts(
          entriesSnapshot,
          [.. DistributionFiles],
          NewFileName,
          linkCache);
        Application.Current?.Dispatcher.Invoke(() =>
        {
          if (DistributionEntries.Count == entryCountAtStart && DistributionEntries.Count > 0)
          {
            HasConflicts                = result.HasConflicts;
            ConflictsResolvedByFilename = result.ConflictsResolvedByFilename;
            ConflictSummary             = result.ConflictSummary;
            HasOverlaps                 = result.HasOverlaps;
            OverlapsResolvedByFilename  = result.OverlapsResolvedByFilename;
            OverlapSummary              = result.OverlapSummary;
            SuggestedFileName           = result.SuggestedFileName;
            DistributionConflictDetectionService.ApplyConflictIndicators(result, DistributionEntries);
          }
          else
          {
            _logger.Debug("Conflict detection completed but entries were cleared/changed, clearing conflict state");
            ResetConflictState(NewFileName);
          }
        });
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error during conflict detection");
      }
    });
  }

  private void ResetConflictState(string suggestedFileName = "")
  {
    HasConflicts                = false;
    ConflictsResolvedByFilename = false;
    ConflictSummary             = string.Empty;
    HasOverlaps                 = false;
    OverlapsResolvedByFilename  = false;
    OverlapSummary              = string.Empty;
    SuggestedFileName           = suggestedFileName;
    ClearNpcConflictIndicators();
  }

  private void ClearNpcConflictIndicators() =>
    DistributionConflictDetectionService.ClearConflictIndicators(DistributionEntries);

  private void UpdateMatchingNpcsForEntry(DistributionEntryViewModel? entry)
  {
    if (entry == null || _cache.AllNpcs.Count == 0)
    {
      _matchingNpcsForSelectedEntry = [];
      this.RaisePropertyChanged(nameof(MatchingNpcsForSelectedEntry));
      this.RaisePropertyChanged(nameof(MatchingNpcsCount));
      return;
    }

    try
    {
      var matchingNpcs = FilterMatchingService.GetMatchingNpcsForEntry(
                            _cache.AllNpcs,
                            entry.Entry,
                            _cache.SimulatedKeywordsByNpc);
      _matchingNpcsForSelectedEntry = matchingNpcs;
      this.RaisePropertyChanged(nameof(MatchingNpcsForSelectedEntry));
      this.RaisePropertyChanged(nameof(MatchingNpcsCount));
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Failed to compute matching NPCs for entry");
      _matchingNpcsForSelectedEntry = [];
      this.RaisePropertyChanged(nameof(MatchingNpcsForSelectedEntry));
      this.RaisePropertyChanged(nameof(MatchingNpcsCount));
    }
  }

  /// <summary>
  ///   Called when a user enables chance-based distribution.
  ///   Returns true if format is currently SkyPatcher (will be changed to SPID), false if already SPID.
  /// </summary>
  private bool IsFormatChangingToSpid()
  {
    if (DistributionFormat != DistributionFileType.SkyPatcher)
    {
      return false;
    }

    DistributionFormat = DistributionFileType.Spid;
    UpdateFileContent();
    return true;
  }

  private DistributionEntryViewModel CreateEntryViewModel(DistributionEntry entry)
  {
    var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry, IsFormatChangingToSpid);
    _hydrationService.HydrateEntry(entryVm, entry, AvailableOutfits);
    return entryVm;
  }
}
