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
        CopyOutfitCommand = ReactiveCommand.CreateFromTask<OutfitRecordViewModel>(CopyOutfit, notLoading);

        // Outfits tab search filtering
        this.WhenAnyValue(vm => vm.OutfitSearchText)
            .Subscribe(_ => UpdateFilteredOutfits());

        // Vanilla outfit filtering
        this.WhenAnyValue(vm => vm.HideVanillaOutfits)
            .Subscribe(_ => UpdateFilteredOutfits());

        // Update NPC assignments when selection changes (async to avoid UI freeze)
        this.WhenAnyValue(vm => vm.SelectedOutfit)
            .Subscribe(async _ => await UpdateSelectedOutfitNpcAssignmentsAsync());
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

    public ReactiveCommand<OutfitRecordViewModel, Unit> CopyOutfitCommand { get; }

    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    /// <summary>
    /// Event raised when an outfit is copied to create a distribution entry.
    /// </summary>
    public event EventHandler<CopiedOutfit>? OutfitCopied;

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

            // Use cached NPC assignments from startup (already calculated correctly)
            if (_cache.AllNpcOutfitAssignments.Count > 0)
            {
                _npcAssignments = _cache.AllNpcOutfitAssignments
                    .Select(vm => vm.Assignment)
                    .ToList();
                _logger.Debug("Using {Count} cached NPC outfit assignments", _npcAssignments.Count);

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

        if (!_mutagenService.IsInitialized || _mutagenService.LinkCache is not { } linkCache)
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

    private Task CopyOutfit(OutfitRecordViewModel outfitVm)
    {
        var outfit = outfitVm.Outfit;
        var editorId = outfit.EditorID ?? outfit.FormKey.ToString();

        var copiedOutfit = new CopiedOutfit
        {
            OutfitFormKey = outfit.FormKey,
            OutfitEditorId = editorId,
            Description = editorId
        };

        OutfitCopied?.Invoke(this, copiedOutfit);
        _logger.Debug("Copied outfit {EditorId} to Create tab", editorId);

        return Task.CompletedTask;
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

    private async Task UpdateSelectedOutfitNpcAssignmentsAsync()
    {
        SelectedOutfitNpcAssignments.Clear();

        if (SelectedOutfit == null || _npcAssignments == null)
        {
            return;
        }

        var outfitFormKey = SelectedOutfit.FormKey;
        var selectedOutfitRef = SelectedOutfit;

        // Run filtering on background thread to avoid UI freeze
        var matchingAssignments = await Task.Run(() =>
        {
            // Find all NPCs that have this outfit as their final resolved outfit
            return _npcAssignments
                .Where(assignment => assignment.FinalOutfitFormKey == outfitFormKey)
                .Select(assignment => new NpcOutfitAssignmentViewModel(assignment))
                .ToList();
        });

        // Check if selection changed while we were processing
        if (SelectedOutfit != selectedOutfitRef)
            return;

        foreach (var vm in matchingAssignments)
        {
            SelectedOutfitNpcAssignments.Add(vm);
        }

        _logger.Debug(
            "UpdateSelectedOutfitNpcAssignmentsAsync: Found {Count} NPCs with outfit {EditorID}",
            matchingAssignments.Count, selectedOutfitRef.EditorID);
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

        // Count unique NPCs for each outfit based on their FINAL resolved outfit only
        // We don't count distributions because that would include ESP defaults for every NPC
        var outfitNpcSets = new Dictionary<FormKey, HashSet<FormKey>>();

        foreach (var assignment in _npcAssignments)
        {
            // Only count NPCs where this outfit is the FINAL resolved outfit
            var finalOutfitFormKey = assignment.FinalOutfitFormKey;
            if (finalOutfitFormKey.HasValue)
            {
                if (!outfitNpcSets.ContainsKey(finalOutfitFormKey.Value))
                    outfitNpcSets[finalOutfitFormKey.Value] = [];
                outfitNpcSets[finalOutfitFormKey.Value].Add(assignment.NpcFormKey);
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
