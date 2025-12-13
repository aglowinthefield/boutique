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
    private readonly ILogger _logger;

    private ObservableCollection<DistributionEntryViewModel> _distributionEntries = [];
    private bool _isBulkLoading;
    private bool _outfitsLoaded;

    public DistributionEditTabViewModel(
        DistributionFileWriterService fileWriterService,
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
        GameDataCacheService cache,
        SettingsViewModel settings,
        ILogger logger)
    {
        _fileWriterService = fileWriterService;
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _cache = cache;
        _settings = settings;
        _logger = logger.ForContext<DistributionEditTabViewModel>();

        // Subscribe to plugin changes so we refresh the available outfits list
        _mutagenService.PluginsChanged += OnPluginsChanged;

        // Subscribe to collection changes to update computed count property
        _distributionEntries.CollectionChanged += OnDistributionEntriesChanged;

        AddDistributionEntryCommand = ReactiveCommand.Create(AddDistributionEntry);
        RemoveDistributionEntryCommand = ReactiveCommand.Create<DistributionEntryViewModel>(RemoveDistributionEntry);
        SelectEntryCommand = ReactiveCommand.Create<DistributionEntryViewModel>(SelectEntry);

        // Simple canExecute observables for commands
        var hasEntries = this.WhenAnyValue(vm => vm.DistributionEntriesCount, count => count > 0);
        AddSelectedNpcsToEntryCommand = ReactiveCommand.Create(AddSelectedNpcsToEntry, hasEntries);
        AddSelectedFactionsToEntryCommand = ReactiveCommand.Create(AddSelectedFactionsToEntry, hasEntries);
        AddSelectedKeywordsToEntryCommand = ReactiveCommand.Create(AddSelectedKeywordsToEntry, hasEntries);
        AddSelectedRacesToEntryCommand = ReactiveCommand.Create(AddSelectedRacesToEntry, hasEntries);

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
        LoadDistributionFileCommand = ReactiveCommand.CreateFromTask(LoadDistributionFileAsync, notLoading);
        ScanNpcsCommand = ReactiveCommand.CreateFromTask(ScanNpcsAsync, notLoading);
        SelectDistributionFilePathCommand = ReactiveCommand.Create(SelectDistributionFilePath);
        PreviewEntryCommand = ReactiveCommand.CreateFromTask<DistributionEntryViewModel>(PreviewEntryAsync, notLoading);

        this.WhenAnyValue(vm => vm.NpcSearchText)
            .Subscribe(_ => UpdateFilteredNpcs());
        this.WhenAnyValue(vm => vm.FactionSearchText)
            .Subscribe(_ => UpdateFilteredFactions());
        this.WhenAnyValue(vm => vm.KeywordSearchText)
            .Subscribe(_ => UpdateFilteredKeywords());
        this.WhenAnyValue(vm => vm.RaceSearchText)
            .Subscribe(_ => UpdateFilteredRaces());

        // Watch for format changes to update preview
        this.WhenAnyValue(vm => vm.DistributionFormat)
            .Skip(1) // Skip initial value
            .Subscribe(_ => UpdateDistributionPreview());

        // When creating a new file, reset format to SkyPatcher
        this.WhenAnyValue(vm => vm.IsCreatingNewFile)
            .Where(isNew => isNew)
            .Subscribe(_ => DistributionFormat = DistributionFileType.SkyPatcher);
    }

    [Reactive] public bool IsLoading { get; private set; }

    [Reactive] public string StatusMessage { get; private set; } = string.Empty;

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
            // Clear previous selection
            field?.IsSelected = false;

            this.RaiseAndSetIfChanged(ref field, value);

            // Set new selection
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

    [Reactive] public ObservableCollection<IOutfitGetter> AvailableOutfits { get; private set; } = [];

    [Reactive] public ObservableCollection<DistributionFileSelectionItem> AvailableDistributionFiles { get; private set; } = [];

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
                    NewFileName = string.Empty; // Clear new filename when selecting existing file

                    // Automatically load the file when selected
                    if (File.Exists(DistributionFilePath) && !IsLoading)
                    {
                        _logger.Debug("Auto-loading file when selected: {Path}", DistributionFilePath);
                        _ = LoadDistributionFileAsync();
                    }
                    else
                    {
                        _logger.Debug("Not auto-loading file - Exists: {Exists}, IsLoading: {IsLoading}",
                            File.Exists(DistributionFilePath), IsLoading);
                    }
                }
                else if (value.IsNewFile)
                {
                    // Clear entries when switching to New File mode (don't copy from previous selection)
                    if (previous != null && !previous.IsNewFile)
                    {
                        _isBulkLoading = true;
                        try
                        {
                            DistributionEntries.Clear();
                            // Immediately clear conflict state when switching to new file
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
                        UpdateDistributionPreview();
                    }

                    // Set default filename if empty
                    if (string.IsNullOrWhiteSpace(NewFileName))
                    {
                        NewFileName = "Distribution.ini";
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
                // Re-detect conflicts when filename changes
                DetectConflicts();
            }
        }
    }

    [Reactive] public string DistributionFilePath { get; private set; } = string.Empty;

    [Reactive] public string NpcSearchText { get; set; } = string.Empty;

    [Reactive] public string FactionSearchText { get; set; } = string.Empty;

    [Reactive] public string KeywordSearchText { get; set; } = string.Empty;

    [Reactive] public string RaceSearchText { get; set; } = string.Empty;

    [Reactive] public string DistributionPreviewText { get; private set; } = string.Empty;

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
            UpdateDistributionPreview();
        }
    }
    private DistributionFileType _distributionFormat = DistributionFileType.SkyPatcher;

    /// <summary>
    /// Available distribution file formats for the dropdown.
    /// </summary>
    public IReadOnlyList<DistributionFileType> AvailableFormats { get; } =
        new[] { DistributionFileType.Spid, DistributionFileType.SkyPatcher };

    [Reactive] public ObservableCollection<NpcRecordViewModel> FilteredNpcs { get; private set; } = [];

    [Reactive] public ObservableCollection<FactionRecordViewModel> FilteredFactions { get; private set; } = [];

    [Reactive] public ObservableCollection<KeywordRecordViewModel> FilteredKeywords { get; private set; } = [];

    [Reactive] public ObservableCollection<RaceRecordViewModel> FilteredRaces { get; private set; } = [];

    /// <summary>
    /// Indicates whether the current distribution entries have conflicts with existing files.
    /// </summary>
    [Reactive] public bool HasConflicts { get; private set; }

    /// <summary>
    /// Indicates whether conflicts exist but are resolved by the current filename ordering.
    /// </summary>
    [Reactive] public bool ConflictsResolvedByFilename { get; private set; }

    /// <summary>
    /// Summary text describing the detected conflicts.
    /// </summary>
    [Reactive] public string ConflictSummary { get; private set; } = string.Empty;

    /// <summary>
    /// The suggested filename with Z-prefix to ensure proper load order.
    /// </summary>
    [Reactive] public string SuggestedFileName { get; private set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> AddDistributionEntryCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> RemoveDistributionEntryCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> SelectEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedNpcsToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedFactionsToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedKeywordsToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedRacesToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveDistributionFileCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadDistributionFileCommand { get; }
    public ReactiveCommand<Unit, Unit> ScanNpcsCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectDistributionFilePathCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> PreviewEntryCommand { get; }

    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    public bool IsInitialized => _mutagenService.IsInitialized;

    /// <summary>
    /// Sets the distribution files from the Files tab. This is used for conflict detection
    /// and for populating the AvailableDistributionFiles dropdown.
    /// </summary>
    public void SetDistributionFiles(IReadOnlyList<DistributionFileViewModel> files)
    {
        UpdateAvailableDistributionFiles(files);
    }

    private IReadOnlyList<DistributionFileViewModel>? _distributionFiles;

    /// <summary>
    /// Internal method to store distribution files for conflict detection.
    /// </summary>
    internal void SetDistributionFilesInternal(IReadOnlyList<DistributionFileViewModel> files)
    {
        _distributionFiles = files;
    }

    private void OnDistributionEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Skip expensive operations during bulk loading
        if (_isBulkLoading)
            return;

        _logger.Debug("OnDistributionEntriesChanged: Action={Action}, NewItems={NewCount}, OldItems={OldCount}",
            e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);

        // Raise PropertyChanged synchronously - this is fast and necessary for bindings
        this.RaisePropertyChanged(nameof(DistributionEntriesCount));

        // Subscribe to property changes on new entries
        if (e.NewItems != null)
        {
            foreach (DistributionEntryViewModel entry in e.NewItems)
            {
                SubscribeToEntryChanges(entry);
            }
        }

        // Update preview whenever entries change
        UpdateDistributionPreview();

        _logger.Debug("OnDistributionEntriesChanged completed");
    }

    private void SubscribeToEntryChanges(DistributionEntryViewModel entry)
    {
        // Debounce preview updates to avoid excessive calls during rapid changes
        var previewUpdateSubject = new System.Reactive.Subjects.Subject<Unit>();
        previewUpdateSubject
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateDistributionPreview());

        entry.WhenAnyValue(evm => evm.SelectedOutfit)
            .Skip(1) // Skip initial value
            .Subscribe(_ => previewUpdateSubject.OnNext(Unit.Default));
        entry.WhenAnyValue(evm => evm.SelectedNpcs)
            .Skip(1) // Skip initial value
            .Subscribe(_ => previewUpdateSubject.OnNext(Unit.Default));
        entry.SelectedNpcs.CollectionChanged += (s, args) => previewUpdateSubject.OnNext(Unit.Default);
        entry.SelectedFactions.CollectionChanged += (s, args) => previewUpdateSubject.OnNext(Unit.Default);
        entry.SelectedKeywords.CollectionChanged += (s, args) => previewUpdateSubject.OnNext(Unit.Default);
        entry.SelectedRaces.CollectionChanged += (s, args) => previewUpdateSubject.OnNext(Unit.Default);
        entry.WhenAnyValue(evm => evm.UseChance, evm => evm.Chance)
            .Skip(1) // Skip initial value
            .Subscribe(_ => previewUpdateSubject.OnNext(Unit.Default));
    }

    private void AddDistributionEntry()
    {
        _logger.Debug("AddDistributionEntry called");
        try
        {
            _logger.Debug("Creating DistributionEntry");
            var entry = new DistributionEntry();

            _logger.Debug("Creating DistributionEntryViewModel");
            var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry, OnUseChanceEnabling);

            _logger.Debug("Adding to DistributionEntries collection");
            DistributionEntries.Add(entryVm);

            _logger.Debug("Deferring SelectedEntry assignment");
            // Defer SelectedEntry assignment to avoid blocking UI thread
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

    private void AddSelectedNpcsToEntry()
    {
        if (SelectedEntry == null)
        {
            // If no entry is selected, use the first one or create a new one
            SelectedEntry = DistributionEntries.FirstOrDefault();
            if (SelectedEntry == null)
            {
                AddDistributionEntry();
                return;
            }
        }

        // Get all NPCs that are checked - check FilteredNpcs first since that's what the user sees
        // The instances are the same, but we want to check what's actually visible in the DataGrid
        var selectedNpcs = FilteredNpcs
            .Where(npc => npc.IsSelected)
            .ToList();

        _logger.Debug("AddSelectedNpcsToEntry: Total NPCs={Total}, Filtered={Filtered}, Selected={Selected}",
            AvailableNpcs.Count,
            FilteredNpcs.Count,
            selectedNpcs.Count);

        if (selectedNpcs.Count == 0)
        {
            StatusMessage = "No NPCs selected. Check the boxes next to NPCs you want to add.";
            return;
        }

        var addedCount = 0;
        foreach (var npc in selectedNpcs)
        {
            // Check if NPC is already in this entry
            if (!SelectedEntry.SelectedNpcs.Any(existing => existing.FormKey == npc.FormKey))
            {
                SelectedEntry.AddNpc(npc);
                addedCount++;
            }
        }

        // Clear the selection state in the NPC picker after adding to entry
        // This prevents previously selected NPCs from being added to the next entry
        // The IsSelected property in the picker is only for selection, not for tracking entries
        foreach (var npc in selectedNpcs)
        {
            npc.IsSelected = false;
        }

        if (addedCount > 0)
        {
            StatusMessage = $"Added {addedCount} NPC(s) to entry: {SelectedEntry.SelectedOutfit?.EditorID ?? "(No outfit)"}";
            _logger.Debug("Added {Count} NPCs to entry", addedCount);
        }
        else
        {
            StatusMessage = "All selected NPCs are already in this entry.";
        }
    }

    private void AddSelectedFactionsToEntry()
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

        var selectedFactions = FilteredFactions
            .Where(faction => faction.IsSelected)
            .ToList();

        if (selectedFactions.Count == 0)
        {
            StatusMessage = "No factions selected. Check the boxes next to factions you want to add.";
            return;
        }

        var addedCount = 0;
        foreach (var faction in selectedFactions)
        {
            if (!SelectedEntry.SelectedFactions.Any(existing => existing.FormKey == faction.FormKey))
            {
                SelectedEntry.AddFaction(faction);
                addedCount++;
            }
        }

        foreach (var faction in selectedFactions)
        {
            faction.IsSelected = false;
        }

        if (addedCount > 0)
        {
            StatusMessage = $"Added {addedCount} faction(s) to entry: {SelectedEntry.SelectedOutfit?.EditorID ?? "(No outfit)"}";
            _logger.Debug("Added {Count} factions to entry", addedCount);
        }
        else
        {
            StatusMessage = "All selected factions are already in this entry.";
        }
    }

    private void AddSelectedKeywordsToEntry()
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

        var selectedKeywords = FilteredKeywords
            .Where(keyword => keyword.IsSelected)
            .ToList();

        if (selectedKeywords.Count == 0)
        {
            StatusMessage = "No keywords selected. Check the boxes next to keywords you want to add.";
            return;
        }

        var addedCount = 0;
        foreach (var keyword in selectedKeywords)
        {
            if (!SelectedEntry.SelectedKeywords.Any(existing => existing.FormKey == keyword.FormKey))
            {
                SelectedEntry.AddKeyword(keyword);
                addedCount++;
            }
        }

        foreach (var keyword in selectedKeywords)
        {
            keyword.IsSelected = false;
        }

        if (addedCount > 0)
        {
            StatusMessage = $"Added {addedCount} keyword(s) to entry: {SelectedEntry.SelectedOutfit?.EditorID ?? "(No outfit)"}";
            _logger.Debug("Added {Count} keywords to entry", addedCount);
        }
        else
        {
            StatusMessage = "All selected keywords are already in this entry.";
        }
    }

    private void AddSelectedRacesToEntry()
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

        var selectedRaces = FilteredRaces
            .Where(race => race.IsSelected)
            .ToList();

        if (selectedRaces.Count == 0)
        {
            StatusMessage = "No races selected. Check the boxes next to races you want to add.";
            return;
        }

        var addedCount = 0;
        foreach (var race in selectedRaces)
        {
            if (!SelectedEntry.SelectedRaces.Any(existing => existing.FormKey == race.FormKey))
            {
                SelectedEntry.AddRace(race);
                addedCount++;
            }
        }

        foreach (var race in selectedRaces)
        {
            race.IsSelected = false;
        }

        if (addedCount > 0)
        {
            StatusMessage = $"Added {addedCount} race(s) to entry: {SelectedEntry.SelectedOutfit?.EditorID ?? "(No outfit)"}";
            _logger.Debug("Added {Count} races to entry", addedCount);
        }
        else
        {
            StatusMessage = "All selected races are already in this entry.";
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

        // Detect conflicts before saving
        DetectConflicts();

        var finalFilePath = DistributionFilePath;
        var finalFileName = Path.GetFileName(DistributionFilePath);

        // If creating a new file with conflicts, show confirmation with suggested filename
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
                // Use the suggested filename
                var directory = Path.GetDirectoryName(DistributionFilePath);
                finalFileName = SuggestedFileName;
                if (!finalFileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                {
                    finalFileName += ".ini";
                }
                finalFilePath = !string.IsNullOrEmpty(directory)
                    ? Path.Combine(directory, finalFileName)
                    : finalFileName;

                // Update the NewFileName to reflect the change
                NewFileName = finalFileName;
            }
            // If No, continue with original filename
        }

        // Check if file exists and prompt for overwrite confirmation (before showing loading state)
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

            var entries = DistributionEntries
                .Select(evm => evm.Entry)
                .ToList();

            // Auto-detect format: if any entry uses chance, use SPID format
            var anyEntryUsesChance = entries.Any(e => e.Chance.HasValue);
            var effectiveFormat = anyEntryUsesChance ? DistributionFileType.Spid : DistributionFormat;

            await _fileWriterService.WriteDistributionFileAsync(finalFilePath, entries, effectiveFormat);

            StatusMessage = $"Successfully saved distribution file: {Path.GetFileName(finalFilePath)}";
            _logger.Information("Saved distribution file: {FilePath}", finalFilePath);

            // Notify parent that file was saved (for refreshing file list)
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

    /// <summary>
    /// Event raised when a file is saved, so the parent can refresh the file list.
    /// </summary>
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

            // Load file and detect format
            var (entries, detectedFormat) = await ((DistributionFileWriterService)_fileWriterService).LoadDistributionFileWithFormatAsync(
                DistributionFilePath);

            // Set the detected format
            DistributionFormat = detectedFormat;

            // Ensure outfits are loaded before creating entries so ComboBox bindings work
            await LoadAvailableOutfitsAsync();

            // Use bulk loading to avoid triggering expensive updates for each entry
            _isBulkLoading = true;
            try
            {
                DistributionEntries.Clear();
                // Clear conflict state when loading a file (will be recalculated if needed)
                HasConflicts = false;
                ConflictsResolvedByFilename = false;
                ConflictSummary = string.Empty;
                SuggestedFileName = string.Empty;
                ClearNpcConflictIndicators();

                // Create all ViewModels first (can be done on background thread for large lists)
                var entryVms = await Task.Run(() =>
                    entries.Select(entry => CreateEntryViewModel(entry)).ToList());

                // Add all entries to collection
                foreach (var entryVm in entryVms)
                {
                    DistributionEntries.Add(entryVm);
                }

                // Subscribe to changes on all entries
                foreach (var entryVm in entryVms)
                {
                    SubscribeToEntryChanges(entryVm);
                }
            }
            finally
            {
                _isBulkLoading = false;
            }

            // Now do a single update for count and preview
            this.RaisePropertyChanged(nameof(DistributionEntriesCount));
            UpdateDistributionPreview();

            StatusMessage = $"Loaded {entries.Count} distribution entries from {Path.GetFileName(DistributionFilePath)}";
            _logger.Information("Loaded distribution file: {FilePath} with {Count} entries", DistributionFilePath, entries.Count);
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

            // Initialize MutagenService if not already initialized
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

            // Wait for cache to be loaded if not already
            if (!_cache.IsLoaded && !_cache.IsLoading)
            {
                StatusMessage = "Loading game data cache...";
                await _cache.LoadAsync();
            }
            else if (_cache.IsLoading)
            {
                StatusMessage = "Waiting for game data cache...";
                while (_cache.IsLoading)
                    await Task.Delay(100);
            }

            // NPCs, factions, races, keywords are already available from cache
            // Just update the filtered list
            UpdateFilteredNpcs();

            StatusMessage = $"Data loaded: {AvailableNpcs.Count} NPCs, {AvailableFactions.Count} factions, {AvailableRaces.Count} races.";
            _logger.Information("Using cached data: {NpcCount} NPCs, {FactionCount} factions.",
                AvailableNpcs.Count, AvailableFactions.Count);

            // Load outfits when scanning (LinkCache is now available)
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
        // Only show browse dialog when creating a new file
        if (!IsCreatingNewFile)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*",
            DefaultExt = "ini",
            FileName = NewFileName
        };

        var dataPath = _settings.SkyrimDataPath;
        if (!string.IsNullOrWhiteSpace(dataPath) && Directory.Exists(dataPath))
        {
            var defaultDir = Path.Combine(dataPath, "skse", "plugins", "SkyPatcher", "npc");
            if (Directory.Exists(defaultDir))
            {
                dialog.InitialDirectory = defaultDir;
            }
            else
            {
                dialog.InitialDirectory = dataPath;
            }
        }

        if (dialog.ShowDialog() == true)
        {
            NewFileName = Path.GetFileName(dialog.FileName);
            DistributionFilePath = dialog.FileName;
            _logger.Debug("Selected distribution file path: {FilePath}", DistributionFilePath);
        }
    }

    private void UpdateAvailableDistributionFiles(IReadOnlyList<DistributionFileViewModel> files)
    {
        var previousSelected = SelectedDistributionFile;
        var previousFilePath = DistributionFilePath;

        // Update collection in place to maintain bindings
        AvailableDistributionFiles.Clear();

        // Add "New File" option
        AvailableDistributionFiles.Add(new DistributionFileSelectionItem(isNewFile: true, file: null));

        // Add existing files
        foreach (var file in files)
        {
            AvailableDistributionFiles.Add(new DistributionFileSelectionItem(isNewFile: false, file: file));
        }

        // Try to restore previous selection
        if (previousSelected != null)
        {
            if (previousSelected.IsNewFile)
            {
                // Restore "New File" selection
                var newFileItem = AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
                if (newFileItem != null)
                {
                    SelectedDistributionFile = newFileItem;
                    DistributionFilePath = previousFilePath;
                }
            }
            else if (previousSelected.File != null)
            {
                // Try to find the same file
                var matchingItem = AvailableDistributionFiles.FirstOrDefault(item =>
                    !item.IsNewFile && item.File?.FullPath == previousSelected.File.FullPath);
                if (matchingItem != null)
                {
                    SelectedDistributionFile = matchingItem;
                }
            }
        }
    }

    private void UpdateDistributionFilePathFromNewFileName()
    {
        var dataPath = _settings.SkyrimDataPath;
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            return;

        if (string.IsNullOrWhiteSpace(NewFileName))
        {
            DistributionFilePath = string.Empty;
            return;
        }

        // Ensure .ini extension
        var fileName = NewFileName.Trim();
        if (!fileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".ini";
        }

        // Default to SkyPatcher directory
        var defaultPath = Path.Combine(dataPath, "skse", "plugins", "SkyPatcher", "npc", fileName);
        DistributionFilePath = defaultPath;
    }

    private void UpdateDistributionPreview()
    {
        try
        {
            var lines = new List<string>
            {
                // Add header comment
                "; Distribution File",
                "; Generated by Boutique",
                ""
            };

            if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
            {
                DistributionPreviewText = string.Join(Environment.NewLine, lines) + Environment.NewLine + "; LinkCache not available";
                return;
            }

            // Auto-detect format: if any entry uses chance, use SPID format
            var anyEntryUsesChance = DistributionEntries.Any(e => e.UseChance);
            var effectiveFormat = anyEntryUsesChance ? DistributionFileType.Spid : DistributionFormat;

            _logger.Debug("UpdateDistributionPreview: {EntryCount} entries, effectiveFormat={Format}, anyEntryUsesChance={UsesChance}",
                DistributionEntries.Count, effectiveFormat, anyEntryUsesChance);

            foreach (var entryVm in DistributionEntries)
            {
                if (entryVm.SelectedOutfit == null)
                    continue;

                if (effectiveFormat == DistributionFileType.Spid)
                {
                    // SPID format: Outfit = FormOrEditorID|StringFilters|FormFilters|LevelFilters|TraitFilters|CountOrPackageIdx|Chance
                    var outfitIdentifier = FormKeyHelper.FormatForSpid(entryVm.SelectedOutfit.FormKey);

                    // StringFilters (position 2): NPC names (comma-separated for OR) and Keywords (with + for AND)
                    var stringFilters = new List<string>();

                    // Add NPC names (comma-separated for OR logic)
                    var npcNames = entryVm.SelectedNpcs
                        .Where(npc => !string.IsNullOrWhiteSpace(npc.DisplayName))
                        .Select(npc => npc.DisplayName)
                        .ToList();
                    if (npcNames.Count > 0)
                    {
                        // NPC names use comma-separated (OR logic)
                        stringFilters.Add(string.Join(",", npcNames));
                    }

                    // Add Keywords (with + for AND logic)
                    var keywordEditorIds = entryVm.SelectedKeywords
                        .Where(k => !string.IsNullOrWhiteSpace(k.EditorID) && k.EditorID != "(No EditorID)")
                        .Select(k => k.EditorID)
                        .ToList();
                    if (keywordEditorIds.Count > 0)
                    {
                        // Keywords use + for AND logic
                        stringFilters.Add(string.Join("+", keywordEditorIds));
                    }

                    var stringFiltersPart = stringFilters.Count > 0 ? string.Join(",", stringFilters) : null;

                    // FormFilters (position 3): Factions and Races (with + for AND logic)
                    var formFilters = new List<string>();
                    foreach (var faction in entryVm.SelectedFactions)
                    {
                        var editorId = faction.EditorID;
                        if (!string.IsNullOrWhiteSpace(editorId) && editorId != "(No EditorID)")
                        {
                            formFilters.Add(editorId);
                        }
                    }
                    foreach (var race in entryVm.SelectedRaces)
                    {
                        var editorId = race.EditorID;
                        if (!string.IsNullOrWhiteSpace(editorId) && editorId != "(No EditorID)")
                        {
                            formFilters.Add(editorId);
                        }
                    }
                    var formFiltersPart = formFilters.Count > 0 ? string.Join("+", formFilters) : null;

                    // LevelFilters (position 4): Not supported yet
                    string? levelFiltersPart = null;

                    // TraitFilters (position 5): from entry
                    var traitFiltersPart = FormatTraitFilters(entryVm.Entry.TraitFilters);

                    // CountOrPackageIdx (position 6): Not supported yet
                    string? countPart = null;

                    // Chance (position 7) - only include if not 100 or if explicitly set
                    var chancePart = entryVm.UseChance && entryVm.Chance != 100
                        ? entryVm.Chance.ToString(CultureInfo.InvariantCulture)
                        : null;

                    // Build SPID line - preserve intermediate NONEs, trim trailing ones
                    var filterParts = new[] { stringFiltersPart, formFiltersPart, levelFiltersPart, traitFiltersPart, countPart, chancePart };

                    // Find the last non-null position
                    var lastNonNullIndex = -1;
                    for (var i = filterParts.Length - 1; i >= 0; i--)
                    {
                        if (filterParts[i] != null)
                        {
                            lastNonNullIndex = i;
                            break;
                        }
                    }

                    // Build the line
                    var sb = new System.Text.StringBuilder();
                    sb.Append("Outfit = ");
                    sb.Append(outfitIdentifier);

                    for (var i = 0; i <= lastNonNullIndex; i++)
                    {
                        sb.Append('|');
                        sb.Append(filterParts[i] ?? "NONE");
                    }

                    lines.Add(sb.ToString());
                }
                else
                {
                    // SkyPatcher format: filterByNpcs=...:filterByFactions=...:filterByKeywords=...:filterByRaces=...:outfitDefault=...
                    var filterParts = new List<string>();

                    // Add NPC filter if present
                    if (entryVm.SelectedNpcs.Count > 0)
                    {
                        var npcFormKeys = entryVm.SelectedNpcs
                            .Select(npc => FormKeyHelper.Format(npc.FormKey))
                            .ToList();
                        var npcList = string.Join(",", npcFormKeys);
                        filterParts.Add($"filterByNpcs={npcList}");
                    }

                    // Add faction filter if present
                    if (entryVm.SelectedFactions.Count > 0)
                    {
                        var factionFormKeys = entryVm.SelectedFactions
                            .Select(faction => FormKeyHelper.Format(faction.FormKey))
                            .ToList();
                        var factionList = string.Join(",", factionFormKeys);
                        filterParts.Add($"filterByFactions={factionList}");
                    }

                    // Add keyword filter if present
                    if (entryVm.SelectedKeywords.Count > 0)
                    {
                        var keywordFormKeys = entryVm.SelectedKeywords
                            .Select(keyword => FormKeyHelper.Format(keyword.FormKey))
                            .ToList();
                        var keywordList = string.Join(",", keywordFormKeys);
                        filterParts.Add($"filterByKeywords={keywordList}");
                    }

                    // Add race filter if present
                    if (entryVm.SelectedRaces.Count > 0)
                    {
                        var raceFormKeys = entryVm.SelectedRaces
                            .Select(race => FormKeyHelper.Format(race.FormKey))
                            .ToList();
                        var raceList = string.Join(",", raceFormKeys);
                        filterParts.Add($"filterByRaces={raceList}");
                    }

                    // Add outfit
                    var outfitFormKey = FormKeyHelper.Format(entryVm.SelectedOutfit.FormKey);
                    filterParts.Add($"outfitDefault={outfitFormKey}");

                    var line = string.Join(":", filterParts);
                    lines.Add(line);
                }
            }

            DistributionPreviewText = string.Join(Environment.NewLine, lines);

            _logger.Debug("UpdateDistributionPreview: Generated {LineCount} lines", lines.Count);

            // Also detect conflicts when preview is updated (runs asynchronously internally)
            DetectConflicts();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error updating distribution preview");
            DistributionPreviewText = $"; Error generating preview: {ex.Message}";
        }
    }

    private async Task LoadAvailableOutfitsAsync()
    {
        // Only load once, and only if not already loaded and collection has items
        if (_outfitsLoaded && AvailableOutfits.Count > 0)
            return;

        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            // If LinkCache is not available, clear collection and reset flag so we'll try again later
            if (AvailableOutfits.Count > 0)
            {
                AvailableOutfits.Clear();
            }
            _outfitsLoaded = false;
            return;
        }

        try
        {
            // Load outfits from the active load order (enabled plugins)
            var outfits = await Task.Run(() =>
                linkCache.WinningOverrides<IOutfitGetter>().ToList());

            // Also load outfits from the patch file if it exists but isn't in the active load order
            // This handles newly created patches that aren't enabled in plugins.txt yet
            await MergeOutfitsFromPatchFileAsync(outfits);

            // Update collection on UI thread
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
            // Fire and forget - the async method will update the collection when done
            _ = LoadAvailableOutfitsAsync();
        }
    }

    private async void OnPluginsChanged(object? sender, EventArgs e)
    {
        _logger.Debug("PluginsChanged event received in DistributionEditTabViewModel, invalidating outfits cache...");

        // Reset the loaded flag so outfits will be reloaded on next access
        _outfitsLoaded = false;

        // Reload outfits immediately so the dropdown has the latest
        _logger.Information("Reloading available outfits...");
        await LoadAvailableOutfitsAsync();
    }

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
            // Not creating a new file, no need to check conflicts
            HasConflicts = false;
            ConflictsResolvedByFilename = false;
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ClearNpcConflictIndicators();
            return;
        }

        // If no entries, immediately clear conflict state (no conflicts possible)
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

        // Need distribution files for conflict detection
        if (_distributionFiles == null || _distributionFiles.Count == 0)
        {
            HasConflicts = false;
            ConflictsResolvedByFilename = false;
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ClearNpcConflictIndicators();
            return;
        }

        // Capture current entry count to detect if entries were cleared while async operation runs
        var entryCountAtStart = DistributionEntries.Count;
        var entriesSnapshot = DistributionEntries.ToList();

        // Run expensive conflict detection on background thread
        Task.Run(() =>
        {
            try
            {
                // Use the conflict detection service
                var result = DistributionConflictDetectionService.DetectConflicts(
                    entriesSnapshot,
                    _distributionFiles.ToList(),
                    NewFileName,
                    linkCache);

                // Update properties from result on UI thread
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    // Only update if entry count hasn't changed (entries weren't cleared while we were running)
                    if (DistributionEntries.Count == entryCountAtStart && DistributionEntries.Count > 0)
                    {
                        HasConflicts = result.HasConflicts;
                        ConflictsResolvedByFilename = result.ConflictsResolvedByFilename;
                        ConflictSummary = result.ConflictSummary;
                        SuggestedFileName = result.SuggestedFileName;

                        // Update NPC conflict indicators
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
                        // Entries were cleared or changed, clear conflict state
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

    /// <summary>
    /// Clears conflict indicators on all NPCs in distribution entries.
    /// </summary>
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
    /// Called when a user enables chance-based distribution. Changes format to SPID if needed.
    /// </summary>
    private void OnUseChanceEnabling()
    {
        // Change format to SPID if it's currently SkyPatcher
        if (DistributionFormat == DistributionFileType.SkyPatcher)
        {
            DistributionFormat = DistributionFileType.Spid;
            UpdateDistributionPreview();
        }
    }

    /// <summary>
    /// Creates a DistributionEntryViewModel from a DistributionEntry,
    /// resolving outfit and NPC references for proper UI binding.
    /// </summary>
    private DistributionEntryViewModel CreateEntryViewModel(DistributionEntry entry)
    {
        var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry, OnUseChanceEnabling);

        // Resolve outfit to AvailableOutfits instance for ComboBox binding
        ResolveEntryOutfit(entryVm);

        // Resolve NPCs from FormKeys
        var npcVms = ResolveNpcFormKeys(entry.NpcFormKeys);
        if (npcVms.Count > 0)
        {
            entryVm.SelectedNpcs = new ObservableCollection<NpcRecordViewModel>(npcVms);
            entryVm.UpdateEntryNpcs();
        }

        // Resolve Factions from FormKeys
        var factionVms = ResolveFactionFormKeys(entry.FactionFormKeys);
        if (factionVms.Count > 0)
        {
            entryVm.SelectedFactions = new ObservableCollection<FactionRecordViewModel>(factionVms);
            entryVm.UpdateEntryFactions();
        }

        // Resolve Keywords from FormKeys
        var keywordVms = ResolveKeywordFormKeys(entry.KeywordFormKeys);
        if (keywordVms.Count > 0)
        {
            entryVm.SelectedKeywords = new ObservableCollection<KeywordRecordViewModel>(keywordVms);
            entryVm.UpdateEntryKeywords();
        }

        // Resolve Races from FormKeys
        var raceVms = ResolveRaceFormKeys(entry.RaceFormKeys);
        if (raceVms.Count > 0)
        {
            entryVm.SelectedRaces = new ObservableCollection<RaceRecordViewModel>(raceVms);
            entryVm.UpdateEntryRaces();
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
        // Try to find in AvailableNpcs first
        var existingNpc = AvailableNpcs.FirstOrDefault(npc => npc.FormKey == formKey);
        if (existingNpc != null)
        {
            return existingNpc;
        }

        // If not in AvailableNpcs, resolve from LinkCache
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

    private List<KeywordRecordViewModel> ResolveKeywordFormKeys(IEnumerable<FormKey> formKeys)
    {
        var keywordVms = new List<KeywordRecordViewModel>();

        foreach (var formKey in formKeys)
        {
            var keywordVm = ResolveKeywordFormKey(formKey);
            if (keywordVm != null)
            {
                keywordVms.Add(keywordVm);
            }
        }

        return keywordVms;
    }

    private KeywordRecordViewModel? ResolveKeywordFormKey(FormKey formKey)
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

        // Update the collection in-place to preserve checkbox state
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

    /// <summary>
    /// Formats trait filters for SPID output.
    /// </summary>
    private static string? FormatTraitFilters(Models.SpidTraitFilters traits)
    {
        if (traits.IsEmpty)
            return null;

        var parts = new List<string>();

        if (traits.IsFemale == true)
            parts.Add("F");
        else if (traits.IsFemale == false)
            parts.Add("M");

        if (traits.IsUnique == true)
            parts.Add("U");
        else if (traits.IsUnique == false)
            parts.Add("-U");

        if (traits.IsSummonable == true)
            parts.Add("S");
        else if (traits.IsSummonable == false)
            parts.Add("-S");

        if (traits.IsChild == true)
            parts.Add("C");
        else if (traits.IsChild == false)
            parts.Add("-C");

        if (traits.IsLeveled == true)
            parts.Add("L");
        else if (traits.IsLeveled == false)
            parts.Add("-L");

        if (traits.IsTeammate == true)
            parts.Add("T");
        else if (traits.IsTeammate == false)
            parts.Add("-T");

        if (traits.IsDead == true)
            parts.Add("D");
        else if (traits.IsDead == false)
            parts.Add("-D");

        return parts.Count > 0 ? string.Join("/", parts) : null;
    }
}
