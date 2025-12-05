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

public class DistributionViewModel : ReactiveObject
{
    private readonly IDistributionDiscoveryService _discoveryService;
    private readonly IDistributionFileWriterService _fileWriterService;
    private readonly INpcScanningService _npcScanningService;
    private readonly ILogger _logger;
    private readonly IMutagenService _mutagenService;
    private readonly IArmorPreviewService _armorPreviewService;
    private readonly SettingsViewModel _settings;

    private ObservableCollection<DistributionFileViewModel> _files = new();
    private ObservableCollection<DistributionEntryViewModel> _distributionEntries = new();
    
    private void OnDistributionEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _logger.Debug("OnDistributionEntriesChanged: Action={Action}, NewItems={NewCount}, OldItems={OldCount}", 
            e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);
        
        // Raise PropertyChanged synchronously - this is fast and necessary for bindings
        this.RaisePropertyChanged(nameof(DistributionEntriesCount));
        
        // Subscribe to property changes on new entries
        if (e.NewItems != null)
        {
            foreach (DistributionEntryViewModel entry in e.NewItems)
            {
                entry.WhenAnyValue(evm => evm.SelectedOutfit)
                    .Subscribe(_ => UpdateDistributionPreview());
                entry.WhenAnyValue(evm => evm.SelectedNpcs)
                    .Subscribe(_ => UpdateDistributionPreview());
                entry.SelectedNpcs.CollectionChanged += (s, args) => UpdateDistributionPreview();
            }
        }
        
        // Unsubscribe from removed entries
        if (e.OldItems != null)
        {
            // No need to unsubscribe - entries will be garbage collected
        }
        
        // Update preview whenever entries change
        UpdateDistributionPreview();
        
        _logger.Debug("OnDistributionEntriesChanged completed");
    }
    
    private int DistributionEntriesCount => _distributionEntries.Count;
    private ObservableCollection<NpcRecordViewModel> _availableNpcs = new();
    private ObservableCollection<IOutfitGetter> _availableOutfits = new();
    private bool _outfitsLoaded;
    private ObservableCollection<DistributionFileSelectionItem> _availableDistributionFiles = new();
    private bool _isLoading;
    private bool _isEditMode;
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

    public DistributionViewModel(
        IDistributionDiscoveryService discoveryService,
        IDistributionFileWriterService fileWriterService,
        INpcScanningService npcScanningService,
        SettingsViewModel settings,
        IArmorPreviewService armorPreviewService,
        IMutagenService mutagenService,
        ILogger logger)
    {
        _discoveryService = discoveryService;
        _fileWriterService = fileWriterService;
        _npcScanningService = npcScanningService;
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

        _settings.WhenAnyValue(x => x.SkyrimDataPath)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(DataPath));
                if (IsCreatingNewFile)
                {
                    UpdateDistributionFilePathFromNewFileName();
                }
            });

        this.WhenAnyValue(vm => vm.IsEditMode)
            .Subscribe(editMode =>
            {
                if (editMode)
                {
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
                }
            });

        this.WhenAnyValue(vm => vm.NpcSearchText)
            .Throttle(TimeSpan.FromMilliseconds(150))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => UpdateFilteredNpcs());
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

    public bool IsEditMode
    {
        get => _isEditMode;
        set => this.RaiseAndSetIfChanged(ref _isEditMode, value);
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
        private set => this.RaiseAndSetIfChanged(ref _isCreatingNewFile, value);
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

    public bool IsInitialized => _mutagenService.IsInitialized;

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

        // Check if file exists and prompt for overwrite confirmation (before showing loading state)
        if (File.Exists(DistributionFilePath))
        {
            var result = System.Windows.MessageBox.Show(
                $"The file '{Path.GetFileName(DistributionFilePath)}' already exists.\n\nDo you want to overwrite it?",
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

            await _fileWriterService.WriteDistributionFileAsync(DistributionFilePath, entries);

            StatusMessage = $"Successfully saved distribution file: {Path.GetFileName(DistributionFilePath)}";
            _logger.Information("Saved distribution file: {FilePath}", DistributionFilePath);

            // Refresh the file list
            await RefreshAsync();
            
            // If we saved a new file, select it in the dropdown
            if (IsCreatingNewFile)
            {
                var savedFile = Files.FirstOrDefault(f => 
                    string.Equals(f.FullPath, DistributionFilePath, StringComparison.OrdinalIgnoreCase));
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

            DistributionEntries.Clear();

            // Ensure outfits are loaded before creating entries so ComboBox bindings work
            await LoadAvailableOutfitsAsync();

            foreach (var entry in entries)
            {
                var entryVm = CreateEntryViewModel(entry);
                DistributionEntries.Add(entryVm);
                // Note: Subscriptions for preview updates are handled by OnDistributionEntriesChanged
            }
            
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
}
