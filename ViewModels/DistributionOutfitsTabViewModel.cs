using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Boutique.ViewModels;

public class DistributionOutfitsTabViewModel : ReactiveObject
{
    private readonly NpcScanningService _npcScanningService;
    private readonly NpcOutfitResolutionService _npcOutfitResolutionService;
    private readonly ArmorPreviewService _armorPreviewService;
    private readonly MutagenService _mutagenService;
    private readonly GameDataCacheService _cache;
    private readonly SettingsViewModel _settings;
    private readonly ILogger _logger;

    public DistributionOutfitsTabViewModel(
        NpcScanningService npcScanningService,
        NpcOutfitResolutionService npcOutfitResolutionService,
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
        GameDataCacheService cache,
        SettingsViewModel settings,
        ILogger logger)
    {
        _npcScanningService = npcScanningService;
        _npcOutfitResolutionService = npcOutfitResolutionService;
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _cache = cache;
        _settings = settings;
        _logger = logger.ForContext<DistributionOutfitsTabViewModel>();

        var notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);
        LoadOutfitsCommand = ReactiveCommand.CreateFromTask(LoadOutfitsAsync, notLoading);
        PreviewOutfitCommand = ReactiveCommand.CreateFromTask<OutfitRecordViewModel>(PreviewOutfitAsync, notLoading);

        // Outfits tab search filtering
        this.WhenAnyValue(vm => vm.OutfitSearchText)
            .Subscribe(_ => UpdateFilteredOutfits());

        // Vanilla outfit filtering
        this.WhenAnyValue(vm => vm.HideVanillaOutfits)
            .Subscribe(_ => UpdateFilteredOutfits());

