using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Threading;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Boutique.Views;
using DynamicData;
using DynamicData.Binding;
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

public sealed partial class DistributionEditTabViewModel : ReactiveObject, IDisposable
{
  private readonly ArmorPreviewService                                 _armorPreviewService;
  private readonly DistributionFileBackupService                       _backupService;
  private readonly GameDataCacheService                                _cache;
  private readonly IObservable<bool>                                   _canPaste;
  private readonly IObservable<bool>                                   _canSave;
  private readonly IDialogService                                      _dialogService;
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

  private List<DistributionParseError> _actualParseErrors = [];

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

  [Reactive] private bool _hasIntraFileOverlaps;

  [Reactive] private int _intraFileOverlapCount;

  /// <summary>
  ///   True if any distribution entry is a keyword distribution.
  ///   When true, SkyPatcher format is not available (it doesn't support keyword distributions).
  /// </summary>
  [Reactive] private bool _hasKeywordDistributions;

  [Reactive] private PreviewLineHighlightRequest? _highlightRequest;

  private IntraFileConflictResult? _intraFileConflictResult;

  private bool _isBulkLoading;

  [Reactive] private bool _isCreatingNewFile;

  private bool _isInitialized;

  [Reactive] private bool _isLoading;

  private string? _justSavedFilePath;

  private string? _lastSavedContent;

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
    DistributionFileBackupService backupService,
    IDialogService dialogService,
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
    _backupService       = backupService;
    _dialogService       = dialogService;
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
  public IReadOnlyList<DistributionParseError> ActualParseErrors => _actualParseErrors;

  public bool HasParseErrors => _actualParseErrors.Count > 0;

  private void RefreshActualParseErrors()
  {
    _actualParseErrors = ParseErrors
                         .Where(e => !e.Reason.EndsWith("(preserved)", StringComparison.Ordinal))
                         .ToList();
    this.RaisePropertyChanged(nameof(ActualParseErrors));
    this.RaisePropertyChanged(nameof(HasParseErrors));
  }

  public bool HasUnsavedChanges =>
    DistributionEntries.Count > 0 &&
    !string.Equals(DistributionFileContent, _lastSavedContent, StringComparison.Ordinal);

  public ObservableCollection<DistributionEntryViewModel> DistributionEntries => _distributionEntries;

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
              RefreshActualParseErrors();
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
      _intraFileConflictResult = null;
      HasIntraFileOverlaps     = false;
      IntraFileOverlapCount    = 0;
      return;
    }

    var result = DistributionConflictDetectionService.DetectIntraFile(
                   outfitEntries,
                   _cache.AllNpcs.ToList(),
                   _cache.SimulatedKeywordsByNpc);
    _intraFileConflictResult = result;
    HasIntraFileOverlaps     = result.TotalOverlappingNpcCount > 0;
    IntraFileOverlapCount    = result.TotalOverlappingNpcCount;
  }

  [ReactiveCommand]
  private void ViewIntraFileOverlaps()
  {
    if (_intraFileConflictResult is null)
    {
      return;
    }

    var window = new IntraFileOverlapsWindow(_intraFileConflictResult)
    {
      Owner = Application.Current.MainWindow
    };
    window.Show();
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
      var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry, IsFormatChangingToSpid, _dialogService);

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

    var addedCount = selectedItems.Count(item => addToEntry(SelectedEntry, item));

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
}
