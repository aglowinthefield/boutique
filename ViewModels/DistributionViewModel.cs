using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Microsoft.Win32;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using Serilog;

namespace Boutique.ViewModels;

public enum DistributionTab
{
    Files = 0,
    Edit = 1,
    Npcs = 2
}

public class DistributionViewModel : ReactiveObject
{
    private readonly IDistributionDiscoveryService _discoveryService;
    private readonly IDistributionFileWriterService _fileWriterService;
    private readonly INpcScanningService _npcScanningService;
    private readonly INpcOutfitResolutionService _npcOutfitResolutionService;
    private readonly ILogger _logger;
    private readonly IMutagenService _mutagenService;
    private readonly IArmorPreviewService _armorPreviewService;
    private readonly SettingsViewModel _settings;

    private ObservableCollection<DistributionFileViewModel> _files = new();
    private ObservableCollection<DistributionEntryViewModel> _distributionEntries = new();
    private bool _isBulkLoading;
    
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
        entry.WhenAnyValue(evm => evm.SelectedOutfit)
            .Skip(1) // Skip initial value
            .Subscribe(_ => UpdateDistributionPreview());
        entry.WhenAnyValue(evm => evm.SelectedNpcs)
            .Skip(1) // Skip initial value
            .Subscribe(_ => UpdateDistributionPreview());
        entry.SelectedNpcs.CollectionChanged += (s, args) => UpdateDistributionPreview();
    }
    
    private int DistributionEntriesCount => _distributionEntries.Count;
    private ObservableCollection<NpcRecordViewModel> _availableNpcs = new();
    private ObservableCollection<IOutfitGetter> _availableOutfits = new();
    private bool _outfitsLoaded;
    private ObservableCollection<DistributionFileSelectionItem> _availableDistributionFiles = new();
    private bool _isLoading;
    private DistributionFileViewModel? _selectedFile;
    private DistributionFileSelectionItem? _selectedDistributionFile;
    private DistributionEntryViewModel? _selectedEntry;
    private string _statusMessage = "Distribution files not loaded.";
    private string _lineFilter = string.Empty;
    private string _distributionFilePath = string.Empty;
    private string _newFileName = string.Empty;
    private bool _isCreatingNewFile;
    private string _npcSearchText = string.Empty;
    private string _distributionPreviewText = string.Empty;
    private ObservableCollection<NpcRecordViewModel> _filteredNpcs = new();
    private bool _hasConflicts;
    private bool _conflictsResolvedByFilename;
    private string _conflictSummary = string.Empty;
    private string _suggestedFileName = string.Empty;
    
    // NPCs tab fields
    private int _selectedTabIndex;
    private ObservableCollection<NpcOutfitAssignmentViewModel> _npcOutfitAssignments = new();
    private NpcOutfitAssignmentViewModel? _selectedNpcAssignment;
    private string _npcOutfitSearchText = string.Empty;
    private ObservableCollection<NpcOutfitAssignmentViewModel> _filteredNpcOutfitAssignments = new();
    private string _selectedNpcOutfitContents = string.Empty;

    public DistributionViewModel(
        IDistributionDiscoveryService discoveryService,
        IDistributionFileWriterService fileWriterService,
        INpcScanningService npcScanningService,
        INpcOutfitResolutionService npcOutfitResolutionService,
        SettingsViewModel settings,
        IArmorPreviewService armorPreviewService,
        IMutagenService mutagenService,
        ILogger logger)
    {
        _discoveryService = discoveryService;
        _fileWriterService = fileWriterService;
        _npcScanningService = npcScanningService;
        _npcOutfitResolutionService = npcOutfitResolutionService;
        _settings = settings;
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _logger = logger.ForContext<DistributionViewModel>();

        // Subscribe to plugin changes so we refresh the available outfits list
        _mutagenService.PluginsChanged += OnPluginsChanged;

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        PreviewLineCommand = ReactiveCommand.CreateFromTask<DistributionLine>(PreviewLineAsync, 
            this.WhenAnyValue(vm => vm.IsLoading).Select(loading => !loading));
        PreviewEntryCommand = ReactiveCommand.CreateFromTask<DistributionEntryViewModel>(PreviewEntryAsync,
            this.WhenAnyValue(vm => vm.IsLoading).Select(loading => !loading));
        
        // Subscribe to collection changes to update computed count property
        _distributionEntries.CollectionChanged += OnDistributionEntriesChanged;
        
        AddDistributionEntryCommand = ReactiveCommand.Create(AddDistributionEntry);
        RemoveDistributionEntryCommand = ReactiveCommand.Create<DistributionEntryViewModel>(RemoveDistributionEntry);
        SelectEntryCommand = ReactiveCommand.Create<DistributionEntryViewModel>(SelectEntry);
        
        // Use the computed property that raises PropertyChanged when collection changes
        // Defer evaluation to avoid blocking
        var hasEntries = this.WhenAnyValue(vm => vm.DistributionEntriesCount)
            .Select(count => count > 0)
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler);
        
        AddSelectedNpcsToEntryCommand = ReactiveCommand.Create(AddSelectedNpcsToEntry, hasEntries);
        
        var canSave = Observable.CombineLatest(
            hasEntries,
            this.WhenAnyValue(vm => vm.DistributionFilePath),
            this.WhenAnyValue(vm => vm.IsCreatingNewFile),
            this.WhenAnyValue(vm => vm.NewFileName),
            (hasEntries, path, isNew, newName) => 
                hasEntries && 
                (!string.IsNullOrWhiteSpace(path) || (isNew && !string.IsNullOrWhiteSpace(newName))))
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler);
        
        SaveDistributionFileCommand = ReactiveCommand.CreateFromTask(SaveDistributionFileAsync, canSave);
        LoadDistributionFileCommand = ReactiveCommand.CreateFromTask(LoadDistributionFileAsync,
            this.WhenAnyValue(vm => vm.IsLoading).Select(loading => !loading));
        ScanNpcsCommand = ReactiveCommand.CreateFromTask(ScanNpcsAsync,
            this.WhenAnyValue(vm => vm.IsLoading).Select(loading => !loading));
        SelectDistributionFilePathCommand = ReactiveCommand.Create(SelectDistributionFilePath);
        
        // NPCs tab commands
        ScanNpcOutfitsCommand = ReactiveCommand.CreateFromTask(ScanNpcOutfitsAsync,
            this.WhenAnyValue(vm => vm.IsLoading).Select(loading => !loading));
        PreviewNpcOutfitCommand = ReactiveCommand.CreateFromTask<NpcOutfitAssignmentViewModel>(PreviewNpcOutfitAsync,
            this.WhenAnyValue(vm => vm.IsLoading).Select(loading => !loading));

        _settings.WhenAnyValue(x => x.SkyrimDataPath)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(DataPath));
                if (IsCreatingNewFile)
                {
                    UpdateDistributionFilePathFromNewFileName();
                }
            });

        // Trigger edit mode initialization when Edit tab is selected
        this.WhenAnyValue(vm => vm.SelectedTabIndex)
            .Where(index => index == (int)DistributionTab.Edit)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsEditMode));
                UpdateAvailableDistributionFiles();
                if (SelectedDistributionFile == null)
                {
                    // Select "New File" by default
                    var newFileItem = AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
                    if (newFileItem != null)
                    {
                        SelectedDistributionFile = newFileItem;
                    }
                }
                // Don't load outfits upfront - load lazily when ComboBox opens
                // Update preview when entering edit mode
                UpdateDistributionPreview();
            });

        this.WhenAnyValue(vm => vm.NpcSearchText)
            .Throttle(TimeSpan.FromMilliseconds(150))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateFilteredNpcs());
        
        // NPCs tab search filtering
        this.WhenAnyValue(vm => vm.NpcOutfitSearchText)
            .Throttle(TimeSpan.FromMilliseconds(150))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateFilteredNpcOutfitAssignments());
        
        // Update outfit contents when selection changes
        this.WhenAnyValue(vm => vm.SelectedNpcAssignment)
            .Subscribe(_ => UpdateSelectedNpcOutfitContents());
        
        // Auto-scan NPC outfits when NPCs tab is selected
        this.WhenAnyValue(vm => vm.SelectedTabIndex)
            .Where(index => index == (int)DistributionTab.Npcs)
            .Subscribe(_ => 
            {
                // Only auto-scan if we haven't already scanned
                if (NpcOutfitAssignments.Count == 0 && !IsLoading)
                {
                    _logger.Debug("NPCs tab selected, triggering auto-scan");
                    Task.Run(() => ScanNpcOutfitsAsync());
                }
            });
    }

    public ObservableCollection<DistributionFileViewModel> Files
    {
        get => _files;
        private set => this.RaiseAndSetIfChanged(ref _files, value);
    }

    public DistributionFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            var previous = _selectedFile;
            this.RaiseAndSetIfChanged(ref _selectedFile, value);
            if (!Equals(previous, value))
                this.RaisePropertyChanged(nameof(FilteredLines));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<DistributionLine, Unit> PreviewLineCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> PreviewEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddDistributionEntryCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> RemoveDistributionEntryCommand { get; }
    public ReactiveCommand<DistributionEntryViewModel, Unit> SelectEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSelectedNpcsToEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveDistributionFileCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadDistributionFileCommand { get; }
    public ReactiveCommand<Unit, Unit> ScanNpcsCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectDistributionFilePathCommand { get; }
    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();
    
    // NPCs tab commands
    public ReactiveCommand<Unit, Unit> ScanNpcOutfitsCommand { get; }
    public ReactiveCommand<NpcOutfitAssignmentViewModel, Unit> PreviewNpcOutfitCommand { get; }

    public string LineFilter
    {
        get => _lineFilter;
        set
        {
            var previous = _lineFilter;
            this.RaiseAndSetIfChanged(ref _lineFilter, value ?? string.Empty);
            if (!string.Equals(previous, _lineFilter, StringComparison.OrdinalIgnoreCase))
                this.RaisePropertyChanged(nameof(FilteredLines));
        }
    }

    public IEnumerable<DistributionLine> FilteredLines
    {
        get
        {
            var lines = SelectedFile?.Lines ?? Array.Empty<DistributionLine>();

            if (string.IsNullOrWhiteSpace(LineFilter))
                return lines;

            var term = LineFilter.Trim();
            return lines.Where(line => line.RawText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string DataPath => _settings.SkyrimDataPath;

    public bool IsEditMode => SelectedTabIndex == (int)DistributionTab.Edit;

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

    public DistributionEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            // Clear previous selection
            if (_selectedEntry != null)
            {
                _selectedEntry.IsSelected = false;
            }
            
            this.RaiseAndSetIfChanged(ref _selectedEntry, value);
            
            // Set new selection
            if (value != null)
            {
                value.IsSelected = true;
            }
        }
    }

    public ObservableCollection<NpcRecordViewModel> AvailableNpcs
    {
        get => _availableNpcs;
        private set => this.RaiseAndSetIfChanged(ref _availableNpcs, value);
    }

    public ObservableCollection<IOutfitGetter> AvailableOutfits
    {
        get => _availableOutfits;
        private set => this.RaiseAndSetIfChanged(ref _availableOutfits, value);
    }

    public ObservableCollection<DistributionFileSelectionItem> AvailableDistributionFiles
    {
        get => _availableDistributionFiles;
        private set => this.RaiseAndSetIfChanged(ref _availableDistributionFiles, value);
    }

    public DistributionFileSelectionItem? SelectedDistributionFile
    {
        get => _selectedDistributionFile;
        set
        {
            var previous = _selectedDistributionFile;
            this.RaiseAndSetIfChanged(ref _selectedDistributionFile, value);
            
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
                        _ = LoadDistributionFileAsync();
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

    public bool IsCreatingNewFile
    {
        get => _isCreatingNewFile;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isCreatingNewFile, value);
            // Re-detect conflicts when switching between new file / existing file mode
            DetectConflicts();
        }
    }

    public bool ShowNewFileNameInput => IsCreatingNewFile;

    public string NewFileName
    {
        get => _newFileName;
        set
        {
            this.RaiseAndSetIfChanged(ref _newFileName, value ?? string.Empty);
            if (IsCreatingNewFile)
            {
                UpdateDistributionFilePathFromNewFileName();
                // Re-detect conflicts when filename changes
                DetectConflicts();
            }
        }
    }

    public string DistributionFilePath
    {
        get => _distributionFilePath;
        private set => this.RaiseAndSetIfChanged(ref _distributionFilePath, value);
    }

    public string NpcSearchText
    {
        get => _npcSearchText;
        set => this.RaiseAndSetIfChanged(ref _npcSearchText, value ?? string.Empty);
    }

    public string DistributionPreviewText
    {
        get => _distributionPreviewText;
        private set => this.RaiseAndSetIfChanged(ref _distributionPreviewText, value);
    }

    public ObservableCollection<NpcRecordViewModel> FilteredNpcs
    {
        get => _filteredNpcs;
        private set => this.RaiseAndSetIfChanged(ref _filteredNpcs, value);
    }

    // NPCs tab properties
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
    }

    public ObservableCollection<NpcOutfitAssignmentViewModel> NpcOutfitAssignments
    {
        get => _npcOutfitAssignments;
        private set => this.RaiseAndSetIfChanged(ref _npcOutfitAssignments, value);
    }

    public NpcOutfitAssignmentViewModel? SelectedNpcAssignment
    {
        get => _selectedNpcAssignment;
        set
        {
            // Clear previous selection
            if (_selectedNpcAssignment != null)
            {
                _selectedNpcAssignment.IsSelected = false;
            }
            
            this.RaiseAndSetIfChanged(ref _selectedNpcAssignment, value);
            
            // Set new selection
            if (value != null)
            {
                value.IsSelected = true;
            }
        }
    }

    public string NpcOutfitSearchText
    {
        get => _npcOutfitSearchText;
        set => this.RaiseAndSetIfChanged(ref _npcOutfitSearchText, value ?? string.Empty);
    }

    public ObservableCollection<NpcOutfitAssignmentViewModel> FilteredNpcOutfitAssignments
    {
        get => _filteredNpcOutfitAssignments;
        private set => this.RaiseAndSetIfChanged(ref _filteredNpcOutfitAssignments, value);
    }

    public string SelectedNpcOutfitContents
    {
        get => _selectedNpcOutfitContents;
        private set => this.RaiseAndSetIfChanged(ref _selectedNpcOutfitContents, value);
    }

    public bool IsInitialized => _mutagenService.IsInitialized;

    /// <summary>
    /// Indicates whether the current distribution entries have conflicts with existing files.
    /// </summary>
    public bool HasConflicts
    {
        get => _hasConflicts;
        private set => this.RaiseAndSetIfChanged(ref _hasConflicts, value);
    }

    /// <summary>
    /// Indicates whether conflicts exist but are resolved by the current filename ordering.
    /// </summary>
    public bool ConflictsResolvedByFilename
    {
        get => _conflictsResolvedByFilename;
        private set => this.RaiseAndSetIfChanged(ref _conflictsResolvedByFilename, value);
    }

    /// <summary>
    /// Summary text describing the detected conflicts.
    /// </summary>
    public string ConflictSummary
    {
        get => _conflictSummary;
        private set => this.RaiseAndSetIfChanged(ref _conflictSummary, value);
    }

    /// <summary>
    /// The suggested filename with Z-prefix to ensure proper load order.
    /// </summary>
    public string SuggestedFileName
    {
        get => _suggestedFileName;
        private set => this.RaiseAndSetIfChanged(ref _suggestedFileName, value);
    }

    public async Task RefreshAsync()
    {
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;
            var dataPath = _settings.SkyrimDataPath;

            if (string.IsNullOrWhiteSpace(dataPath))
            {
                Files = [];
                StatusMessage = "Set the Skyrim data path in Settings to scan distribution files.";
                return;
            }

            if (!Directory.Exists(dataPath))
            {
                Files = [];
                StatusMessage = $"Skyrim data path does not exist: {dataPath}";
                return;
            }

            StatusMessage = "Scanning for distribution files...";
            var discovered = await _discoveryService.DiscoverAsync(dataPath);
            var outfitFiles = discovered
                .Where(file => file.OutfitDistributionCount > 0)
                .ToList();

            var viewModels = outfitFiles
                .Select(file => new DistributionFileViewModel(file))
                .ToList();

            Files = new ObservableCollection<DistributionFileViewModel>(viewModels);
            LineFilter = string.Empty;
            SelectedFile = Files.FirstOrDefault();

            // Update available distribution files for dropdown
            UpdateAvailableDistributionFiles();

            StatusMessage = Files.Count == 0
                ? "No outfit distributions found."
                : $"Found {Files.Count} outfit distribution file(s).";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh distribution files.");
            StatusMessage = $"Error loading distribution files: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PreviewLineAsync(DistributionLine? line)
    {
        if (line == null)
            return;

        if (line.OutfitFormKeys.Count == 0)
        {
            StatusMessage = "Selected line does not reference an outfit.";
            return;
        }

        if (!_mutagenService.IsInitialized || _mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            StatusMessage = "Initialize Skyrim data path before previewing outfits.";
            return;
        }

        List<IOutfitGetter>? cachedOutfits = null;

        foreach (var keyString in line.OutfitFormKeys)
        {
            if (!OutfitResolver.TryResolve(keyString, linkCache, ref cachedOutfits, out var outfit, out var label))
                continue;

            var armorPieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
            if (armorPieces.Count == 0)
                continue;

            try
            {
                StatusMessage = $"Building preview for {label}...";
                var scene = await _armorPreviewService.BuildPreviewAsync(armorPieces, GenderedModelVariant.Female);
                await ShowPreview.Handle(scene);
                StatusMessage = $"Preview ready for {label}.";
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to preview outfit {Identifier}", label);
                StatusMessage = $"Failed to preview outfit: {ex.Message}";
                return;
            }
        }

        StatusMessage = "Unable to resolve outfit for preview.";
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

    private void AddDistributionEntry()
    {
        _logger.Debug("AddDistributionEntry called");
        try
        {
            _logger.Debug("Creating DistributionEntry");
            var entry = new DistributionEntry();
            
            _logger.Debug("Creating DistributionEntryViewModel");
            var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry);
            
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
            sb.AppendLine($"    {SuggestedFileName}");
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

            await _fileWriterService.WriteDistributionFileAsync(finalFilePath, entries);

            StatusMessage = $"Successfully saved distribution file: {Path.GetFileName(finalFilePath)}";
            _logger.Information("Saved distribution file: {FilePath}", finalFilePath);

            // Refresh the file list
            await RefreshAsync();
            
            // If we saved a new file, select it in the dropdown
            if (IsCreatingNewFile)
            {
                var savedFile = Files.FirstOrDefault(f => 
                    string.Equals(f.FullPath, finalFilePath, StringComparison.OrdinalIgnoreCase));
                if (savedFile != null)
                {
                    var matchingItem = AvailableDistributionFiles.FirstOrDefault(item => 
                        !item.IsNewFile && item.File == savedFile);
                    if (matchingItem != null)
                    {
                        SelectedDistributionFile = matchingItem;
                    }
                }
            }
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

    private async Task LoadDistributionFileAsync()
    {
        if (string.IsNullOrWhiteSpace(DistributionFilePath) || !File.Exists(DistributionFilePath))
        {
            StatusMessage = "File does not exist. Please select a valid file.";
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = "Loading distribution file...";

            var entries = await _fileWriterService.LoadDistributionFileAsync(DistributionFilePath);

            // Ensure outfits are loaded before creating entries so ComboBox bindings work
            await LoadAvailableOutfitsAsync();

            // Use bulk loading to avoid triggering expensive updates for each entry
            _isBulkLoading = true;
            try
            {
                DistributionEntries.Clear();
                
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

            // Update selected file in dropdown to match loaded file
            var matchingFile = Files.FirstOrDefault(f => 
                string.Equals(f.FullPath, DistributionFilePath, StringComparison.OrdinalIgnoreCase));
            if (matchingFile != null)
            {
                var matchingItem = AvailableDistributionFiles.FirstOrDefault(item => 
                    !item.IsNewFile && item.File == matchingFile);
                if (matchingItem != null)
                {
                    SelectedDistributionFile = matchingItem;
                }
            }

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

            StatusMessage = "Scanning NPCs from modlist...";

            var npcs = await _npcScanningService.ScanNpcsAsync();

            AvailableNpcs.Clear();
            foreach (var npc in npcs)
            {
                var npcVm = new NpcRecordViewModel(npc);
                AvailableNpcs.Add(npcVm);
            }
            
            // Update the filtered list after populating
            UpdateFilteredNpcs();

            StatusMessage = $"Scanned {AvailableNpcs.Count} NPCs.";
            _logger.Information("Scanned {Count} NPCs.", AvailableNpcs.Count);
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

    private void UpdateAvailableDistributionFiles()
    {
        var previousSelected = SelectedDistributionFile;
        var previousFilePath = DistributionFilePath;
        
        var items = new List<DistributionFileSelectionItem>();
        
        // Add "New File" option
        items.Add(new DistributionFileSelectionItem(isNewFile: true, file: null));
        
        // Add existing files
        foreach (var file in Files)
        {
            items.Add(new DistributionFileSelectionItem(isNewFile: false, file: file));
        }
        
        AvailableDistributionFiles = new ObservableCollection<DistributionFileSelectionItem>(items);
        
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
        var lines = new List<string>();

        // Add header comment
        lines.Add("; SkyPatcher Distribution File");
        lines.Add("; Generated by Boutique");
        lines.Add("");

        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            DistributionPreviewText = string.Join(Environment.NewLine, lines) + Environment.NewLine + "; LinkCache not available";
            return;
        }

        foreach (var entryVm in DistributionEntries)
        {
            if (entryVm.SelectedOutfit == null || entryVm.SelectedNpcs.Count == 0)
                continue;

            var npcFormKeys = entryVm.SelectedNpcs
                .Select(npc => FormKeyHelper.Format(npc.FormKey))
                .ToList();

            var npcList = string.Join(",", npcFormKeys);
            var outfitFormKey = FormKeyHelper.Format(entryVm.SelectedOutfit.FormKey);

            var line = $"filterByNpcs={npcList}:outfitDefault={outfitFormKey}";
            lines.Add(line);
        }

        DistributionPreviewText = string.Join(Environment.NewLine, lines);
        
        // Also detect conflicts when preview is updated
        DetectConflicts();
    }

    private async Task LoadAvailableOutfitsAsync()
    {
        // Only load once, and only if not already loaded
        if (_outfitsLoaded || AvailableOutfits.Count > 0)
            return;

        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            AvailableOutfits.Clear();
            return;
        }

        try
        {
            // Load outfits from the active load order (enabled plugins)
            var outfits = await Task.Run(() => 
                linkCache.PriorityOrder.WinningOverrides<IOutfitGetter>().ToList());

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

    // Public method to trigger lazy loading when ComboBox opens
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
        _logger.Debug("PluginsChanged event received in DistributionViewModel, invalidating outfits cache...");

        // Reset the loaded flag so outfits will be reloaded on next access
        _outfitsLoaded = false;

        // If we're in edit mode, reload outfits immediately so the dropdown has the latest
        if (IsEditMode)
        {
            _logger.Information("Edit mode active, reloading available outfits...");
            await LoadAvailableOutfitsAsync();
        }
    }

    #region Helper Methods

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
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ClearNpcConflictIndicators();
            return;
        }

        // Build a set of NPC FormKeys from current entries
        var npcFormKeysInEntries = DistributionEntries
            .SelectMany(e => e.SelectedNpcs)
            .Select(npc => npc.FormKey)
            .ToHashSet();

        if (npcFormKeysInEntries.Count == 0)
        {
            HasConflicts = false;
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ClearNpcConflictIndicators();
            return;
        }

        // Build a map of NPC FormKey -> (FileName, OutfitEditorId) from existing distribution files
        var existingDistributions = BuildExistingDistributionMap();

        // Find conflicts
        var conflicts = new List<Models.NpcConflictInfo>();
        var conflictingFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in DistributionEntries)
        {
            var newOutfitName = entry.SelectedOutfit?.EditorID ?? entry.SelectedOutfit?.FormKey.ToString();
            
            foreach (var npcVm in entry.SelectedNpcs)
            {
                if (existingDistributions.TryGetValue(npcVm.FormKey, out var existing))
                {
                    conflicts.Add(new Models.NpcConflictInfo(
                        npcVm.FormKey,
                        npcVm.DisplayName,
                        existing.FileName,
                        existing.OutfitName,
                        newOutfitName));
                    
                    conflictingFileNames.Add(existing.FileName);
                    
                    // Update NPC conflict indicator
                    npcVm.HasConflict = true;
                    npcVm.ConflictingFileName = existing.FileName;
                }
                else
                {
                    npcVm.HasConflict = false;
                    npcVm.ConflictingFileName = null;
                }
            }
        }

        // Check if the current filename already loads after all conflicting files
        var currentFileLoadsLast = DoesFileLoadAfterAll(NewFileName, conflictingFileNames);

        // Only show as conflict if the user's file wouldn't load last
        HasConflicts = conflicts.Count > 0 && !currentFileLoadsLast;
        ConflictsResolvedByFilename = conflicts.Count > 0 && currentFileLoadsLast;

        if (conflicts.Count > 0)
        {
            if (currentFileLoadsLast)
            {
                // Conflict exists but is resolved by filename ordering
                ConflictSummary = $"✓ {conflicts.Count} NPC(s) have existing distributions, but your filename '{NewFileName}' will load after them.";
                SuggestedFileName = NewFileName;
                
                // Clear NPC conflict indicators since the conflict is resolved
                ClearNpcConflictIndicators();
            }
            else
            {
                // Build conflict summary
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"⚠ {conflicts.Count} NPC(s) already have outfit distributions in existing files:");
                
                foreach (var conflict in conflicts.Take(5)) // Show first 5
                {
                    sb.AppendLine($"  • {conflict.DisplayName ?? conflict.NpcFormKey.ToString()} ({conflict.ExistingFileName})");
                }
                
                if (conflicts.Count > 5)
                {
                    sb.AppendLine($"  ... and {conflicts.Count - 5} more");
                }
                
                ConflictSummary = sb.ToString().TrimEnd();

                // Calculate suggested filename with Z-prefix
                SuggestedFileName = CalculateZPrefixedFileName(conflictingFileNames);
            }
        }
        else
        {
            ConflictSummary = string.Empty;
            SuggestedFileName = NewFileName;
            ConflictsResolvedByFilename = false;
        }
    }

    /// <summary>
    /// Checks if the given filename would alphabetically load after all the conflicting filenames.
    /// </summary>
    private static bool DoesFileLoadAfterAll(string fileName, HashSet<string> conflictingFileNames)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        // No conflicting files means we're already "after" all of them (vacuously true)
        if (conflictingFileNames.Count == 0)
            return true;

        foreach (var conflictingFile in conflictingFileNames)
        {
            // Compare alphabetically (case-insensitive, like file systems)
            if (string.Compare(fileName, conflictingFile, StringComparison.OrdinalIgnoreCase) <= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds a map of NPC FormKey to existing distribution info from loaded distribution files.
    /// </summary>
    private Dictionary<FormKey, (string FileName, string? OutfitName)> BuildExistingDistributionMap()
    {
        var map = new Dictionary<FormKey, (string FileName, string? OutfitName)>();

        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
            return map;

        // Build lookup dictionaries for NPC resolution
        var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
        var npcByEditorId = allNpcs
            .Where(n => !string.IsNullOrWhiteSpace(n.EditorID))
            .GroupBy(n => n.EditorID!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var npcByName = allNpcs
            .Where(n => !string.IsNullOrWhiteSpace(n.Name?.String))
            .GroupBy(n => n.Name!.String!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var file in Files)
        {
            foreach (var line in file.Lines.Where(l => l.IsOutfitDistribution))
            {
                // Parse the line to extract NPC FormKeys
                var npcFormKeys = ExtractNpcFormKeysFromLine(file, line, linkCache, npcByEditorId, npcByName);
                var outfitName = ExtractOutfitNameFromLine(line, linkCache);

                foreach (var npcFormKey in npcFormKeys)
                {
                    // Only track first occurrence (earlier files in load order)
                    if (!map.ContainsKey(npcFormKey))
                    {
                        map[npcFormKey] = (file.FileName, outfitName);
                    }
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Extracts NPC FormKeys from a distribution line.
    /// </summary>
    private List<FormKey> ExtractNpcFormKeysFromLine(
        DistributionFileViewModel file,
        DistributionLine line,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter> npcByEditorId,
        Dictionary<string, INpcGetter> npcByName)
    {
        var results = new List<FormKey>();

        if (file.TypeDisplay == "SkyPatcher")
        {
            // SkyPatcher format: filterByNpcs=ModKey|FormID,ModKey|FormID:outfitDefault=ModKey|FormID
            var trimmed = line.RawText.Trim();
            var filterByNpcsIndex = trimmed.IndexOf("filterByNpcs=", StringComparison.OrdinalIgnoreCase);
            
            if (filterByNpcsIndex >= 0)
            {
                var npcStart = filterByNpcsIndex + "filterByNpcs=".Length;
                var npcEnd = trimmed.IndexOf(':', npcStart);
                
                if (npcEnd > npcStart)
                {
                    var npcString = trimmed.Substring(npcStart, npcEnd - npcStart);
                    
                    foreach (var npcPart in npcString.Split(','))
                    {
                        var formKey = TryParseFormKeyLocal(npcPart.Trim());
                        if (formKey.HasValue)
                        {
                            results.Add(formKey.Value);
                        }
                    }
                }
            }
        }
        else if (file.TypeDisplay == "SPID")
        {
            // SPID format: Outfit = 0x800~ModKey|EditorID[,EditorID,...]
            var trimmed = line.RawText.Trim();
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex < 0) return results;

            var valuePart = trimmed.Substring(equalsIndex + 1).Trim();
            var tildeIndex = valuePart.IndexOf('~');
            if (tildeIndex < 0) return results;

            var rest = valuePart.Substring(tildeIndex + 1).Trim();
            var pipeIndex = rest.IndexOf('|');
            if (pipeIndex < 0) return results;

            var editorIdsString = rest.Substring(pipeIndex + 1).Trim();
            var npcIdentifiers = editorIdsString
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            foreach (var identifier in npcIdentifiers)
            {
                INpcGetter? npc = null;
                if (npcByEditorId.TryGetValue(identifier, out var npcById))
                {
                    npc = npcById;
                }
                else if (npcByName.TryGetValue(identifier, out var npcByNameMatch))
                {
                    npc = npcByNameMatch;
                }

                if (npc != null)
                {
                    results.Add(npc.FormKey);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Extracts outfit name from a distribution line.
    /// </summary>
    private string? ExtractOutfitNameFromLine(DistributionLine line, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        foreach (var formKeyString in line.OutfitFormKeys)
        {
            var formKey = TryParseFormKeyLocal(formKeyString);
            if (formKey.HasValue && linkCache.TryResolve<IOutfitGetter>(formKey.Value, out var outfit))
            {
                return outfit.EditorID ?? outfit.FormKey.ToString();
            }
        }
        return null;
    }

    /// <summary>
    /// Calculates a Z-prefixed filename that will load after all conflicting files.
    /// </summary>
    private string CalculateZPrefixedFileName(HashSet<string> conflictingFileNames)
    {
        if (string.IsNullOrWhiteSpace(NewFileName) || conflictingFileNames.Count == 0)
            return NewFileName;

        // Find the maximum number of leading Z's in conflicting filenames
        var maxZCount = 0;
        foreach (var fileName in conflictingFileNames)
        {
            var zCount = 0;
            foreach (var c in fileName)
            {
                if (c == 'Z' || c == 'z')
                    zCount++;
                else
                    break;
            }
            maxZCount = Math.Max(maxZCount, zCount);
        }

        // Add one more Z than the maximum
        var zPrefix = new string('Z', maxZCount + 1);
        
        // Remove any existing Z prefix from the new filename
        var baseName = NewFileName.TrimStart('Z', 'z');
        
        return zPrefix + baseName;
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

    private static FormKey? TryParseFormKeyLocal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        var pipeIndex = trimmed.IndexOf('|');
        if (pipeIndex < 0)
            return null;

        var modKeyString = trimmed.Substring(0, pipeIndex).Trim();
        var formIdString = trimmed.Substring(pipeIndex + 1).Trim();

        if (!ModKey.TryFromNameAndExtension(modKeyString, out var modKey))
            return null;

        formIdString = formIdString.Replace("0x", "").Replace("0X", "");
        if (!uint.TryParse(formIdString, System.Globalization.NumberStyles.HexNumber, null, out var formId))
            return null;

        return new FormKey(modKey, formId);
    }

    /// <summary>
    /// Creates a DistributionEntryViewModel from a DistributionEntry,
    /// resolving outfit and NPC references for proper UI binding.
    /// </summary>
    private DistributionEntryViewModel CreateEntryViewModel(DistributionEntry entry)
    {
        var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry);
        
        // Resolve outfit to AvailableOutfits instance for ComboBox binding
        ResolveEntryOutfit(entryVm);
        
        // Resolve NPCs from FormKeys
        var npcVms = ResolveNpcFormKeys(entry.NpcFormKeys);
        if (npcVms.Count > 0)
        {
            entryVm.SelectedNpcs = new ObservableCollection<NpcRecordViewModel>(npcVms);
            entryVm.UpdateEntryNpcs();
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
        _filteredNpcs.Clear();
        foreach (var npc in filtered)
        {
            _filteredNpcs.Add(npc);
        }
    }

    #endregion

    #region NPCs Tab Methods

    private async Task ScanNpcOutfitsAsync()
    {
        _logger.Debug("ScanNpcOutfitsAsync started");
        
        try
        {
            IsLoading = true;

            // Initialize MutagenService if not already initialized
            if (!_mutagenService.IsInitialized)
            {
                var dataPath = _settings.SkyrimDataPath;
                _logger.Debug("MutagenService not initialized, data path: {DataPath}", dataPath);
                
                if (string.IsNullOrWhiteSpace(dataPath))
                {
                    StatusMessage = "Please set the Skyrim data path in Settings before scanning NPC outfits.";
                    _logger.Warning("Skyrim data path is not set");
                    return;
                }

                if (!Directory.Exists(dataPath))
                {
                    StatusMessage = $"Skyrim data path does not exist: {dataPath}";
                    _logger.Warning("Skyrim data path does not exist: {DataPath}", dataPath);
                    return;
                }

                StatusMessage = "Initializing Skyrim environment...";
                _logger.Debug("Initializing MutagenService...");
                await _mutagenService.InitializeAsync(dataPath);
                _logger.Debug("MutagenService initialized successfully");
                this.RaisePropertyChanged(nameof(IsInitialized));
            }

            // Make sure we have distribution files loaded
            if (Files.Count == 0)
            {
                StatusMessage = "Scanning for distribution files...";
                _logger.Debug("No distribution files loaded, refreshing...");
                await RefreshAsync();
                _logger.Debug("Refresh complete, found {Count} files", Files.Count);
            }

            // Get the raw distribution files from the discovered files
            _logger.Debug("Building distribution file list from {Count} file view models", Files.Count);
            
            var distributionFiles = Files
                .Select(fvm => new DistributionFile(
                    fvm.FileName,
                    fvm.FullPath,
                    fvm.RelativePath,
                    fvm.TypeDisplay == "SPID" ? DistributionFileType.Spid : DistributionFileType.SkyPatcher,
                    fvm.Lines,
                    fvm.OutfitCount))
                .ToList();

            foreach (var file in distributionFiles)
            {
                _logger.Debug("Distribution file: {FileName} ({Type}), {LineCount} lines, {OutfitCount} outfit distributions",
                    file.FileName, file.Type, file.Lines.Count, file.OutfitDistributionCount);
            }

            // First, scan all NPCs with full filter data for proper SPID matching
            StatusMessage = "Scanning NPCs for filter matching...";
            _logger.Debug("Scanning NPCs with full filter data...");
            var npcFilterData = await _npcScanningService.ScanNpcsWithFilterDataAsync();
            _logger.Debug("Scanned {Count} NPCs with filter data", npcFilterData.Count);
            
            StatusMessage = $"Resolving outfit assignments from {distributionFiles.Count} files...";
            _logger.Debug("Calling ResolveNpcOutfitsWithFiltersAsync with {FileCount} files and {NpcCount} NPCs", 
                distributionFiles.Count, npcFilterData.Count);

            var assignments = await _npcOutfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(distributionFiles, npcFilterData);
            _logger.Debug("ResolveNpcOutfitsWithFiltersAsync returned {Count} assignments", assignments.Count);

            NpcOutfitAssignments.Clear();
            foreach (var assignment in assignments)
            {
                var vm = new NpcOutfitAssignmentViewModel(assignment);
                NpcOutfitAssignments.Add(vm);
            }

            // Update filtered list
            UpdateFilteredNpcOutfitAssignments();

            var conflictCount = assignments.Count(a => a.HasConflict);
            StatusMessage = $"Found {assignments.Count} NPCs with outfit distributions ({conflictCount} conflicts).";
            _logger.Information("Resolved {Count} NPC outfit assignments with {Conflicts} conflicts.", 
                assignments.Count, conflictCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to scan NPC outfits.");
            StatusMessage = $"Error scanning NPC outfits: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PreviewNpcOutfitAsync(NpcOutfitAssignmentViewModel? npcAssignment)
    {
        if (npcAssignment == null || !npcAssignment.FinalOutfitFormKey.HasValue)
        {
            StatusMessage = "No outfit to preview for this NPC.";
            return;
        }

        if (!_mutagenService.IsInitialized || _mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            StatusMessage = "Initialize Skyrim data path before previewing outfits.";
            return;
        }

        var outfitFormKey = npcAssignment.FinalOutfitFormKey.Value;
        if (!linkCache.TryResolve<IOutfitGetter>(outfitFormKey, out var outfit))
        {
            StatusMessage = $"Could not resolve outfit: {outfitFormKey}";
            return;
        }

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

    private void UpdateFilteredNpcOutfitAssignments()
    {
        IEnumerable<NpcOutfitAssignmentViewModel> filtered;

        if (string.IsNullOrWhiteSpace(NpcOutfitSearchText))
        {
            filtered = NpcOutfitAssignments;
        }
        else
        {
            var term = NpcOutfitSearchText.Trim().ToLowerInvariant();
            filtered = NpcOutfitAssignments.Where(a => 
                (a.DisplayName?.ToLowerInvariant().Contains(term) ?? false) ||
                (a.EditorId?.ToLowerInvariant().Contains(term) ?? false) ||
                (a.FinalOutfitEditorId?.ToLowerInvariant().Contains(term) ?? false) ||
                a.FormKeyString.ToLowerInvariant().Contains(term) ||
                a.ModDisplayName.ToLowerInvariant().Contains(term));
        }

        _filteredNpcOutfitAssignments.Clear();
        foreach (var assignment in filtered)
        {
            _filteredNpcOutfitAssignments.Add(assignment);
        }
    }

    private void UpdateSelectedNpcOutfitContents()
    {
        if (SelectedNpcAssignment == null || !SelectedNpcAssignment.FinalOutfitFormKey.HasValue)
        {
            SelectedNpcOutfitContents = string.Empty;
            return;
        }

        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            SelectedNpcOutfitContents = "LinkCache not available";
            return;
        }

        var outfitFormKey = SelectedNpcAssignment.FinalOutfitFormKey.Value;
        if (!linkCache.TryResolve<IOutfitGetter>(outfitFormKey, out var outfit))
        {
            SelectedNpcOutfitContents = $"Could not resolve outfit: {outfitFormKey}";
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Outfit: {outfit.EditorID ?? outfit.FormKey.ToString()}");
        sb.AppendLine();

        var armorPieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
        if (armorPieces.Count == 0)
        {
            sb.AppendLine("(No armor pieces)");
        }
        else
        {
            sb.AppendLine("Armor Pieces:");
            foreach (var armor in armorPieces)
            {
                var armorName = armor.EditorID ?? armor.FormKeyString;
                sb.AppendLine($"  - {armorName}");
            }
        }

        SelectedNpcOutfitContents = sb.ToString();
    }

    #endregion
}
