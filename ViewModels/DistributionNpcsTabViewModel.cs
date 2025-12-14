using System.Collections.ObjectModel;
using System.Globalization;
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

public class DistributionNpcsTabViewModel : ReactiveObject
{
    private readonly ArmorPreviewService _armorPreviewService;
    private readonly MutagenService _mutagenService;
    private readonly GameDataCacheService _cache;
    private readonly ILogger _logger;

    public DistributionNpcsTabViewModel(
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
        GameDataCacheService cache,
        ILogger logger)
    {
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _cache = cache;
        _logger = logger.ForContext<DistributionNpcsTabViewModel>();

        var notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);
        ScanNpcOutfitsCommand = ReactiveCommand.CreateFromTask(ScanNpcOutfitsAsync, notLoading);
        PreviewNpcOutfitCommand = ReactiveCommand.CreateFromTask<NpcOutfitAssignmentViewModel>(PreviewNpcOutfitAsync, notLoading);
        ClearFiltersCommand = ReactiveCommand.Create(ClearFilters);

        // NPCs tab search filtering - combine text search with SPID filters
        this.WhenAnyValue(vm => vm.NpcOutfitSearchText)
            .Subscribe(_ => UpdateFilteredNpcOutfitAssignments());

        // Update outfit contents when selection changes
        this.WhenAnyValue(vm => vm.SelectedNpcAssignment)
            .Subscribe(_ => UpdateSelectedNpcOutfitContents());

        // Subscribe to filter changes
        this.WhenAnyValue(
                vm => vm.SelectedGenderFilter,
                vm => vm.SelectedUniqueFilter,
                vm => vm.SelectedTemplatedFilter,
                vm => vm.SelectedChildFilter)
            .Subscribe(_ => OnFiltersChanged());

        this.WhenAnyValue(
                vm => vm.SelectedFaction,
                vm => vm.SelectedRace,
                vm => vm.SelectedKeyword)
            .Subscribe(_ => OnFiltersChanged());

        // Subscribe to cache loaded event to populate data
        _cache.CacheLoaded += OnCacheLoaded;

