using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Microsoft.Win32;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Boutique.ViewModels;

public class DistributionEditTabViewModel : ReactiveObject
{
    private readonly DistributionFileWriterService _fileWriterService;
    private readonly ArmorPreviewService _armorPreviewService;
    private readonly MutagenService _mutagenService;
    private readonly GameDataCacheService _cache;
    private readonly SettingsViewModel _settings;
    private readonly GuiSettingsService _guiSettings;
    private readonly ILogger _logger;

    private ObservableCollection<DistributionEntryViewModel> _distributionEntries = [];
    private bool _isBulkLoading;
    private bool _outfitsLoaded;
    private string? _justSavedFilePath;
    private readonly Dictionary<DistributionEntryViewModel, IDisposable> _useChanceSubscriptions = new();
    private readonly Dictionary<DistributionEntryViewModel, IDisposable> _typeSubscriptions = new();

    public DistributionEditTabViewModel(
        DistributionFileWriterService fileWriterService,
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
        GameDataCacheService cache,
        SettingsViewModel settings,
        GuiSettingsService guiSettings,
        ILogger logger)
    {
        _fileWriterService = fileWriterService;
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _cache = cache;
        _settings = settings;
        _guiSettings = guiSettings;
        _logger = logger.ForContext<DistributionEditTabViewModel>();

        _mutagenService.PluginsChanged += OnPluginsChanged;
        _cache.CacheLoaded += OnCacheLoaded;

        if (_cache.IsLoaded)
        {
            InitializeFromCache();
        }

        _distributionEntries.CollectionChanged += OnDistributionEntriesChanged;

        AddDistributionEntryCommand = ReactiveCommand.Create(AddDistributionEntry);
        RemoveDistributionEntryCommand = ReactiveCommand.Create<DistributionEntryViewModel>(RemoveDistributionEntry);
        SelectEntryCommand = ReactiveCommand.Create<DistributionEntryViewModel>(SelectEntry);

        var hasEntries = this.WhenAnyValue(vm => vm.DistributionEntriesCount, count => count > 0);
        AddSelectedNpcsToEntryCommand = ReactiveCommand.Create(AddSelectedNpcsToEntry, hasEntries);
        AddSelectedFactionsToEntryCommand = ReactiveCommand.Create(AddSelectedFactionsToEntry, hasEntries);
        AddSelectedKeywordsToEntryCommand = ReactiveCommand.Create(AddSelectedKeywordsToEntry, hasEntries);
        AddSelectedRacesToEntryCommand = ReactiveCommand.Create(AddSelectedRacesToEntry, hasEntries);
        AddSelectedClassesToEntryCommand = ReactiveCommand.Create(AddSelectedClassesToEntry, hasEntries);

        var notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);
        var canSave = this.WhenAnyValue(
            vm => vm.DistributionEntriesCount,
            vm => vm.DistributionFilePath,
            vm => vm.IsCreatingNewFile,
            vm => vm.NewFileName,
            (count, path, isNew, newName) =>
                count > 0 &&
                (!string.IsNullOrWhiteSpace(path) || (isNew && !string.IsNullOrWhiteSpace(newName))));

        SaveDistributionFileCommand = ReactiveCommand.CreateFromTask(SaveDistributionFileAsync, canSave);
        ScanNpcsCommand = ReactiveCommand.CreateFromTask(ScanNpcsAsync, notLoading);
        SelectDistributionFilePathCommand = ReactiveCommand.Create(SelectDistributionFilePath);
        PreviewEntryCommand = ReactiveCommand.CreateFromTask<DistributionEntryViewModel>(PreviewEntryAsync, notLoading);

        var canPaste = this.WhenAnyValue(
            vm => vm.HasCopiedFilter,
            vm => vm.SelectedEntry,
            (hasCopied, entry) => hasCopied && entry != null);
        PasteFilterToEntryCommand = ReactiveCommand.Create(PasteFilterToEntry, canPaste);

