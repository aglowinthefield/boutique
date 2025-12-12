using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Boutique.ViewModels;

public class DistributionNpcsTabViewModel : ReactiveObject
{
    private readonly NpcScanningService _npcScanningService;
    private readonly NpcOutfitResolutionService _npcOutfitResolutionService;
    private readonly DistributionDiscoveryService _discoveryService;
    private readonly ArmorPreviewService _armorPreviewService;
    private readonly MutagenService _mutagenService;
    private readonly SettingsViewModel _settings;
    private readonly ILogger _logger;

    public DistributionNpcsTabViewModel(
        NpcScanningService npcScanningService,
        NpcOutfitResolutionService npcOutfitResolutionService,
        DistributionDiscoveryService discoveryService,
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
        SettingsViewModel settings,
        ILogger logger)
    {
        _npcScanningService = npcScanningService;
        _npcOutfitResolutionService = npcOutfitResolutionService;
        _discoveryService = discoveryService;
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _settings = settings;
        _logger = logger.ForContext<DistributionNpcsTabViewModel>();

        var notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);
        ScanNpcOutfitsCommand = ReactiveCommand.CreateFromTask(ScanNpcOutfitsAsync, notLoading);
        PreviewNpcOutfitCommand = ReactiveCommand.CreateFromTask<NpcOutfitAssignmentViewModel>(PreviewNpcOutfitAsync, notLoading);

        // NPCs tab search filtering
        this.WhenAnyValue(vm => vm.NpcOutfitSearchText)
            .Subscribe(_ => UpdateFilteredNpcOutfitAssignments());
        
        // Update outfit contents when selection changes
        this.WhenAnyValue(vm => vm.SelectedNpcAssignment)
            .Subscribe(_ => UpdateSelectedNpcOutfitContents());
    }

    [Reactive] public bool IsLoading { get; private set; }

    [Reactive] public string StatusMessage { get; private set; } = string.Empty;

    [Reactive] public ObservableCollection<NpcOutfitAssignmentViewModel> NpcOutfitAssignments { get; private set; } = new();

    public NpcOutfitAssignmentViewModel? SelectedNpcAssignment
    {
        get => field;
        set
        {
            // Clear previous selection
            field?.IsSelected = false;
            
            this.RaiseAndSetIfChanged(ref field, value);
            
            // Set new selection
            if (value != null)
            {
                value.IsSelected = true;
            }
        }
    }

    [Reactive] public string NpcOutfitSearchText { get; set; } = string.Empty;

    [Reactive] public ObservableCollection<NpcOutfitAssignmentViewModel> FilteredNpcOutfitAssignments { get; private set; } = new();

    [Reactive] public string SelectedNpcOutfitContents { get; private set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> ScanNpcOutfitsCommand { get; }

    public ReactiveCommand<NpcOutfitAssignmentViewModel, Unit> PreviewNpcOutfitCommand { get; }

    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    public bool IsInitialized => _mutagenService.IsInitialized;

    /// <summary>
    /// Sets the distribution files from the Files tab. This allows the NPCs tab to work with
    /// the files discovered by the Files tab without duplicating the discovery logic.
    /// </summary>
    public void SetDistributionFiles(IReadOnlyList<DistributionFileViewModel> files)
    {
        // This method allows the main ViewModel to pass files from Files tab
        // The files are used in ScanNpcOutfitsAsync
    }

    private IReadOnlyList<DistributionFileViewModel>? _distributionFiles;

    /// <summary>
    /// Internal method to set distribution files for scanning.
    /// Called by the main ViewModel when files are available.
    /// </summary>
    internal void SetDistributionFilesInternal(IReadOnlyList<DistributionFileViewModel> files)
    {
        _distributionFiles = files;
    }

    public async Task ScanNpcOutfitsAsync()
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
            IReadOnlyList<DistributionFileViewModel> files;
            if (_distributionFiles != null && _distributionFiles.Count > 0)
            {
                files = _distributionFiles;
            }
            else
            {
                // Fallback: discover files ourselves if not provided
                StatusMessage = "Scanning for distribution files...";
                _logger.Debug("No distribution files provided, discovering...");
                var discovered = await _discoveryService.DiscoverAsync(_settings.SkyrimDataPath);
                var outfitFiles = discovered
                    .Where(file => file.OutfitDistributionCount > 0)
                    .ToList();
                files = outfitFiles
                    .Select(file => new DistributionFileViewModel(file))
                    .ToList();
                _logger.Debug("Discovered {Count} files", files.Count);
            }

            // Get the raw distribution files from the discovered files
            _logger.Debug("Building distribution file list from {Count} file view models", files.Count);
            
            var distributionFiles = files
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

        FilteredNpcOutfitAssignments.Clear();
        foreach (var assignment in filtered)
        {
            FilteredNpcOutfitAssignments.Add(assignment);
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
}

