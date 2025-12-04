using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Boutique.Models;
using Boutique.Services;
using Microsoft.Win32;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Order;
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
    private ObservableCollection<NpcRecordViewModel> _availableNpcs = new();
    private ObservableCollection<IOutfitGetter> _availableOutfits = new();
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

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        PreviewLineCommand = ReactiveCommand.CreateFromTask<DistributionLine>(PreviewLineAsync, 
            this.WhenAnyValue(vm => vm.IsLoading).Select(loading => !loading));
        
        AddDistributionEntryCommand = ReactiveCommand.Create(AddDistributionEntry);
        RemoveDistributionEntryCommand = ReactiveCommand.Create<DistributionEntryViewModel>(RemoveDistributionEntry);
        SelectEntryCommand = ReactiveCommand.Create<DistributionEntryViewModel>(SelectEntry);
        AddSelectedNpcsToEntryCommand = ReactiveCommand.Create(AddSelectedNpcsToEntry,
            this.WhenAnyValue(vm => vm.DistributionEntries.Count, count => count > 0));
        SaveDistributionFileCommand = ReactiveCommand.CreateFromTask(SaveDistributionFileAsync,
            this.WhenAnyValue(
                vm => vm.DistributionEntries.Count, 
                vm => vm.DistributionFilePath,
                vm => vm.IsCreatingNewFile,
                vm => vm.NewFileName,
                (count, path, isNew, newName) => 
                    count > 0 && 
                    (!string.IsNullOrWhiteSpace(path) || (isNew && !string.IsNullOrWhiteSpace(newName)))));
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
                    LoadAvailableOutfits();
                }
            });

        this.WhenAnyValue(vm => vm.NpcSearchText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FilteredNpcs)));
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
        private set => this.RaiseAndSetIfChanged(ref _distributionEntries, value);
    }

    public DistributionEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set => this.RaiseAndSetIfChanged(ref _selectedEntry, value);
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

    public IEnumerable<NpcRecordViewModel> FilteredNpcs
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NpcSearchText))
                return AvailableNpcs;

            var term = NpcSearchText.Trim().ToLowerInvariant();
            return AvailableNpcs.Where(npc => npc.MatchesSearch(term));
        }
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
            if (!TryResolveOutfit(keyString, linkCache, ref cachedOutfits, out var outfit, out var label))
                continue;

            var armorPieces = GatherArmorPieces(outfit, linkCache);
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

    private bool TryResolveOutfit(
        string identifier,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ref List<IOutfitGetter>? cachedOutfits,
        [NotNullWhen(true)] out IOutfitGetter? outfit,
        out string label)
    {
        outfit = null;
        label = string.Empty;

        if (TryCreateFormKey(identifier, out var formKey) &&
            linkCache.TryResolve<IOutfitGetter>(formKey, out var resolvedFromFormKey))
        {
            outfit = resolvedFromFormKey;
            label = outfit.EditorID ?? formKey.ToString();
            return true;
        }

        if (TryResolveOutfitByEditorId(identifier, linkCache, ref cachedOutfits, out var resolvedFromEditorId))
        {
            outfit = resolvedFromEditorId;
            label = outfit.EditorID ?? identifier;
            return true;
        }

        return false;
    }

    private bool TryResolveOutfitByEditorId(
        string identifier,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ref List<IOutfitGetter>? cachedOutfits,
        [NotNullWhen(true)] out IOutfitGetter? outfit)
    {
        outfit = null;

        if (!TryParseEditorIdReference(identifier, out var modKey, out var editorId))
            return false;

        cachedOutfits ??= linkCache.PriorityOrder.WinningOverrides<IOutfitGetter>().ToList();

        IEnumerable<IOutfitGetter> query = cachedOutfits
            .Where(o => string.Equals(o.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

        if (modKey.HasValue)
            query = query.Where(o => o.FormKey.ModKey == modKey.Value);

        outfit = query.FirstOrDefault();
        return outfit != null;
    }

    private static bool TryParseEditorIdReference(string identifier, out ModKey? modKey, out string editorId)
    {
        modKey = null;
        editorId = string.Empty;

        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        var trimmed = identifier.Trim();
        string? modCandidate = null;
        string? editorCandidate = null;

        var pipeIndex = trimmed.IndexOf('|');
        var tildeIndex = trimmed.IndexOf('~');

        if (pipeIndex >= 0)
        {
            var firstPart = trimmed[..pipeIndex].Trim();
            var secondPart = trimmed[(pipeIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(secondPart) && TryParseModKey(secondPart, out var modFromSecond))
            {
                modKey = modFromSecond;
                editorCandidate = firstPart;
            }
            else if (!string.IsNullOrWhiteSpace(firstPart) && TryParseModKey(firstPart, out var modFromFirst))
            {
                modKey = modFromFirst;
                editorCandidate = secondPart;
            }
            else
            {
                editorCandidate = firstPart;
                modCandidate = secondPart;
            }
        }
        else if (tildeIndex >= 0)
        {
            var firstPart = trimmed[..tildeIndex].Trim();
            var secondPart = trimmed[(tildeIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(secondPart) && TryParseModKey(secondPart, out var modFromSecond))
            {
                modKey = modFromSecond;
                editorCandidate = firstPart;
            }
            else if (!string.IsNullOrWhiteSpace(firstPart) && TryParseModKey(firstPart, out var modFromFirst))
            {
                modKey = modFromFirst;
                editorCandidate = secondPart;
            }
            else
            {
                editorCandidate = firstPart;
                modCandidate = secondPart;
            }
        }
        else
        {
            editorCandidate = trimmed;
        }

        if (!modKey.HasValue && !string.IsNullOrWhiteSpace(modCandidate) && TryParseModKey(modCandidate, out var parsedMod))
            modKey = parsedMod;

        if (string.IsNullOrWhiteSpace(editorCandidate))
            return false;

        editorId = editorCandidate;
        return true;
    }

    private static List<ArmorRecordViewModel> GatherArmorPieces(
        IOutfitGetter outfit,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var pieces = new List<ArmorRecordViewModel>();

        var items = outfit.Items ?? Array.Empty<IFormLinkGetter<IOutfitTargetGetter>>();

        foreach (var itemLink in items)
        {
            if (itemLink == null)
                continue;

            var targetKeyNullable = itemLink.FormKeyNullable;
            if (!targetKeyNullable.HasValue || targetKeyNullable.Value == FormKey.Null)
                continue;

            var targetKey = targetKeyNullable.Value;

            if (!linkCache.TryResolve<IItemGetter>(targetKey, out var itemRecord))
                continue;

            if (itemRecord is not IArmorGetter armor)
                continue;

            var vm = new ArmorRecordViewModel(armor, linkCache);
            pieces.Add(vm);
        }

        return pieces;
    }

    private static bool TryCreateFormKey(string text, out FormKey formKey)
    {
        formKey = FormKey.Null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        string modPart;
        string formIdPart;

        if (trimmed.Contains('|'))
        {
            var parts = trimmed.Split('|', 2);
            modPart = parts[0].Trim();
            formIdPart = parts[1].Trim();
        }
        else if (trimmed.Contains('~'))
        {
            var parts = trimmed.Split('~', 2);
            formIdPart = parts[0].Trim();
            modPart = parts[1].Trim();
        }
        else
        {
            return false;
        }

        if (!TryParseModKey(modPart, out var modKey))
            return false;

        if (!TryParseFormId(formIdPart, out var id))
            return false;

        formKey = new FormKey(modKey, id);
        return true;
    }

    private static bool TryParseModKey(string input, out ModKey modKey)
    {
        try
        {
            modKey = ModKey.FromNameAndExtension(input);
            return true;
        }
        catch
        {
            modKey = ModKey.Null;
            return false;
        }
    }

    private static bool TryParseFormId(string text, out uint id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];

        return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }

    private void AddDistributionEntry()
    {
        var entry = new DistributionEntry();
        var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry);
        DistributionEntries.Add(entryVm);
        SelectedEntry = entryVm; // Select the newly created entry
        _logger.Debug("Added new distribution entry.");
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

        // Get all NPCs that are checked
        var selectedNpcs = FilteredNpcs
            .Where(npc => npc.IsSelected)
            .ToList();

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
        SelectedEntry = entryVm;
        _logger.Debug("Selected distribution entry: {Outfit}", entryVm.SelectedOutfit?.EditorID ?? "(No outfit)");
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

            foreach (var entry in entries)
            {
                var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry);
                
                // Resolve NPCs from FormKeys - try to match with AvailableNpcs first
                var npcVms = new List<NpcRecordViewModel>();
                foreach (var npcFormKey in entry.NpcFormKeys)
                {
                    // Try to find in AvailableNpcs first
                    var existingNpc = AvailableNpcs.FirstOrDefault(npc => npc.FormKey == npcFormKey);
                    if (existingNpc != null)
                    {
                        existingNpc.IsSelected = true;
                        npcVms.Add(existingNpc);
                    }
                    else if (_mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
                    {
                        // If not in AvailableNpcs, resolve from LinkCache
                        if (linkCache.TryResolve<INpcGetter>(npcFormKey, out var npc))
                        {
                            var npcRecord = new NpcRecord(
                                npc.FormKey,
                                npc.EditorID,
                                npc.Name?.String,
                                npc.FormKey.ModKey);
                            var npcVm = new NpcRecordViewModel(npcRecord);
                            npcVm.IsSelected = true;
                            npcVms.Add(npcVm);
                        }
                    }
                }
                
                if (npcVms.Count > 0)
                {
                    entryVm.SelectedNpcs = new ObservableCollection<NpcRecordViewModel>(npcVms);
                    entryVm.UpdateEntryNpcs();
                }

                DistributionEntries.Add(entryVm);
            }

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

    private void LoadAvailableOutfits()
    {
        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            AvailableOutfits.Clear();
            return;
        }

        try
        {
            var outfits = linkCache.PriorityOrder.WinningOverrides<IOutfitGetter>().ToList();
            AvailableOutfits = new ObservableCollection<IOutfitGetter>(outfits);
            _logger.Debug("Loaded {Count} available outfits.", outfits.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load available outfits.");
            AvailableOutfits.Clear();
        }
    }
}