        this.WhenAnyValue(vm => vm.CopiedFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(HasCopiedFilter)));

        this.WhenAnyValue(vm => vm.NpcSearchText)
            .Subscribe(_ => UpdateFilteredNpcs());
        this.WhenAnyValue(vm => vm.FactionSearchText)
            .Subscribe(_ => UpdateFilteredFactions());
        this.WhenAnyValue(vm => vm.KeywordSearchText)
            .Subscribe(_ => UpdateFilteredKeywords());
        this.WhenAnyValue(vm => vm.RaceSearchText)
            .Subscribe(_ => UpdateFilteredRaces());
        this.WhenAnyValue(vm => vm.ClassSearchText)
            .Subscribe(_ => UpdateFilteredClasses());

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
    }

    [Reactive] public bool IsLoading { get; private set; }

    [Reactive] public string StatusMessage { get; private set; } = string.Empty;

    [Reactive] public IReadOnlyList<DistributionParseError> ParseErrors { get; private set; } = [];

    /// <summary>
    /// Actual parse errors (excludes preserved lines like keyword distributions).
    /// </summary>
    public IReadOnlyList<DistributionParseError> ActualParseErrors =>
        ParseErrors.Where(e => !e.Reason.EndsWith("(preserved)", StringComparison.Ordinal)).ToList();

    public bool HasParseErrors => ActualParseErrors.Count > 0;

    public bool IsFilePreviewExpanded
    {
        get => _guiSettings.IsFilePreviewExpanded;
        set
        {
            if (_guiSettings.IsFilePreviewExpanded == value) return;
            _guiSettings.IsFilePreviewExpanded = value;
            this.RaisePropertyChanged();
        }
    }

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

    /// <summary>Available NPCs for distribution entry selection (from cache).</summary>
    public ObservableCollection<NpcRecordViewModel> AvailableNpcs => _cache.AllNpcRecords;

    /// <summary>Available factions for distribution entry selection (from cache).</summary>
    public ObservableCollection<FactionRecordViewModel> AvailableFactions => _cache.AllFactions;

    /// <summary>Available keywords for distribution entry selection (from cache).</summary>
    public ObservableCollection<KeywordRecordViewModel> AvailableKeywords => _cache.AllKeywords;

    /// <summary>Available races for distribution entry selection (from cache).</summary>
    public ObservableCollection<RaceRecordViewModel> AvailableRaces => _cache.AllRaces;

    /// <summary>Available classes for distribution entry selection (from cache).</summary>
    public ObservableCollection<ClassRecordViewModel> AvailableClasses => _cache.AllClasses;

    [Reactive] public ObservableCollection<IOutfitGetter> AvailableOutfits { get; private set; } = [];

    [Reactive] public ObservableCollection<DistributionFileSelectionItem> AvailableDistributionFiles { get; private set; } = [];

    /// <summary>
    /// Organized dropdown items with headers and files for tree-ish display.
    /// </summary>
    [Reactive] public IReadOnlyList<DistributionDropdownItem> DropdownItems { get; private set; } = [];

    /// <summary>
    /// The currently selected dropdown item. Headers are not selectable.
    /// </summary>
    public DistributionDropdownItem? SelectedDropdownItem
    {
        get
        {
            if (SelectedDistributionFile == null) return null;
            if (SelectedDistributionFile.IsNewFile) return DistributionNewFileItem.Instance;

            return DropdownItems.OfType<DistributionFileItem>()
                .FirstOrDefault(f => f.FullPath == SelectedDistributionFile.File?.FullPath);
        }
        set
        {
            if (value is DistributionGroupHeader)
                return; // Headers are not selectable

            if (value is DistributionNewFileItem)
            {
                SelectedDistributionFile = AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
            }
            else if (value is DistributionFileItem fileItem)
            {
                var match = AvailableDistributionFiles.FirstOrDefault(f =>
                    !f.IsNewFile && f.File?.FullPath == fileItem.FullPath);
                if (match != null)
                    SelectedDistributionFile = match;
            }

            this.RaisePropertyChanged(nameof(SelectedDropdownItem));
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
                    NewFileName = string.Empty;
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
                            HasConflicts = false;
                            ConflictsResolvedByFilename = false;
                            ConflictSummary = string.Empty;
                            SuggestedFileName = string.Empty;
                            ClearNpcConflictIndicators();
                        }
                        finally
                        {
                            _isBulkLoading = false;
                        }
                        this.RaisePropertyChanged(nameof(DistributionEntriesCount));
                        UpdateFileContent();
                        UpdateHasChanceBasedEntries();
                        UpdateHasKeywordDistributions();
                    }
                    if (string.IsNullOrWhiteSpace(NewFileName))
                    {
                        NewFileName = GenerateUniqueNewFileName();
                    }
                    UpdateDistributionFilePathFromNewFileName();
                }
            }

            if (!Equals(previous, value))
            {
                this.RaisePropertyChanged(nameof(ShowNewFileNameInput));
                this.RaisePropertyChanged(nameof(SaveDistributionFileCommand));
            }
        }
    }

    [Reactive] public bool IsCreatingNewFile { get; private set; }

    public bool ShowNewFileNameInput => IsCreatingNewFile;

    public string NewFileName
    {
        get => field ?? string.Empty;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value ?? string.Empty);
            if (IsCreatingNewFile)
            {
                UpdateDistributionFilePathFromNewFileName();
                DetectConflicts();
            }
        }
    }

    [Reactive] public string DistributionFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// The actual filename that will be saved (derived from DistributionFilePath).
    /// For SPID format, this includes the _DISTR suffix.
    /// </summary>
    public string ActualFileName => !string.IsNullOrEmpty(DistributionFilePath)
        ? Path.GetFileName(DistributionFilePath)
        : string.Empty;

    [Reactive] public string NpcSearchText { get; set; } = string.Empty;

    [Reactive] public string FactionSearchText { get; set; } = string.Empty;

    [Reactive] public string KeywordSearchText { get; set; } = string.Empty;

    [Reactive] public string RaceSearchText { get; set; } = string.Empty;

    [Reactive] public string ClassSearchText { get; set; } = string.Empty;

    [Reactive] public string DistributionFileContent { get; private set; } = string.Empty;

    /// <summary>
    /// The distribution file format (SPID or SkyPatcher).
    /// Defaults to SkyPatcher for new files, or detected from existing files.
    /// </summary>
    public DistributionFileType DistributionFormat
    {
        get => _distributionFormat;
        set
        {
            if (_distributionFormat == value)
                return;

            _logger.Debug("DistributionFormat changing from {OldFormat} to {NewFormat}, EntryCount={Count}",
                _distributionFormat, value, DistributionEntries.Count);

            this.RaiseAndSetIfChanged(ref _distributionFormat, value);
            UpdateFileContent();
        }
    }
    private DistributionFileType _distributionFormat = DistributionFileType.SkyPatcher;
    public IReadOnlyList<DistributionFileType> AvailableFormats { get; } =
        new[] { DistributionFileType.Spid, DistributionFileType.SkyPatcher };

    /// <summary>
    /// True if any distribution entry has chance-based distribution enabled.
    /// When true, SkyPatcher format is not available (it doesn't support chance).
    /// </summary>
    [Reactive] public bool HasChanceBasedEntries { get; private set; }

    /// <summary>
    /// True if any distribution entry is a keyword distribution.
    /// When true, SkyPatcher format is not available (it doesn't support keyword distributions).
    /// </summary>
    [Reactive] public bool HasKeywordDistributions { get; private set; }

    [Reactive] public ObservableCollection<NpcRecordViewModel> FilteredNpcs { get; private set; } = [];

    [Reactive] public ObservableCollection<FactionRecordViewModel> FilteredFactions { get; private set; } = [];

    [Reactive] public ObservableCollection<KeywordRecordViewModel> FilteredKeywords { get; private set; } = [];

    [Reactive] public ObservableCollection<RaceRecordViewModel> FilteredRaces { get; private set; } = [];

    [Reactive] public ObservableCollection<ClassRecordViewModel> FilteredClasses { get; private set; } = [];
    [Reactive] public bool HasConflicts { get; private set; }
    [Reactive] public bool ConflictsResolvedByFilename { get; private set; }
    [Reactive] public string ConflictSummary { get; private set; } = string.Empty;
    [Reactive] public string SuggestedFileName { get; private set; } = string.Empty;
    [Reactive] public CopiedNpcFilter? CopiedFilter { get; set; }
    public bool HasCopiedFilter => CopiedFilter != null;

    public ReactiveCommand<Unit, Unit> AddDistributionEntryCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> RemoveDistributionEntryCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> SelectEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedNpcsToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedFactionsToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedKeywordsToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedRacesToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedClassesToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveDistributionFileCommand { get; }
    public ReactiveCommand<Unit, Unit> ScanNpcsCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectDistributionFilePathCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> PreviewEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> PasteFilterToEntryCommand { get; }

    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    public bool IsInitialized => _mutagenService.IsInitialized;
    private IReadOnlyList<DistributionFileViewModel> DistributionFiles => _cache.AllDistributionFiles;

    private void OnDistributionEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_isBulkLoading)
            return;

        _logger.Debug("OnDistributionEntriesChanged: Action={Action}, NewItems={NewCount}, OldItems={OldCount}",
            e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);

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

        _logger.Debug("OnDistributionEntriesChanged completed");
    }

    private void SubscribeToEntryChanges(DistributionEntryViewModel entry)
    {
        Observable.FromEventPattern(entry, nameof(entry.EntryChanged))
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateFileContent());

        var useChanceSub = entry.WhenAnyValue(e => e.UseChance)
            .Subscribe(_ => UpdateHasChanceBasedEntries());
        _useChanceSubscriptions[entry] = useChanceSub;

        var typeSub = entry.WhenAnyValue(e => e.Type)
            .Subscribe(_ => UpdateHasKeywordDistributions());
        _typeSubscriptions[entry] = typeSub;
    }

    private void UnsubscribeFromEntryChanges(DistributionEntryViewModel entry)
    {
        if (_useChanceSubscriptions.TryGetValue(entry, out var sub))
        {
            sub.Dispose();
            _useChanceSubscriptions.Remove(entry);
        }
        if (_typeSubscriptions.TryGetValue(entry, out var typeSub))
        {
            typeSub.Dispose();
            _typeSubscriptions.Remove(entry);
        }
    }

    private void UpdateHasChanceBasedEntries() =>
        HasChanceBasedEntries = DistributionEntries.Any(e => e.UseChance);

    private void UpdateHasKeywordDistributions() =>
        HasKeywordDistributions = DistributionEntries.Any(e => e.Type == DistributionType.Keyword);

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
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                SelectedEntry = entryVm;
                _logger.Debug("SelectedEntry set");
            }), System.Windows.Threading.DispatcherPriority.Background);

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
    /// <param name="getTargetCollection">Function to get the target collection from the entry.</param>
    /// <param name="addToEntry">Action to add an item to the entry.</param>
    /// <param name="itemTypeName">The display name for the item type (e.g., "NPC", "faction").</param>
    private void AddSelectedCriteriaToEntry<T>(
        ObservableCollection<T> filteredItems,
        Func<DistributionEntryViewModel, ObservableCollection<T>> getTargetCollection,
        Action<DistributionEntryViewModel, T> addToEntry,
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

        var targetCollection = getTargetCollection(SelectedEntry);
        var addedCount = 0;
        foreach (var item in selectedItems)
        {
            if (!targetCollection.Any(existing => existing.FormKey == item.FormKey))
            {
                addToEntry(SelectedEntry, item);
                addedCount++;
            }
        }

        foreach (var item in selectedItems)
        {
            item.IsSelected = false;
        }

        if (addedCount > 0)
        {
            StatusMessage = $"Added {addedCount} {itemTypeName}(s) to entry: {SelectedEntry.SelectedOutfit?.EditorID ?? "(No outfit)"}";
            _logger.Debug("Added {Count} {ItemType}s to entry", addedCount, itemTypeName);
        }
        else
        {
            StatusMessage = $"All selected {itemTypeName}s are already in this entry.";
        }
    }

    private void AddSelectedNpcsToEntry() =>
        AddSelectedCriteriaToEntry(
            FilteredNpcs,
            entry => entry.SelectedNpcs,
            (entry, npc) => entry.AddNpc(npc),
            "NPC");

    private void AddSelectedFactionsToEntry() =>
        AddSelectedCriteriaToEntry(
            FilteredFactions,
            entry => entry.SelectedFactions,
            (entry, faction) => entry.AddFaction(faction),
            "faction");

    private void AddSelectedKeywordsToEntry() =>
        AddSelectedCriteriaToEntry(
            FilteredKeywords,
            entry => entry.SelectedKeywords,
            (entry, keyword) => entry.AddKeyword(keyword),
            "keyword");

    private void AddSelectedRacesToEntry() =>
        AddSelectedCriteriaToEntry(
            FilteredRaces,
            entry => entry.SelectedRaces,
            (entry, race) => entry.AddRace(race),
            "race");

    private void AddSelectedClassesToEntry() =>
        AddSelectedCriteriaToEntry(
            FilteredClasses,
            entry => entry.SelectedClasses,
            (entry, classVm) => entry.AddClass(classVm),
            "class");

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
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (SelectedEntry != null)
                    {
                        ApplyFilterToEntry(SelectedEntry, CopiedFilter);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }
        }

        ApplyFilterToEntry(SelectedEntry, CopiedFilter);
    }

    private void ApplyFilterToEntry(DistributionEntryViewModel entry, CopiedNpcFilter filter)
    {
        var addedItems = new List<string>();

        foreach (var factionFormKey in filter.Factions)
        {
            var factionVm = ResolveFactionFormKey(factionFormKey);
            if (factionVm != null && !entry.SelectedFactions.Any(f => f.FormKey == factionFormKey))
            {
                entry.AddFaction(factionVm);
                addedItems.Add($"faction:{factionVm.DisplayName}");
            }
        }

        foreach (var raceFormKey in filter.Races)
        {
            var raceVm = ResolveRaceFormKey(raceFormKey);
            if (raceVm != null && !entry.SelectedRaces.Any(r => r.FormKey == raceFormKey))
            {
                entry.AddRace(raceVm);
                addedItems.Add($"race:{raceVm.DisplayName}");
            }
        }

        foreach (var keywordFormKey in filter.Keywords)
        {
            var keywordVm = ResolveKeywordByFormKey(keywordFormKey);
            if (keywordVm != null && !entry.SelectedKeywords.Any(k =>
                string.Equals(k.KeywordRecord.EditorID, keywordVm.KeywordRecord.EditorID, StringComparison.OrdinalIgnoreCase)))
            {
                entry.AddKeyword(keywordVm);
                addedItems.Add($"keyword:{keywordVm.DisplayName}");
            }
        }

        foreach (var classFormKey in filter.Classes)
        {
            var classVm = ResolveClassFormKey(classFormKey);
            if (classVm != null && !entry.SelectedClasses.Any(c => c.FormKey == classFormKey))
            {
                entry.AddClass(classVm);
                addedItems.Add($"class:{classVm.DisplayName}");
            }
        }

        if (filter.HasTraitFilters)
        {
            if (filter.IsFemale.HasValue)
            {
                entry.Gender = filter.IsFemale.Value ? GenderFilter.Female : GenderFilter.Male;
                addedItems.Add(filter.IsFemale.Value ? "trait:Female" : "trait:Male");
            }
            if (filter.IsUnique.HasValue)
            {
                entry.Unique = filter.IsUnique.Value ? UniqueFilter.UniqueOnly : UniqueFilter.NonUniqueOnly;
                addedItems.Add(filter.IsUnique.Value ? "trait:Unique" : "trait:Non-Unique");
            }
            if (filter.IsChild.HasValue)
            {
                entry.IsChild = filter.IsChild.Value;
                addedItems.Add(filter.IsChild.Value ? "trait:Child" : "trait:Adult");
            }
        }

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

    private void SelectEntry(DistributionEntryViewModel entryVm)
    {
        SelectedEntry = entryVm; // Property setter handles IsSelected updates
        _logger.Debug("Selected distribution entry: {Outfit}", entryVm?.SelectedOutfit?.EditorID ?? "(No outfit)");
    }

    private async Task SaveDistributionFileAsync()
    {
        if (string.IsNullOrWhiteSpace(DistributionFilePath))
        {
            StatusMessage = "Please select a file path.";
            return;
        }

        if (DistributionEntries.Count == 0)
        {
            StatusMessage = "No distribution entries to save.";
            return;
        }

        DetectConflicts();

        var finalFilePath = DistributionFilePath;
        var finalFileName = Path.GetFileName(DistributionFilePath);

        if (IsCreatingNewFile && HasConflicts && !string.IsNullOrEmpty(SuggestedFileName))
        {
            var sb = new System.Text.StringBuilder();
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

            var result = System.Windows.MessageBox.Show(
                sb.ToString(),
                "Conflicts Detected - Filename Change Required",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Cancel)
            {
                StatusMessage = "Save cancelled.";
                return;
            }

            if (result == System.Windows.MessageBoxResult.Yes)
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
            var result = System.Windows.MessageBox.Show(
                $"The file '{Path.GetFileName(finalFilePath)}' already exists.\n\nDo you want to overwrite it?",
                "Confirm Overwrite",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                StatusMessage = "Save cancelled.";
                return;
            }
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Saving distribution file...";

            // Write the preview text directly - it's already correctly formatted
            // This ensures the saved file matches exactly what the user sees in the preview
            var directory = Path.GetDirectoryName(finalFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(finalFilePath, DistributionFileContent, System.Text.Encoding.UTF8);

            StatusMessage = $"Successfully saved distribution file: {Path.GetFileName(finalFilePath)}";
            _logger.Information("Saved distribution file: {FilePath} ({LineCount} lines)",
                finalFilePath, DistributionFileContent.Split('\n').Length);

            // Track the saved file path so RefreshAvailableDistributionFiles can select it
            _justSavedFilePath = finalFilePath;
            _guiSettings.LastDistributionFilePath = finalFilePath;
            DistributionFilePath = finalFilePath;

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
        _logger.Debug("LoadDistributionFileAsync called. DistributionFilePath: {Path}, Exists: {Exists}",
            DistributionFilePath, File.Exists(DistributionFilePath ?? string.Empty));

        if (string.IsNullOrWhiteSpace(DistributionFilePath) || !File.Exists(DistributionFilePath))
        {
            StatusMessage = "File does not exist. Please select a valid file.";
            _logger.Warning("Cannot load file - path is empty or file does not exist: {Path}", DistributionFilePath);
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Loading distribution file...";
            _logger.Information("Loading distribution file: {FilePath}", DistributionFilePath);

            var (entries, detectedFormat, parseErrors) = await ((DistributionFileWriterService)_fileWriterService).LoadDistributionFileWithErrorsAsync(
                DistributionFilePath);
            DistributionFormat = detectedFormat;
            ParseErrors = parseErrors;
            this.RaisePropertyChanged(nameof(ActualParseErrors));
            this.RaisePropertyChanged(nameof(HasParseErrors));

            await LoadAvailableOutfitsAsync();
            _isBulkLoading = true;
            try
            {
                DistributionEntries.Clear();
                HasConflicts = false;
                ConflictsResolvedByFilename = false;
                ConflictSummary = string.Empty;
                SuggestedFileName = string.Empty;
                ClearNpcConflictIndicators();
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

            var statusMsg = $"Loaded {entries.Count} distribution entries from {Path.GetFileName(DistributionFilePath)}";
            if (parseErrors.Count > 0)
                statusMsg += $" ({parseErrors.Count} line(s) could not be parsed)";
            StatusMessage = statusMsg;
            _logger.Information("Loaded distribution file: {FilePath} with {Count} entries, {ErrorCount} parse errors",
                DistributionFilePath, entries.Count, parseErrors.Count);
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
            if (!_cache.IsLoaded && !_cache.IsLoading)
            {
                StatusMessage = "Loading game data (NPCs, factions, keywords, races, classes)...";
                await _cache.LoadAsync();
            }
            else if (_cache.IsLoading)
            {
                StatusMessage = "Loading game data from plugins...";
                while (_cache.IsLoading)
                    await Task.Delay(100);
            }
            UpdateFilteredNpcs();
            UpdateFilteredFactions();
            UpdateFilteredKeywords();
            UpdateFilteredRaces();
            UpdateFilteredClasses();

            StatusMessage = $"Loaded: {AvailableNpcs.Count:N0} NPCs, {AvailableFactions.Count:N0} factions, {AvailableRaces.Count:N0} races, {AvailableClasses.Count:N0} classes, {AvailableKeywords.Count:N0} keywords.";
            _logger.Information("Game data loaded: {NpcCount} NPCs, {FactionCount} factions.",
                AvailableNpcs.Count, AvailableFactions.Count);
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

    private void SelectDistributionFilePath()
    {
        if (!IsCreatingNewFile)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*",
            DefaultExt = "ini",
            FileName = NewFileName
        };

        // Use output folder as base if set, otherwise fall back to data path
        var targetDirectory = !string.IsNullOrWhiteSpace(_settings.OutputPatchPath) && Directory.Exists(_settings.OutputPatchPath)
            ? _settings.OutputPatchPath
            : _settings.SkyrimDataPath;

        if (!string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory))
        {
            var defaultDir = PathUtilities.GetSkyPatcherNpcPath(targetDirectory);
            if (Directory.Exists(defaultDir))
            {
                dialog.InitialDirectory = defaultDir;
            }
            else
            {
                dialog.InitialDirectory = targetDirectory;
            }
        }

        if (dialog.ShowDialog() == true)
        {
            NewFileName = Path.GetFileName(dialog.FileName);
            DistributionFilePath = dialog.FileName;
            _logger.Debug("Selected distribution file path: {FilePath}", DistributionFilePath);
        }
    }

    /// <summary>
    /// Refreshes the file dropdown after a file is saved. Preserves current selection.
    /// For initial setup, use InitializeFromCache() instead.
    /// </summary>
    private void RefreshAvailableDistributionFiles()
    {
        var previousSelected = SelectedDistributionFile;
        var previousNewFileName = NewFileName;
        var justSaved = _justSavedFilePath;
        _justSavedFilePath = null; // Clear after reading

        var files = _cache.AllDistributionFiles.ToList();
        var duplicateFileNames = GetDuplicateFileNames(files);

        _logger.Debug("Refreshing distribution files dropdown. Previous selection: {Selection}, NewFileName: {NewFileName}, JustSaved: {JustSaved}",
            previousSelected?.DisplayName, previousNewFileName, justSaved);

        AvailableDistributionFiles.Clear();
        AvailableDistributionFiles.Add(new DistributionFileSelectionItem(isNewFile: true, file: null));
        foreach (var file in files)
        {
            var hasDuplicate = duplicateFileNames.Contains(file.FileName);
            AvailableDistributionFiles.Add(new DistributionFileSelectionItem(isNewFile: false, file: file, hasDuplicate));
        }

        // Update organized dropdown structure
        var dropdownStructure = DistributionDropdownOrganizer.Organize(files);
        DropdownItems = dropdownStructure.Items;
        this.RaisePropertyChanged(nameof(SelectedDropdownItem));

        // If we just saved a file, select it instead of "Create New File"
        if (!string.IsNullOrEmpty(justSaved))
        {
            var savedFileItem = AvailableDistributionFiles.FirstOrDefault(item =>
                !item.IsNewFile && item.File != null &&
                string.Equals(item.File.FullPath, justSaved, StringComparison.OrdinalIgnoreCase));

            if (savedFileItem != null)
            {
                _logger.Debug("Selecting just-saved file: {Path}", justSaved);
                IsCreatingNewFile = false;
                NewFileName = string.Empty;
                SelectedDistributionFile = savedFileItem;
                return;
            }

            _logger.Warning("Just-saved file not found in cache: {Path}", justSaved);
        }

        if (previousSelected != null)
        {
            if (previousSelected.IsNewFile)
            {
                SelectedDistributionFile = AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
                return;
            }

            if (previousSelected.File != null)
            {
                var matchingItem = AvailableDistributionFiles.FirstOrDefault(item =>
                    !item.IsNewFile && item.File?.FullPath == previousSelected.File.FullPath);
                if (matchingItem != null)
                {
                    SelectedDistributionFile = matchingItem;
                    return;
                }
            }
        }

        // Fall back to last saved file from settings
        var lastFilePath = _guiSettings.LastDistributionFilePath;
        if (!string.IsNullOrEmpty(lastFilePath))
        {
            var lastFileItem = AvailableDistributionFiles.FirstOrDefault(item =>
                !item.IsNewFile && item.File != null &&
                string.Equals(item.File.FullPath, lastFilePath, StringComparison.OrdinalIgnoreCase));

            if (lastFileItem != null)
            {
                _logger.Debug("Selecting last saved file from settings: {Path}", lastFilePath);
                SelectedDistributionFile = lastFileItem;
                return;
            }
        }

        // Default to "Create New File"
        SelectedDistributionFile = AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
    }

    private void UpdateDistributionFilePathForFormat()
    {
        if (IsCreatingNewFile)
        {
            UpdateDistributionFilePathFromNewFileName();
        }
        else if (!string.IsNullOrWhiteSpace(DistributionFilePath))
        {
            UpdateDistributionFilePathFromExistingFile();
        }
    }

    private void UpdateDistributionFilePathFromExistingFile()
    {
        var dataPath = _settings.SkyrimDataPath;
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            return;

        var currentFileName = Path.GetFileNameWithoutExtension(DistributionFilePath);
        if (string.IsNullOrWhiteSpace(currentFileName))
            return;

        // Strip any existing format-specific suffixes
        var baseName = currentFileName;
        if (baseName.EndsWith("_DISTR", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^6];
        }

        // Use output folder as base if set, otherwise fall back to data path
        var baseDirectory = !string.IsNullOrWhiteSpace(_settings.OutputPatchPath)
            ? _settings.OutputPatchPath
            : dataPath;

        DistributionFilePath = GetDistributionFilePath(baseDirectory, baseName, DistributionFormat);
        _logger.Debug("Updated distribution file path for format {Format}: {Path}", DistributionFormat, DistributionFilePath);
    }

    private void UpdateDistributionFilePathFromNewFileName()
    {
        var targetDirectory = !string.IsNullOrWhiteSpace(_settings.OutputPatchPath) && Directory.Exists(_settings.OutputPatchPath)
            ? _settings.OutputPatchPath
            : _settings.SkyrimDataPath;

        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            return;

        if (string.IsNullOrWhiteSpace(NewFileName))
        {
            DistributionFilePath = string.Empty;
            return;
        }

        var fileName = NewFileName.Trim();
        var baseName = fileName;
        if (baseName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^4];
        }
        if (baseName.EndsWith("_DISTR", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^6];
        }

        DistributionFilePath = GetDistributionFilePath(targetDirectory, baseName, DistributionFormat);
    }

    private static string GetDistributionFilePath(string baseDirectory, string baseName, DistributionFileType format)
    {
        if (format == DistributionFileType.Spid)
        {
            // SPID files go in base directory with *_DISTR.ini naming convention
            return Path.Combine(baseDirectory, $"{baseName}_DISTR.ini");
        }

        // SkyPatcher files go in skse/plugins/SkyPatcher/npc/ relative to base directory
        var skyPatcherPath = PathUtilities.GetSkyPatcherNpcPath(baseDirectory);
        return Path.Combine(skyPatcherPath, $"{baseName}.ini");
    }

    private void UpdateFileContent()
    {
        try
        {
            var anyEntryUsesChance = DistributionEntries.Any(e => e.UseChance);
            var effectiveFormat = anyEntryUsesChance ? DistributionFileType.Spid : DistributionFormat;

            _logger.Debug("UpdateFileContent: {EntryCount} entries, effectiveFormat={Format}, anyEntryUsesChance={UsesChance}",
                DistributionEntries.Count, effectiveFormat, anyEntryUsesChance);

            DistributionFileContent = DistributionFileFormatter.GenerateFileContent(DistributionEntries, effectiveFormat, ParseErrors);

            _logger.Debug("UpdateFileContent: Generated {LineCount} lines", DistributionFileContent.Split('\n').Length);
            DetectConflicts();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error updating distribution file content");
            DistributionFileContent = $"; Error generating file content: {ex.Message}";
        }
    }

    private async Task LoadAvailableOutfitsAsync()
    {
        if (_outfitsLoaded && AvailableOutfits.Count > 0)
            return;

        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
            _outfitsLoaded = true;
            _logger.Debug("Loaded {Count} available outfits.", outfits.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load available outfits.");
            AvailableOutfits.Clear();
        }
    }

    /// <summary>
    /// Merges outfits from the patch file into the provided list if the patch exists
    /// but isn't in the active load order (not enabled in plugins.txt).
    /// </summary>
    private async Task MergeOutfitsFromPatchFileAsync(List<IOutfitGetter> outfits)
    {
        var patchPath = _settings.FullOutputPath;
        if (string.IsNullOrEmpty(patchPath) || !File.Exists(patchPath))
            return;

        var patchOutfits = await _mutagenService.LoadOutfitsFromPluginAsync(Path.GetFileName(patchPath));
        var existingFormKeys = outfits.Select(o => o.FormKey).ToHashSet();

        var newOutfits = patchOutfits.Where(o => !existingFormKeys.Contains(o.FormKey)).ToList();
        if (newOutfits.Count > 0)
        {
            outfits.AddRange(newOutfits);
            _logger.Information("Added {Count} outfit(s) from patch file {Patch} (not in active load order).",
                newOutfits.Count, Path.GetFileName(patchPath));
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

    private bool _isInitialized;

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
    /// Single initialization point after cache is fully loaded (first time only).
    /// Linear flow: 1) populate filters, 2) populate file dropdown, 3) select <New File> with unique name.
    /// </summary>
    private void InitializeFromCache()
    {
        _logger.Debug("InitializeFromCache: Starting linear initialization...");
        UpdateFilteredNpcs();
        UpdateFilteredFactions();
        UpdateFilteredKeywords();
        UpdateFilteredRaces();
        UpdateFilteredClasses();
        var files = _cache.AllDistributionFiles.ToList();
        var duplicateFileNames = GetDuplicateFileNames(files);
        AvailableDistributionFiles.Clear();
        AvailableDistributionFiles.Add(new DistributionFileSelectionItem(isNewFile: true, file: null));
        foreach (var file in files)
        {
            var hasDuplicate = duplicateFileNames.Contains(file.FileName);
            AvailableDistributionFiles.Add(new DistributionFileSelectionItem(isNewFile: false, file: file, hasDuplicate));
        }

        // Update organized dropdown structure
        var dropdownStructure = DistributionDropdownOrganizer.Organize(files);
        DropdownItems = dropdownStructure.Items;

        // Try to restore last saved file from settings
        var lastFilePath = _guiSettings.LastDistributionFilePath;
        if (!string.IsNullOrEmpty(lastFilePath))
        {
            var lastFileItem = AvailableDistributionFiles.FirstOrDefault(item =>
                !item.IsNewFile && item.File != null &&
                string.Equals(item.File.FullPath, lastFilePath, StringComparison.OrdinalIgnoreCase));

            if (lastFileItem != null)
            {
                _logger.Debug("Restoring last saved file from settings: {Path}", lastFilePath);
                DistributionFilePath = lastFileItem.File!.FullPath;
                SelectedDistributionFile = lastFileItem;
                this.RaisePropertyChanged(nameof(SelectedDropdownItem));
                _isInitialized = true;
                this.RaisePropertyChanged(nameof(IsInitialized));
                _logger.Information("Initialization complete: {NpcCount} NPCs, {FactionCount} factions, {KeywordCount} keywords, {RaceCount} races, {FileCount} files. Restored file: {FilePath}",
                    FilteredNpcs.Count, FilteredFactions.Count, FilteredKeywords.Count, FilteredRaces.Count, files.Count, lastFilePath);
                _ = LoadDistributionFileAsync();
                return;
            }
            _logger.Debug("Last saved file not found in available files: {Path}", lastFilePath);
        }

        // Fall back to <New File>
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
        _logger.Information("Initialization complete: {NpcCount} NPCs, {FactionCount} factions, {KeywordCount} keywords, {RaceCount} races, {FileCount} files. NewFileName={NewFileName}",
            FilteredNpcs.Count, FilteredFactions.Count, FilteredKeywords.Count, FilteredRaces.Count, files.Count, NewFileName);
    }
    private string GenerateUniqueNewFileName()
    {
        const string baseName = "Boutique_Distribution";
        var existingFileNames = AvailableDistributionFiles
            .Where(f => !f.IsNewFile && f.File != null)
            .Select(f => f.File!.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (IsBaseNameAvailable(baseName, existingFileNames))
            return $"{baseName}.ini";

        for (var i = 2; i < 1000; i++)
        {
            var numberedBase = $"{baseName}_{i}";
            if (IsBaseNameAvailable(numberedBase, existingFileNames))
                return $"{numberedBase}.ini";
        }

        return $"{baseName}_{Guid.NewGuid():N}.ini";
    }

    private static bool IsBaseNameAvailable(string baseName, HashSet<string> existingFileNames) =>
        !existingFileNames.Contains($"{baseName}.ini") &&
        !existingFileNames.Contains($"{baseName}_DISTR.ini");

    private static HashSet<string> GetDuplicateFileNames(IEnumerable<DistributionFileViewModel> files) =>
        files
            .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task PreviewEntryAsync(DistributionEntryViewModel? entry)
    {
        if (entry == null || entry.SelectedOutfit == null)
        {
            StatusMessage = "No outfit selected for preview.";
            return;
        }

        if (!_mutagenService.IsInitialized || _mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            StatusMessage = "Initialize Skyrim data path before previewing outfits.";
            return;
        }

        var outfit = entry.SelectedOutfit;
        var label = outfit.EditorID ?? outfit.FormKey.ToString();

        var armorPieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
        if (armorPieces.Count == 0)
        {
            StatusMessage = $"Outfit '{label}' has no armor pieces to preview.";
            return;
        }

        try
        {
            StatusMessage = $"Building preview for {label}...";
            var scene = await _armorPreviewService.BuildPreviewAsync(armorPieces, GenderedModelVariant.Female);
            await ShowPreview.Handle(scene);
            StatusMessage = $"Preview ready for {label}.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to preview outfit {Identifier}", label);
            StatusMessage = $"Failed to preview outfit: {ex.Message}";
        }
    }

    /// <summary>
    /// Detects conflicts between the current distribution entries and existing distribution files.
    /// Updates HasConflicts, ConflictSummary, and NPC conflict indicators.
    /// </summary>
    private void DetectConflicts()
    {
        if (!IsCreatingNewFile)
        {
            HasConflicts = false;
            ConflictsResolvedByFilename = false;
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ClearNpcConflictIndicators();
            return;
        }
        if (DistributionEntries.Count == 0)
        {
            HasConflicts = false;
            ConflictsResolvedByFilename = false;
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ClearNpcConflictIndicators();
            return;
        }

        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            HasConflicts = false;
            ConflictsResolvedByFilename = false;
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ClearNpcConflictIndicators();
            return;
        }
        if (DistributionFiles.Count == 0)
        {
            HasConflicts = false;
            ConflictsResolvedByFilename = false;
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ClearNpcConflictIndicators();
            return;
        }
        var entryCountAtStart = DistributionEntries.Count;
        var entriesSnapshot = DistributionEntries.ToList();
        Task.Run(() =>
        {
            try
            {
                var result = DistributionConflictDetectionService.DetectConflicts(
                    entriesSnapshot,
                    DistributionFiles.ToList(),
                    NewFileName,
                    linkCache);
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (DistributionEntries.Count == entryCountAtStart && DistributionEntries.Count > 0)
                    {
                        HasConflicts = result.HasConflicts;
                        ConflictsResolvedByFilename = result.ConflictsResolvedByFilename;
                        ConflictSummary = result.ConflictSummary;
                        SuggestedFileName = result.SuggestedFileName;
                        var conflictNpcFormKeys = result.Conflicts
                            .Select(c => c.NpcFormKey)
                            .ToHashSet();

                        foreach (var entry in DistributionEntries)
                        {
                            foreach (var npcVm in entry.SelectedNpcs)
                            {
                                if (conflictNpcFormKeys.Contains(npcVm.FormKey))
                                {
                                    var conflict = result.Conflicts.First(c => c.NpcFormKey == npcVm.FormKey);
                                    npcVm.HasConflict = !result.ConflictsResolvedByFilename;
                                    npcVm.ConflictingFileName = conflict.ExistingFileName;
                                }
                                else
                                {
                                    npcVm.HasConflict = false;
                                    npcVm.ConflictingFileName = null;
                                }
                            }
                        }
                    }
                    else
                    {
                        _logger.Debug("Conflict detection completed but entries were cleared/changed, clearing conflict state");
                        HasConflicts = false;
                        ConflictsResolvedByFilename = false;
                        ConflictSummary = string.Empty;
                        SuggestedFileName = NewFileName;
                        ClearNpcConflictIndicators();
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during conflict detection");
            }
        });
    }
    private void ClearNpcConflictIndicators()
    {
        foreach (var entry in DistributionEntries)
        {
            foreach (var npc in entry.SelectedNpcs)
            {
                npc.HasConflict = false;
                npc.ConflictingFileName = null;
            }
        }
    }

    /// <summary>
    /// Called when a user enables chance-based distribution.
    /// Returns true if format is currently SkyPatcher (will be changed to SPID), false if already SPID.
    /// </summary>
    private bool IsFormatChangingToSpid()
    {
        if (DistributionFormat == DistributionFileType.SkyPatcher)
        {
            DistributionFormat = DistributionFileType.Spid;
            UpdateFileContent();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Creates a DistributionEntryViewModel from a DistributionEntry,
    /// resolving outfit and NPC references for proper UI binding.
    /// </summary>
    private DistributionEntryViewModel CreateEntryViewModel(DistributionEntry entry)
    {
        var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry, IsFormatChangingToSpid);
        ResolveEntryOutfit(entryVm);
        var npcVms = ResolveNpcFormKeys(entry.NpcFormKeys);
        if (npcVms.Count > 0)
        {
            entryVm.SelectedNpcs = new ObservableCollection<NpcRecordViewModel>(npcVms);
            entryVm.UpdateEntryNpcs();
        }
        var factionVms = ResolveFactionFormKeys(entry.FactionFormKeys);
        if (factionVms.Count > 0)
        {
            entryVm.SelectedFactions = new ObservableCollection<FactionRecordViewModel>(factionVms);
            entryVm.UpdateEntryFactions();
        }
        var keywordVms = ResolveKeywordEditorIds(entry.KeywordEditorIds);
        if (keywordVms.Count > 0)
        {
            entryVm.SelectedKeywords = new ObservableCollection<KeywordRecordViewModel>(keywordVms);
            entryVm.UpdateEntryKeywords();
        }
        var raceVms = ResolveRaceFormKeys(entry.RaceFormKeys);
        if (raceVms.Count > 0)
        {
            entryVm.SelectedRaces = new ObservableCollection<RaceRecordViewModel>(raceVms);
            entryVm.UpdateEntryRaces();
        }
        var classVms = ResolveClassFormKeys(entry.ClassFormKeys);
        if (classVms.Count > 0)
        {
            entryVm.SelectedClasses = new ObservableCollection<ClassRecordViewModel>(classVms);
            entryVm.UpdateEntryClasses();
        }

        return entryVm;
    }

    /// <summary>
    /// Resolves the entry's outfit to an instance from AvailableOutfits
    /// so the ComboBox can properly display and select it.
    /// </summary>
    private void ResolveEntryOutfit(DistributionEntryViewModel entryVm)
    {
        if (entryVm.SelectedOutfit == null)
            return;

        var outfitFormKey = entryVm.SelectedOutfit.FormKey;
        var matchingOutfit = AvailableOutfits.FirstOrDefault(o => o.FormKey == outfitFormKey);

        if (matchingOutfit != null)
        {
            entryVm.SelectedOutfit = matchingOutfit;
        }
    }

    /// <summary>
    /// Resolves a list of NPC FormKeys to NpcRecordViewModels,
    /// preferring existing instances from AvailableNpcs.
    /// </summary>
    private List<NpcRecordViewModel> ResolveNpcFormKeys(IEnumerable<FormKey> formKeys)
    {
        var npcVms = new List<NpcRecordViewModel>();

        foreach (var npcFormKey in formKeys)
        {
            var npcVm = ResolveNpcFormKey(npcFormKey);
            if (npcVm != null)
            {
                npcVms.Add(npcVm);
            }
        }

        return npcVms;
    }

    /// <summary>
    /// Resolves a single NPC FormKey to an NpcRecordViewModel,
    /// preferring an existing instance from AvailableNpcs.
    /// </summary>
    private NpcRecordViewModel? ResolveNpcFormKey(FormKey formKey)
    {
        var existingNpc = AvailableNpcs.FirstOrDefault(npc => npc.FormKey == formKey);
        if (existingNpc != null)
        {
            return existingNpc;
        }
        if (_mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
            linkCache.TryResolve<INpcGetter>(formKey, out var npc))
        {
            var npcRecord = new NpcRecord(
                npc.FormKey,
                npc.EditorID,
                npc.Name?.String,
                npc.FormKey.ModKey);
            return new NpcRecordViewModel(npcRecord);
        }

        return null;
    }

    private List<FactionRecordViewModel> ResolveFactionFormKeys(IEnumerable<FormKey> formKeys)
    {
        var factionVms = new List<FactionRecordViewModel>();

        foreach (var formKey in formKeys)
        {
            var factionVm = ResolveFactionFormKey(formKey);
            if (factionVm != null)
            {
                factionVms.Add(factionVm);
            }
        }

        return factionVms;
    }

    private FactionRecordViewModel? ResolveFactionFormKey(FormKey formKey)
    {
        var existingFaction = AvailableFactions.FirstOrDefault(f => f.FormKey == formKey);
        if (existingFaction != null)
        {
            return existingFaction;
        }

        if (_mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
            linkCache.TryResolve<IFactionGetter>(formKey, out var faction))
        {
            var factionRecord = new FactionRecord(
                faction.FormKey,
                faction.EditorID,
                faction.Name?.String,
                faction.FormKey.ModKey);
            return new FactionRecordViewModel(factionRecord);
        }

        return null;
    }

    private List<KeywordRecordViewModel> ResolveKeywordEditorIds(IEnumerable<string> editorIds)
    {
        var keywordVms = new List<KeywordRecordViewModel>();

        foreach (var editorId in editorIds)
        {
            var keywordVm = ResolveKeywordEditorId(editorId);
            if (keywordVm != null)
            {
                keywordVms.Add(keywordVm);
            }
        }

        return keywordVms;
    }

    private KeywordRecordViewModel? ResolveKeywordEditorId(string editorId)
    {
        if (string.IsNullOrWhiteSpace(editorId))
            return null;

        // Check if it's already in AvailableKeywords
        var existingKeyword = AvailableKeywords.FirstOrDefault(k =>
            string.Equals(k.KeywordRecord.EditorID, editorId, StringComparison.OrdinalIgnoreCase));
        if (existingKeyword != null)
        {
            return existingKeyword;
        }

        // Try to resolve from LinkCache
        if (_mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            var keyword = linkCache.WinningOverrides<IKeywordGetter>()
                .FirstOrDefault(k => string.Equals(k.EditorID, editorId, StringComparison.OrdinalIgnoreCase));
            if (keyword != null)
            {
                var keywordRecord = new KeywordRecord(
                    keyword.FormKey,
                    keyword.EditorID,
                    keyword.FormKey.ModKey);
                return new KeywordRecordViewModel(keywordRecord);
            }
        }

        // Create a virtual keyword record (for SPID-distributed keywords)
        var virtualRecord = new KeywordRecord(FormKey.Null, editorId, ModKey.Null);
        return new KeywordRecordViewModel(virtualRecord);
    }

    private KeywordRecordViewModel? ResolveKeywordByFormKey(FormKey formKey)
    {
        var existingKeyword = AvailableKeywords.FirstOrDefault(k => k.FormKey == formKey);
        if (existingKeyword != null)
        {
            return existingKeyword;
        }

        if (_mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
            linkCache.TryResolve<IKeywordGetter>(formKey, out var keyword))
        {
            var keywordRecord = new KeywordRecord(
                keyword.FormKey,
                keyword.EditorID,
                keyword.FormKey.ModKey);
            return new KeywordRecordViewModel(keywordRecord);
        }

        return null;
    }

    private List<RaceRecordViewModel> ResolveRaceFormKeys(IEnumerable<FormKey> formKeys)
    {
        var raceVms = new List<RaceRecordViewModel>();

        foreach (var formKey in formKeys)
        {
            var raceVm = ResolveRaceFormKey(formKey);
            if (raceVm != null)
            {
                raceVms.Add(raceVm);
            }
        }

        return raceVms;
    }

    private RaceRecordViewModel? ResolveRaceFormKey(FormKey formKey)
    {
        var existingRace = AvailableRaces.FirstOrDefault(r => r.FormKey == formKey);
        if (existingRace != null)
        {
            return existingRace;
        }

        if (_mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
            linkCache.TryResolve<IRaceGetter>(formKey, out var race))
        {
            var raceRecord = new RaceRecord(
                race.FormKey,
                race.EditorID,
                race.Name?.String,
                race.FormKey.ModKey);
            return new RaceRecordViewModel(raceRecord);
        }

        return null;
    }

    private List<ClassRecordViewModel> ResolveClassFormKeys(IEnumerable<FormKey> formKeys)
    {
        var classVms = new List<ClassRecordViewModel>();

        foreach (var formKey in formKeys)
        {
            var classVm = ResolveClassFormKey(formKey);
            if (classVm != null)
            {
                classVms.Add(classVm);
            }
        }

        return classVms;
    }

    private ClassRecordViewModel? ResolveClassFormKey(FormKey formKey)
    {
        var existingClass = AvailableClasses.FirstOrDefault(c => c.FormKey == formKey);
        if (existingClass != null)
        {
            return existingClass;
        }

        if (_mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
            linkCache.TryResolve<IClassGetter>(formKey, out var classRecord))
        {
            var record = new ClassRecord(
                classRecord.FormKey,
                classRecord.EditorID,
                classRecord.Name?.String,
                classRecord.FormKey.ModKey);
            return new ClassRecordViewModel(record);
        }

        return null;
    }

    /// <summary>
    /// Updates the FilteredNpcs collection based on the current search text.
    /// This uses a stable collection to avoid DataGrid binding issues with checkboxes.
    /// </summary>
    private void UpdateFilteredNpcs()
    {
        IEnumerable<NpcRecordViewModel> filtered;

        if (string.IsNullOrWhiteSpace(NpcSearchText))
        {
            filtered = AvailableNpcs;
        }
        else
        {
            var term = NpcSearchText.Trim().ToLowerInvariant();
            filtered = AvailableNpcs.Where(npc => npc.MatchesSearch(term));
        }
        FilteredNpcs.Clear();
        foreach (var npc in filtered)
        {
            FilteredNpcs.Add(npc);
        }
    }

    private void UpdateFilteredFactions()
    {
        IEnumerable<FactionRecordViewModel> filtered;

        if (string.IsNullOrWhiteSpace(FactionSearchText))
        {
            filtered = AvailableFactions;
        }
        else
        {
            var term = FactionSearchText.Trim().ToLowerInvariant();
            filtered = AvailableFactions.Where(faction => faction.MatchesSearch(term));
        }

        FilteredFactions.Clear();
        foreach (var faction in filtered)
        {
            FilteredFactions.Add(faction);
        }
    }

    private void UpdateFilteredKeywords()
    {
        IEnumerable<KeywordRecordViewModel> filtered;

        if (string.IsNullOrWhiteSpace(KeywordSearchText))
        {
            filtered = AvailableKeywords;
        }
        else
        {
            var term = KeywordSearchText.Trim().ToLowerInvariant();
            filtered = AvailableKeywords.Where(keyword => keyword.MatchesSearch(term));
        }

        FilteredKeywords.Clear();
        foreach (var keyword in filtered)
        {
            FilteredKeywords.Add(keyword);
        }
    }

    private void UpdateFilteredRaces()
    {
        IEnumerable<RaceRecordViewModel> filtered;

        if (string.IsNullOrWhiteSpace(RaceSearchText))
        {
            filtered = AvailableRaces;
        }
        else
        {
            var term = RaceSearchText.Trim().ToLowerInvariant();
            filtered = AvailableRaces.Where(race => race.MatchesSearch(term));
        }

        FilteredRaces.Clear();
        foreach (var race in filtered)
        {
            FilteredRaces.Add(race);
        }
    }

    private void UpdateFilteredClasses()
    {
        IEnumerable<ClassRecordViewModel> filtered;

        if (string.IsNullOrWhiteSpace(ClassSearchText))
        {
            filtered = AvailableClasses;
        }
        else
        {
            var term = ClassSearchText.Trim().ToLowerInvariant();
            filtered = AvailableClasses.Where(c => c.MatchesSearch(term));
        }

        FilteredClasses.Clear();
        foreach (var classVm in filtered)
        {
            FilteredClasses.Add(classVm);
        }
    }
}
