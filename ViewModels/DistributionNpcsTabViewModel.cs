using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

public partial class DistributionNpcsTabViewModel : ReactiveObject
{
    private readonly ArmorPreviewService _armorPreviewService;
    private readonly GameDataCacheService _cache;

    private readonly IObservable<bool> _hasFilters;
    private readonly ILogger _logger;
    private readonly MutagenService _mutagenService;
    private readonly IObservable<bool> _notLoading;

    [ReactiveCollection] private ObservableCollection<NpcOutfitAssignmentViewModel> _filteredNpcOutfitAssignments = [];

    [Reactive] private bool _hideVanillaDistributions;

    [Reactive] private bool _isLoading;

    [ReactiveCollection] private ObservableCollection<NpcOutfitAssignmentViewModel> _npcOutfitAssignments = [];

    [Reactive] private string _npcOutfitSearchText = string.Empty;

    /// <summary>
    ///     Gets the NpcFilterData for the currently selected NPC, used to display detailed stats.
    /// </summary>
    [Reactive] private NpcFilterData? _selectedNpcFilterData;

    [Reactive] private string _selectedNpcOutfitContents = string.Empty;

    [Reactive] private string _statusMessage = string.Empty;

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

        _notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);

        _hasFilters = this.WhenAnyValue(vm => vm.HasActiveFilters);

        // NPCs tab search filtering - combine text search with SPID filters
        this.WhenAnyValue(vm => vm.NpcOutfitSearchText)
            .Subscribe(_ => UpdateFilteredNpcOutfitAssignments());

        // Vanilla distribution filtering
        this.WhenAnyValue(vm => vm.HideVanillaDistributions)
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
                vm => vm.SelectedKeyword,
                vm => vm.SelectedClass)
            .Subscribe(_ => OnFiltersChanged());

        // Subscribe to cache loaded event to populate data
        _cache.CacheLoaded += OnCacheLoaded;

        // If cache is already loaded, populate data immediately
        if (_cache.IsLoaded) PopulateFromCache();
    }

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

    public Interaction<ArmorPreviewSceneCollection, Unit> ShowPreview { get; } = new();

    public bool IsInitialized => _mutagenService.IsInitialized;

    private void OnCacheLoaded(object? sender, EventArgs e) => PopulateFromCache();

    private void PopulateFromCache()
    {
        NpcOutfitAssignments.Clear();
        foreach (var assignment in _cache.AllNpcOutfitAssignments) NpcOutfitAssignments.Add(assignment);

        TotalCount = NpcOutfitAssignments.Count;
        UpdateFilteredNpcOutfitAssignments();

        var conflictCount = _cache.AllNpcOutfitAssignments.Count(a => a.HasConflict);
        StatusMessage =
            $"Found {NpcOutfitAssignments.Count} NPCs with outfit distributions ({conflictCount} conflicts).";
        _logger.Debug("Populated {Count} NPC outfit assignments from cache.", NpcOutfitAssignments.Count);
    }

    /// <summary>
    ///     Event raised when a filter is copied, allowing parent to store it for pasting.
    /// </summary>
    public event EventHandler<CopiedNpcFilter>? FilterCopied;

    /// <summary>
    ///     Ensures NPC outfit data is loaded (uses cache if available).
    /// </summary>
    [ReactiveCommand(CanExecute = nameof(_notLoading))]
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
    ///     Forces a refresh of NPC outfit data, invalidating the cache.
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

    [ReactiveCommand(CanExecute = nameof(_notLoading))]
    private async Task PreviewNpcOutfitAsync(NpcOutfitAssignmentViewModel? npcAssignment)
    {
        if (npcAssignment == null || !npcAssignment.FinalOutfitFormKey.HasValue)
        {
            StatusMessage = "No outfit to preview for this NPC.";
            return;
        }

        if (!_mutagenService.IsInitialized ||
            _mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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

            var npcGender = GetNpcGender(npcAssignment.NpcFormKey, linkCache);
            var metadata = new OutfitMetadata(label, outfit.FormKey.ModKey.FileName.String, false);
            var collection = new ArmorPreviewSceneCollection(
                1,
                0,
                new[] { metadata },
                async (_, gender) =>
                {
                    var scene = await _armorPreviewService.BuildPreviewAsync(armorPieces, gender);
                    return scene with { OutfitLabel = label, SourceFile = outfit.FormKey.ModKey.FileName.String };
                },
                npcGender);

            await ShowPreview.Handle(collection);
            StatusMessage = $"Preview ready for {label}.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to preview outfit {Identifier}", label);
            StatusMessage = $"Failed to preview outfit: {ex.Message}";
        }
    }

    [ReactiveCommand(CanExecute = nameof(_notLoading))]
    private async Task PreviewDistributionOutfitAsync(OutfitDistribution? clickedDistribution)
    {
        if (clickedDistribution == null || SelectedNpcAssignment == null)
        {
            StatusMessage = "No distribution to preview.";
            return;
        }

        if (!_mutagenService.IsInitialized ||
            _mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            StatusMessage = "Initialize Skyrim data path before previewing outfits.";
            return;
        }

        try
        {
            StatusMessage = "Building outfit preview...";

            var distributions = SelectedNpcAssignment.Distributions;
            var clickedIndex = -1;
            for (var i = 0; i < distributions.Count; i++)
            {
                if (distributions[i] == clickedDistribution)
                {
                    clickedIndex = i;
                    break;
                }
            }

            if (clickedIndex == -1)
                clickedIndex = 0;

            var metadata = distributions
                .Select(d => new OutfitMetadata(
                    d.OutfitEditorId ?? d.OutfitFormKey.ToString(),
                    d.FileName,
                    d.IsWinner))
                .ToList();

            var npcGender = GetNpcGender(SelectedNpcAssignment.NpcFormKey, linkCache);
            var collection = new ArmorPreviewSceneCollection(
                distributions.Count,
                clickedIndex,
                metadata,
                async (index, gender) =>
                {
                    var distribution = distributions[index];

                    if (!linkCache.TryResolve<IOutfitGetter>(distribution.OutfitFormKey, out var outfit))
                        throw new InvalidOperationException($"Could not resolve outfit: {distribution.OutfitFormKey}");

                    var armorPieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
                    if (armorPieces.Count == 0)
                        throw new InvalidOperationException(
                            $"Outfit '{outfit.EditorID ?? outfit.FormKey.ToString()}' has no armor pieces");

                    var scene = await _armorPreviewService.BuildPreviewAsync(armorPieces, gender);
                    return scene with
                    {
                        OutfitLabel = distribution.OutfitEditorId ?? distribution.OutfitFormKey.ToString(),
                        SourceFile = distribution.FileName,
                        IsWinner = distribution.IsWinner
                    };
                },
                npcGender);

            await ShowPreview.Handle(collection);
            StatusMessage = $"Preview ready with {distributions.Count} outfit(s).";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to preview outfits");
            StatusMessage = $"Failed to preview outfits: {ex.Message}";
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
        if (SelectedFaction != null) Filter.Factions.Add(SelectedFaction.FormKey);

        // Race
        Filter.Races.Clear();
        if (SelectedRace != null) Filter.Races.Add(SelectedRace.FormKey);

        // Keyword
        Filter.Keywords.Clear();
        if (SelectedKeyword != null) Filter.Keywords.Add(SelectedKeyword.FormKey);

        // Class
        Filter.Classes.Clear();
        if (SelectedClass != null) Filter.Classes.Add(SelectedClass.FormKey);

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

        // Filter out NPCs whose final outfit is their default outfit (vanilla distribution)
        if (HideVanillaDistributions)
            filtered = filtered.Where(a => !IsVanillaDistribution(a.NpcFormKey, a.FinalOutfitFormKey));

        // Apply SPID-style filters
        if (!Filter.IsEmpty)
        {
            // When filters are active, also exclude NPCs without EditorIDs
            // These are typically template-generated NPCs that can't be targeted in distribution files
            filtered = filtered.Where(a =>
                !string.IsNullOrEmpty(a.EditorId) &&
                a.EditorId != "(No EditorID)" &&
                MatchesSpidFilter(a.NpcFormKey));
        }

        FilteredNpcOutfitAssignments.Clear();
        foreach (var assignment in filtered) FilteredNpcOutfitAssignments.Add(assignment);

        FilteredCount = FilteredNpcOutfitAssignments.Count;
    }

    /// <summary>
    ///     Returns true if the NPC's final outfit is their default outfit (no distribution changed it).
    /// </summary>
    private bool IsVanillaDistribution(FormKey npcFormKey, FormKey? finalOutfitFormKey)
    {
        if (!_cache.NpcsByFormKey.TryGetValue(npcFormKey, out var npcData))
            return false;

        // If no final outfit or no default outfit, not a vanilla distribution
        if (!finalOutfitFormKey.HasValue || !npcData.DefaultOutfitFormKey.HasValue)
            return false;

        // If final outfit matches default outfit, it's a vanilla distribution
        return finalOutfitFormKey.Value == npcData.DefaultOutfitFormKey.Value;
    }

    private bool MatchesSpidFilter(FormKey npcFormKey)
    {
        if (!_cache.NpcsByFormKey.TryGetValue(npcFormKey, out var npcData))
            return false; // If we don't have filter data, filter out (can't evaluate filters)

        return Filter.Matches(npcData);
    }

    private void UpdateSyntaxPreview()
    {
        var linkCache = _mutagenService.LinkCache;
        var (spidSyntax, skyPatcherSyntax) = NpcSpidSyntaxGenerator.Generate(Filter, linkCache);

        GeneratedSpidSyntax = spidSyntax;
        GeneratedSkyPatcherSyntax = skyPatcherSyntax;
    }

    [ReactiveCommand]
    private void ClearFilters()
    {
        SelectedGenderFilter = "Any";
        SelectedUniqueFilter = "Any";
        SelectedTemplatedFilter = "Any";
        SelectedChildFilter = "Any";
        SelectedFaction = null;
        SelectedRace = null;
        SelectedKeyword = null;
        SelectedClass = null;

        Filter.Clear();
        HasActiveFilters = false;
        FilterDescription = "No filters active";

        UpdateFilteredNpcOutfitAssignments();
        UpdateSyntaxPreview();
    }

    [ReactiveCommand(CanExecute = nameof(_hasFilters))]
    private void CopyFilter()
    {
        if (!HasActiveFilters)
        {
            StatusMessage = "No filters to copy. Apply filters first.";
            return;
        }

        var copiedFilter = CopiedNpcFilter.FromSpidFilter(Filter, FilterDescription);
        FilterCopied?.Invoke(this, copiedFilter);
        StatusMessage = $"Filter copied: {FilterDescription}";
        _logger.Debug("Copied filter: {Description}", FilterDescription);
    }

    private void UpdateSelectedNpcOutfitContents()
    {
        // Update NpcFilterData for the selected NPC
        if (SelectedNpcAssignment != null &&
            _cache.NpcsByFormKey.TryGetValue(SelectedNpcAssignment.NpcFormKey, out var npcData))
            SelectedNpcFilterData = npcData;
        else
            SelectedNpcFilterData = null;

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

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"Outfit: {outfit.EditorID ?? outfit.FormKey.ToString()}").AppendLine();
        sb.AppendLine();

        var armorPieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
        if (armorPieces.Count == 0)
            sb.AppendLine("(No armor pieces)");
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

    private static GenderedModelVariant GetNpcGender(FormKey npcFormKey,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        if (linkCache.TryResolve<INpcGetter>(npcFormKey, out var npc))
        {
            return npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female)
                ? GenderedModelVariant.Female
                : GenderedModelVariant.Male;
        }

        return GenderedModelVariant.Female;
    }

    #region SPID Filter Properties

    /// <summary>
    ///     Gets the current filter criteria.
    /// </summary>
    public NpcSpidFilter Filter { get; } = new();

    /// <summary>Gets the gender filter options for the dropdown.</summary>
    public IReadOnlyList<string> GenderFilterOptions { get; } = ["Any", "Female", "Male"];

    [Reactive] private string _selectedGenderFilter = "Any";

    /// <summary>Gets the unique filter options for the dropdown.</summary>
    public IReadOnlyList<string> UniqueFilterOptions { get; } = ["Any", "Unique Only", "Non-Unique"];

    [Reactive] private string _selectedUniqueFilter = "Any";

    /// <summary>Gets the templated filter options for the dropdown.</summary>
    public IReadOnlyList<string> TemplatedFilterOptions { get; } = ["Any", "Templated", "Non-Templated"];

    [Reactive] private string _selectedTemplatedFilter = "Any";

    /// <summary>Child filter options for the dropdown.</summary>
    public IReadOnlyList<string> ChildFilterOptions { get; } = ["Any", "Children", "Adults"];

    [Reactive] private string _selectedChildFilter = "Any";

    /// <summary>Available factions for filtering (from centralized cache).</summary>
    public ObservableCollection<FactionRecordViewModel> AvailableFactions => _cache.AllFactions;

    [Reactive] private FactionRecordViewModel? _selectedFaction;

    /// <summary>Available races for filtering (from centralized cache).</summary>
    public ObservableCollection<RaceRecordViewModel> AvailableRaces => _cache.AllRaces;

    [Reactive] private RaceRecordViewModel? _selectedRace;

    /// <summary>Available keywords for filtering (from centralized cache).</summary>
    public ObservableCollection<KeywordRecordViewModel> AvailableKeywords => _cache.AllKeywords;

    [Reactive] private KeywordRecordViewModel? _selectedKeyword;

    /// <summary>Available classes for filtering (from centralized cache).</summary>
    public ObservableCollection<ClassRecordViewModel> AvailableClasses => _cache.AllClasses;

    [Reactive] private ClassRecordViewModel? _selectedClass;

    /// <summary>
    ///     Generated SPID syntax based on current filters.
    /// </summary>
    [Reactive] private string _generatedSpidSyntax = string.Empty;

    /// <summary>
    ///     Generated SkyPatcher syntax based on current filters.
    /// </summary>
    [Reactive] private string _generatedSkyPatcherSyntax = string.Empty;

    /// <summary>
    ///     Human-readable description of active filters.
    /// </summary>
    [Reactive] private string _filterDescription = "No filters active";

    /// <summary>
    ///     Whether any filters are currently active.
    /// </summary>
    [Reactive] private bool _hasActiveFilters;

    /// <summary>
    ///     Count of NPCs matching current filters.
    /// </summary>
    [Reactive] private int _filteredCount;

    /// <summary>
    ///     Total count of NPCs before filtering.
    /// </summary>
    [Reactive] private int _totalCount;

    #endregion
}