        // If cache is already loaded, populate data immediately
        if (_cache.IsLoaded)
        {
            PopulateFromCache();
        }
    }

    private void OnCacheLoaded(object? sender, EventArgs e)
    {
        PopulateFromCache();
    }

    private void PopulateFromCache()
    {
        NpcOutfitAssignments.Clear();
        foreach (var assignment in _cache.AllNpcOutfitAssignments)
        {
            NpcOutfitAssignments.Add(assignment);
        }

        TotalCount = NpcOutfitAssignments.Count;
        UpdateFilteredNpcOutfitAssignments();

        var conflictCount = _cache.AllNpcOutfitAssignments.Count(a => a.HasConflict);
        StatusMessage = $"Found {NpcOutfitAssignments.Count} NPCs with outfit distributions ({conflictCount} conflicts).";
        _logger.Debug("Populated {Count} NPC outfit assignments from cache.", NpcOutfitAssignments.Count);
    }

    [Reactive] public bool IsLoading { get; private set; }

    [Reactive] public string StatusMessage { get; private set; } = string.Empty;

    [Reactive] public ObservableCollection<NpcOutfitAssignmentViewModel> NpcOutfitAssignments { get; private set; } = [];

    public NpcOutfitAssignmentViewModel? SelectedNpcAssignment
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

    [Reactive] public string NpcOutfitSearchText { get; set; } = string.Empty;

    [Reactive] public ObservableCollection<NpcOutfitAssignmentViewModel> FilteredNpcOutfitAssignments { get; private set; } = [];

    [Reactive] public string SelectedNpcOutfitContents { get; private set; } = string.Empty;

    /// <summary>
    /// The NpcFilterData for the currently selected NPC, used to display detailed stats.
    /// </summary>
    [Reactive] public NpcFilterData? SelectedNpcFilterData { get; private set; }

    #region SPID Filter Properties

    /// <summary>
    /// The current filter criteria.
    /// </summary>
    public NpcSpidFilter Filter { get; } = new();

    /// <summary>Gender filter options for the dropdown.</summary>
    public IReadOnlyList<string> GenderFilterOptions { get; } = ["Any", "Female", "Male"];

    [Reactive] public string SelectedGenderFilter { get; set; } = "Any";

    /// <summary>Unique filter options for the dropdown.</summary>
    public IReadOnlyList<string> UniqueFilterOptions { get; } = ["Any", "Unique Only", "Non-Unique"];

    [Reactive] public string SelectedUniqueFilter { get; set; } = "Any";

    /// <summary>Templated filter options for the dropdown.</summary>
    public IReadOnlyList<string> TemplatedFilterOptions { get; } = ["Any", "Templated", "Non-Templated"];

    [Reactive] public string SelectedTemplatedFilter { get; set; } = "Any";

    /// <summary>Child filter options for the dropdown.</summary>
    public IReadOnlyList<string> ChildFilterOptions { get; } = ["Any", "Children", "Adults"];

    [Reactive] public string SelectedChildFilter { get; set; } = "Any";

    /// <summary>Available factions for filtering (from centralized cache).</summary>
    public ObservableCollection<FactionRecordViewModel> AvailableFactions => _cache.AllFactions;

    [Reactive] public FactionRecordViewModel? SelectedFaction { get; set; }

    /// <summary>Available races for filtering (from centralized cache).</summary>
    public ObservableCollection<RaceRecordViewModel> AvailableRaces => _cache.AllRaces;

    [Reactive] public RaceRecordViewModel? SelectedRace { get; set; }

    /// <summary>Available keywords for filtering (from centralized cache).</summary>
    public ObservableCollection<KeywordRecordViewModel> AvailableKeywords => _cache.AllKeywords;

    [Reactive] public KeywordRecordViewModel? SelectedKeyword { get; set; }

    /// <summary>
    /// Generated SPID syntax based on current filters.
    /// </summary>
    [Reactive] public string GeneratedSpidSyntax { get; private set; } = string.Empty;

    /// <summary>
    /// Generated SkyPatcher syntax based on current filters.
    /// </summary>
    [Reactive] public string GeneratedSkyPatcherSyntax { get; private set; } = string.Empty;

    /// <summary>
    /// Human-readable description of active filters.
    /// </summary>
    [Reactive] public string FilterDescription { get; private set; } = "No filters active";

    /// <summary>
    /// Whether any filters are currently active.
    /// </summary>
    [Reactive] public bool HasActiveFilters { get; private set; }

    /// <summary>
    /// Count of NPCs matching current filters.
    /// </summary>
    [Reactive] public int FilteredCount { get; private set; }

    /// <summary>
    /// Total count of NPCs before filtering.
    /// </summary>
    [Reactive] public int TotalCount { get; private set; }

    #endregion

    public ReactiveCommand<Unit, Unit> ScanNpcOutfitsCommand { get; }

    public ReactiveCommand<NpcOutfitAssignmentViewModel, Unit> PreviewNpcOutfitCommand { get; }

    public ReactiveCommand<Unit, Unit> ClearFiltersCommand { get; }

    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    public bool IsInitialized => _mutagenService.IsInitialized;

    /// <summary>
    /// Ensures NPC outfit data is loaded (uses cache if available).
    /// </summary>
    public async Task ScanNpcOutfitsAsync()
    {
        _logger.Debug("ScanNpcOutfitsAsync started");

        try
        {
            IsLoading = true;
            StatusMessage = "Loading NPC outfit data...";

            // Wait for cache to load (uses cached data if available)
            await _cache.EnsureLoadedAsync();

            // Cache load triggers OnCacheLoaded which populates the assignments
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load NPC outfits.");
            StatusMessage = $"Error loading NPC outfits: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Forces a refresh of NPC outfit data, invalidating the cache.
    /// </summary>
    public async Task ForceRefreshNpcOutfitsAsync()
    {
        _logger.Debug("ForceRefreshNpcOutfitsAsync started");

        try
        {
            IsLoading = true;
            StatusMessage = "Refreshing NPC outfit data...";

            // Force reload (invalidates cache and re-scans)
            await _cache.ReloadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh NPC outfits.");
            StatusMessage = $"Error refreshing NPC outfits: {ex.Message}";
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

    private void OnFiltersChanged()
    {
        // Update the Filter model from UI selections
        UpdateFilterFromSelections();

        // Update filtered results and syntax preview
        UpdateFilteredNpcOutfitAssignments();
        UpdateSyntaxPreview();
    }

    private void UpdateFilterFromSelections()
    {
        // Gender
        Filter.IsFemale = SelectedGenderFilter switch
        {
            "Female" => true,
            "Male" => false,
            _ => null
        };

        // Unique
        Filter.IsUnique = SelectedUniqueFilter switch
        {
            "Unique Only" => true,
            "Non-Unique" => false,
            _ => null
        };

        // Templated
        Filter.IsTemplated = SelectedTemplatedFilter switch
        {
            "Templated" => true,
            "Non-Templated" => false,
            _ => null
        };

        // Child
        Filter.IsChild = SelectedChildFilter switch
        {
            "Children" => true,
            "Adults" => false,
            _ => null
        };

        // Faction
        Filter.Factions.Clear();
        if (SelectedFaction != null)
        {
            Filter.Factions.Add(SelectedFaction.FormKey);
        }

        // Race
        Filter.Races.Clear();
        if (SelectedRace != null)
        {
            Filter.Races.Add(SelectedRace.FormKey);
        }

        // Keyword
        Filter.Keywords.Clear();
        if (SelectedKeyword != null)
        {
            Filter.Keywords.Add(SelectedKeyword.FormKey);
        }

        // Update UI state
        HasActiveFilters = !Filter.IsEmpty;
        FilterDescription = NpcSpidSyntaxGenerator.GetFilterDescription(Filter);
    }

    private void UpdateFilteredNpcOutfitAssignments()
    {
        IEnumerable<NpcOutfitAssignmentViewModel> filtered = NpcOutfitAssignments;

        // Apply text search filter
        if (!string.IsNullOrWhiteSpace(NpcOutfitSearchText))
        {
            var term = NpcOutfitSearchText.Trim();
            filtered = filtered.Where(a =>
                (a.DisplayName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.EditorId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.FinalOutfitEditorId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                a.FormKeyString.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                a.ModDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Apply SPID-style filters
        if (!Filter.IsEmpty)
        {
            filtered = filtered.Where(a => MatchesSpidFilter(a.NpcFormKey));
        }

        FilteredNpcOutfitAssignments.Clear();
        foreach (var assignment in filtered)
        {
            FilteredNpcOutfitAssignments.Add(assignment);
        }

        FilteredCount = FilteredNpcOutfitAssignments.Count;
    }

    private bool MatchesSpidFilter(FormKey npcFormKey)
    {
        if (!_cache.NpcsByFormKey.TryGetValue(npcFormKey, out var npcData))
            return true; // If we don't have filter data, don't filter out

        return Filter.Matches(npcData);
    }

    private void UpdateSyntaxPreview()
    {
        var linkCache = _mutagenService.LinkCache as ILinkCache<ISkyrimMod, ISkyrimModGetter>;
        var (spidSyntax, skyPatcherSyntax) = NpcSpidSyntaxGenerator.Generate(Filter, linkCache);

        GeneratedSpidSyntax = spidSyntax;
        GeneratedSkyPatcherSyntax = skyPatcherSyntax;
    }

    private void ClearFilters()
    {
        SelectedGenderFilter = "Any";
        SelectedUniqueFilter = "Any";
        SelectedTemplatedFilter = "Any";
        SelectedChildFilter = "Any";
        SelectedFaction = null;
        SelectedRace = null;
        SelectedKeyword = null;

        Filter.Clear();
        HasActiveFilters = false;
        FilterDescription = "No filters active";

        UpdateFilteredNpcOutfitAssignments();
        UpdateSyntaxPreview();
    }

    private void UpdateSelectedNpcOutfitContents()
    {
        // Update NpcFilterData for the selected NPC
        if (SelectedNpcAssignment != null &&
            _cache.NpcsByFormKey.TryGetValue(SelectedNpcAssignment.NpcFormKey, out var npcData))
        {
            SelectedNpcFilterData = npcData;
        }
        else
        {
            SelectedNpcFilterData = null;
        }

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
        sb.Append(CultureInfo.InvariantCulture, $"Outfit: {outfit.EditorID ?? outfit.FormKey.ToString()}").AppendLine();
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
                sb.Append(CultureInfo.InvariantCulture, $"  - {armorName}").AppendLine();
            }
        }

        SelectedNpcOutfitContents = sb.ToString();
    }
}