        // Update NPC assignments when selection changes
        this.WhenAnyValue(vm => vm.SelectedOutfit)
            .Subscribe(_ => UpdateSelectedOutfitNpcAssignments());
    }

    [Reactive]
    public bool IsLoading { get; private set; }

    [Reactive]
    public string StatusMessage { get; private set; } = string.Empty;

    [Reactive]
    public ObservableCollection<OutfitRecordViewModel> Outfits { get; private set; } = [];

    public OutfitRecordViewModel? SelectedOutfit
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

    [Reactive]
    public string OutfitSearchText { get; set; } = string.Empty;

    [Reactive]
    public bool HideVanillaOutfits { get; set; }

    [Reactive]
    public ObservableCollection<OutfitRecordViewModel> FilteredOutfits { get; private set; } = [];

    [Reactive]
    public ObservableCollection<NpcOutfitAssignmentViewModel> SelectedOutfitNpcAssignments { get; private set; } = [];

    public ReactiveCommand<Unit, Unit> LoadOutfitsCommand { get; }

    public ReactiveCommand<OutfitRecordViewModel, Unit> PreviewOutfitCommand { get; }

    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    public bool IsInitialized => _mutagenService.IsInitialized;

    private IReadOnlyList<NpcOutfitAssignment>? _npcAssignments;

    /// <summary>
    /// Gets distribution files from the cache for NPC outfit resolution.
    /// </summary>
    private IReadOnlyList<DistributionFileViewModel> DistributionFiles => _cache.AllDistributionFiles;

    public async Task LoadOutfitsAsync()
    {
        _logger.Debug("LoadOutfitsAsync started");

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
                    StatusMessage = "Please set the Skyrim data path in Settings before loading outfits.";
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

            if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
            {
                StatusMessage = "LinkCache not available.";
                _logger.Warning("LinkCache not available");
                return;
            }

            // Load all outfits from the load order
            StatusMessage = "Loading outfits from load order...";
            _logger.Debug("Loading outfits from load order...");
            var outfits = await Task.Run(() =>
                linkCache.WinningOverrides<IOutfitGetter>().ToList());
            _logger.Debug("Loaded {Count} outfits from load order", outfits.Count);

            // Load NPC assignments if distribution files are available
            if (DistributionFiles.Count > 0)
            {
                StatusMessage = "Resolving NPC outfit assignments...";
                _logger.Debug("Resolving NPC outfit assignments from {Count} files", DistributionFiles.Count);

                var distributionFiles = DistributionFiles
                    .Select(fvm => new DistributionFile(
                        fvm.FileName,
                        fvm.FullPath,
                        fvm.RelativePath,
                        fvm.TypeDisplay == "SPID" ? DistributionFileType.Spid : DistributionFileType.SkyPatcher,
                        fvm.Lines,
                        fvm.OutfitCount))
                    .ToList();

                var npcFilterData = await _npcScanningService.ScanNpcsWithFilterDataAsync();
                _npcAssignments = await _npcOutfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(distributionFiles, npcFilterData);
                _logger.Debug("Resolved {Count} NPC outfit assignments", _npcAssignments.Count);

                // Update counts now that we have both outfits and assignments
                UpdateOutfitNpcCounts();
            }

            // Create view models for outfits
            Outfits.Clear();
            foreach (var outfit in outfits)
            {
                var vm = new OutfitRecordViewModel(outfit);
                Outfits.Add(vm);
            }

            // Update filtered list
            UpdateFilteredOutfits();

            // Update NPC counts for each outfit (after both outfits and assignments are loaded)
            if (_npcAssignments != null)
            {
                UpdateOutfitNpcCounts();
            }

            StatusMessage = $"Loaded {outfits.Count} outfits from load order.";
            _logger.Information("Loaded {Count} outfits from load order.", outfits.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load outfits.");
            StatusMessage = $"Error loading outfits: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PreviewOutfitAsync(OutfitRecordViewModel? outfitVm)
    {
        if (outfitVm == null)
        {
            StatusMessage = "No outfit selected for preview.";
            return;
        }

        if (!_mutagenService.IsInitialized || _mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            StatusMessage = "Initialize Skyrim data path before previewing outfits.";
            return;
        }

        var outfit = outfitVm.Outfit;
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

    private void UpdateFilteredOutfits()
    {
        IEnumerable<OutfitRecordViewModel> filtered = Outfits;

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(OutfitSearchText))
        {
            var term = OutfitSearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(o => o.MatchesSearch(term));
        }

        // Filter out outfits where all NPCs have it as their default (no distribution changed anything)
        if (HideVanillaOutfits)
        {
            filtered = filtered.Where(o => !IsVanillaDistribution(o.FormKey));
        }

        FilteredOutfits.Clear();
        foreach (var outfit in filtered)
        {
            FilteredOutfits.Add(outfit);
        }
    }

    /// <summary>
    /// Returns true if this outfit is only used as default outfits (no distribution changed any NPC to use it).
    /// An outfit is a "vanilla distribution" if all NPCs whose final outfit is this one also have it as their default.
    /// </summary>
    private bool IsVanillaDistribution(FormKey outfitFormKey)
    {
        if (_npcAssignments == null || _npcAssignments.Count == 0)
            return false; // If no assignments loaded, don't filter anything

        // Find all NPCs whose final outfit is this one
        var npcsWithThisOutfit = _npcAssignments
            .Where(a => a.FinalOutfitFormKey == outfitFormKey)
            .ToList();

        if (npcsWithThisOutfit.Count == 0)
            return true; // No NPCs use this outfit, consider it vanilla

        // Check if ALL of these NPCs have this outfit as their default
        foreach (var assignment in npcsWithThisOutfit)
        {
            if (!_cache.NpcsByFormKey.TryGetValue(assignment.NpcFormKey, out var npcData))
                continue; // Can't determine, assume not vanilla

            // If this NPC's default outfit is different from their final outfit, this is NOT a vanilla distribution
            if (!npcData.DefaultOutfitFormKey.HasValue || npcData.DefaultOutfitFormKey.Value != outfitFormKey)
                return false;
        }

        // All NPCs with this outfit have it as their default - it's a vanilla distribution
        return true;
    }

    private void UpdateSelectedOutfitNpcAssignments()
    {
        SelectedOutfitNpcAssignments.Clear();

        if (SelectedOutfit == null || _npcAssignments == null)
        {
            _logger.Debug(
                "UpdateSelectedOutfitNpcAssignments: SelectedOutfit={SelectedOutfit}, _npcAssignments={NpcAssignmentsCount}",
                SelectedOutfit?.EditorID ?? "null",
                _npcAssignments?.Count ?? 0);
            return;
        }

        var outfitFormKey = SelectedOutfit.FormKey;
        _logger.Debug(
            "UpdateSelectedOutfitNpcAssignments: Looking for outfit {OutfitFormKey} ({EditorID})",
            outfitFormKey, SelectedOutfit.EditorID);

        // Find all NPCs that have this outfit assigned
        // Check if the outfit is the final outfit OR if it appears in any distribution targeting this NPC
        var matchingAssignments = _npcAssignments
            .Where(assignment =>
            {
                // Check if this outfit is the final resolved outfit for this NPC
                if (assignment.FinalOutfitFormKey == outfitFormKey)
                {
                    _logger.Debug("  Match: NPC {NpcFormKey} has this outfit as final outfit", assignment.NpcFormKey);
                    return true;
                }

                // Check if this outfit appears in any distribution targeting this NPC
                // This includes distributions that might not be the winner but still target this NPC
                var hasDistribution = assignment.Distributions.Any(d => d.OutfitFormKey == outfitFormKey);
                if (hasDistribution)
                {
                    _logger.Debug("  Match: NPC {NpcFormKey} has this outfit in distributions", assignment.NpcFormKey);
                }

                return hasDistribution;
            })
            .ToList();

        _logger.Debug("UpdateSelectedOutfitNpcAssignments: Found {Count} matching NPCs", matchingAssignments.Count);

        foreach (var assignment in matchingAssignments)
        {
            var vm = new NpcOutfitAssignmentViewModel(assignment);
            SelectedOutfitNpcAssignments.Add(vm);
        }
    }

    private void UpdateOutfitNpcCounts()
    {
        if (_npcAssignments == null)
        {
            foreach (var outfit in Outfits)
            {
                outfit.NpcCount = 0;
            }

            return;
        }

        // Count unique NPCs for each outfit
        // Use HashSet to track which NPCs we've already counted for each outfit
        var outfitNpcSets = new Dictionary<FormKey, HashSet<FormKey>>();

        foreach (var assignment in _npcAssignments)
        {
            // Count NPCs where this outfit is the final resolved outfit
            var finalOutfitFormKey = assignment.FinalOutfitFormKey;
            if (finalOutfitFormKey.HasValue)
            {
                if (!outfitNpcSets.ContainsKey(finalOutfitFormKey.Value))
                    outfitNpcSets[finalOutfitFormKey.Value] = [];
                outfitNpcSets[finalOutfitFormKey.Value].Add(assignment.NpcFormKey);
            }

            // Also count NPCs where this outfit appears in distributions (even if not final)
            foreach (var dist in assignment.Distributions)
            {
                if (!outfitNpcSets.ContainsKey(dist.OutfitFormKey))
                    outfitNpcSets[dist.OutfitFormKey] = [];

                // Add this NPC to the set for this outfit (HashSet automatically handles duplicates)
                outfitNpcSets[dist.OutfitFormKey].Add(assignment.NpcFormKey);
            }
        }

        // Update counts on outfit view models
        foreach (var outfit in Outfits)
        {
            if (outfitNpcSets.TryGetValue(outfit.FormKey, out var npcSet))
            {
                outfit.NpcCount = npcSet.Count;
            }
            else
            {
                outfit.NpcCount = 0;
            }
        }
    }
}
