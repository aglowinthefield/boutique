using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using DynamicData;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

public partial class DistributionNpcsTabViewModel : ReactiveObject, IDisposable
{
    private readonly ArmorPreviewService _armorPreviewService;
    private readonly GameDataCacheService _cache;
    private readonly CompositeDisposable _disposables = new();

    private readonly IObservable<bool> _hasFilters;
    private readonly ILogger _logger;
    private readonly MutagenService _mutagenService;
    private readonly IObservable<bool> _notLoading;

    private readonly SourceCache<NpcOutfitAssignmentViewModel, FormKey> _npcAssignmentsSource = new(x => x.NpcFormKey);

    [Reactive] private bool _hideVanillaDistributions;

    [Reactive] private bool _isLoading;

    [Reactive] private string _npcOutfitSearchText = string.Empty;

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

        var filterPredicate = CreateFilterPredicate();

        _disposables.Add(_npcAssignmentsSource.Connect()
            .Filter(filterPredicate)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var filteredAssignments)
            .Subscribe(_ => FilteredCount = filteredAssignments.Count));
        FilteredNpcOutfitAssignments = filteredAssignments;

        this.WhenAnyValue(vm => vm.SelectedNpcAssignment)
            .Subscribe(_ => UpdateSelectedNpcOutfitContents());

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

        _cache.CacheLoaded += OnCacheLoaded;

        if (_cache.IsLoaded)
        {
            PopulateFromCache();
        }
    }

    private IObservable<Func<NpcOutfitAssignmentViewModel, bool>> CreateFilterPredicate()
    {
        var textFilter = this.WhenAnyValue(vm => vm.NpcOutfitSearchText)
            .Throttle(TimeSpan.FromMilliseconds(200))
            .Select(text => text?.Trim() ?? string.Empty);

        var vanillaFilter = this.WhenAnyValue(vm => vm.HideVanillaDistributions);

        var spidFilters = this.WhenAnyValue(
            vm => vm.SelectedGenderFilter,
            vm => vm.SelectedUniqueFilter,
            vm => vm.SelectedTemplatedFilter,
            vm => vm.SelectedChildFilter,
            vm => vm.SelectedFaction,
            vm => vm.SelectedRace,
            vm => vm.SelectedKeyword,
            vm => vm.SelectedClass,
            (_, _, _, _, _, _, _, _) => Unit.Default);

        return textFilter
            .CombineLatest(vanillaFilter, spidFilters, (text, hideVanilla, _) => (text, hideVanilla))
            .Select(tuple => CreateFilterFunc(tuple.text, tuple.hideVanilla));
    }

    private Func<NpcOutfitAssignmentViewModel, bool> CreateFilterFunc(string searchText, bool hideVanilla)
    {
        return assignment =>
        {
            if (!string.IsNullOrEmpty(searchText))
            {
                var matchesText =
                    (assignment.DisplayName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (assignment.EditorId?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (assignment.FinalOutfitEditorId?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    assignment.FormKeyString.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    assignment.ModDisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase);

                if (!matchesText)
                {
                    return false;
                }
            }

            if (hideVanilla && IsVanillaDistribution(assignment.NpcFormKey, assignment.FinalOutfitFormKey))
            {
                return false;
            }

            if (!Filter.IsEmpty)
            {
                if (string.IsNullOrEmpty(assignment.EditorId) || assignment.EditorId == "(No EditorID)")
                {
                    return false;
                }

                if (!MatchesSpidFilter(assignment.NpcFormKey))
                {
                    return false;
                }
            }

            return true;
        };
    }

    public ReadOnlyObservableCollection<NpcOutfitAssignmentViewModel> FilteredNpcOutfitAssignments { get; }

    public NpcOutfitAssignmentViewModel? SelectedNpcAssignment
    {
        get => field;
        set
        {
            field?.IsSelected = false;
            this.RaiseAndSetIfChanged(ref field, value);
            value?.IsSelected = true;
        }
    }

    public Interaction<ArmorPreviewSceneCollection, Unit> ShowPreview { get; } = new();

    public bool IsInitialized => _mutagenService.IsInitialized;

    private void OnCacheLoaded(object? sender, EventArgs e) => PopulateFromCache();

    private void PopulateFromCache()
    {
        _npcAssignmentsSource.Edit(cache =>
        {
            cache.Clear();
            cache.AddOrUpdate(_cache.AllNpcOutfitAssignments);
        });

        TotalCount = _npcAssignmentsSource.Count;

        var conflictCount = _cache.AllNpcOutfitAssignments.Count(a => a.HasConflict);
        StatusMessage = $"Found {TotalCount} NPCs with outfit distributions ({conflictCount} conflicts).";
        _logger.Debug("Populated {Count} NPC outfit assignments from cache.", TotalCount);
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
            {
                clickedIndex = 0;
            }

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
                    {
                        throw new InvalidOperationException($"Could not resolve outfit: {distribution.OutfitFormKey}");
                    }

                    var armorPieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
                    if (armorPieces.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"Outfit '{outfit.EditorID ?? outfit.FormKey.ToString()}' has no armor pieces");
                    }

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
        UpdateFilterFromSelections();
        UpdateSyntaxPreview();
    }

    private void UpdateFilterFromSelections()
    {
        Filter.IsFemale = SelectedGenderFilter switch
        {
            "Female" => true,
            "Male" => false,
            _ => null
        };

        Filter.IsUnique = SelectedUniqueFilter switch
        {
            "Unique Only" => true,
            "Non-Unique" => false,
            _ => null
        };

        Filter.IsTemplated = SelectedTemplatedFilter switch
        {
            "Templated" => true,
            "Non-Templated" => false,
            _ => null
        };

        Filter.IsChild = SelectedChildFilter switch
        {
            "Children" => true,
            "Adults" => false,
            _ => null
        };

        Filter.Factions.Clear();
        if (SelectedFaction != null)
        {
            Filter.Factions.Add(SelectedFaction.FormKey);
        }

        Filter.Races.Clear();
        if (SelectedRace != null)
        {
            Filter.Races.Add(SelectedRace.FormKey);
        }

        Filter.Keywords.Clear();
        if (SelectedKeyword != null)
        {
            Filter.Keywords.Add(SelectedKeyword.FormKey);
        }

        Filter.Classes.Clear();
        if (SelectedClass != null)
        {
            Filter.Classes.Add(SelectedClass.FormKey);
        }

        HasActiveFilters = !Filter.IsEmpty;
        FilterDescription = NpcSpidSyntaxGenerator.GetFilterDescription(Filter);
    }

    /// <summary>
    ///     Returns true if the NPC's final outfit is their default outfit (no distribution changed it).
    /// </summary>
    private bool IsVanillaDistribution(FormKey npcFormKey, FormKey? finalOutfitFormKey)
    {
        var lookup = _cache.LookupNpc(npcFormKey);
        if (!lookup.HasValue)
        {
            return false;
        }

        var npcData = lookup.Value;

        if (!finalOutfitFormKey.HasValue || !npcData.DefaultOutfitFormKey.HasValue)
        {
            return false;
        }

        return finalOutfitFormKey.Value == npcData.DefaultOutfitFormKey.Value;
    }

    private bool MatchesSpidFilter(FormKey npcFormKey)
    {
        var lookup = _cache.LookupNpc(npcFormKey);
        if (!lookup.HasValue)
        {
            return false;
        }

        return Filter.Matches(lookup.Value);
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
        if (SelectedNpcAssignment != null)
        {
            var lookup = _cache.LookupNpc(SelectedNpcAssignment.NpcFormKey);
            SelectedNpcFilterData = lookup.HasValue ? lookup.Value : null;
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

        var sb = new StringBuilder();
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
    public ReadOnlyObservableCollection<FactionRecordViewModel> AvailableFactions => _cache.AllFactions;

    [Reactive] private FactionRecordViewModel? _selectedFaction;

    /// <summary>Available races for filtering (from centralized cache).</summary>
    public ReadOnlyObservableCollection<RaceRecordViewModel> AvailableRaces => _cache.AllRaces;

    [Reactive] private RaceRecordViewModel? _selectedRace;

    /// <summary>Available keywords for filtering (from centralized cache).</summary>
    public ReadOnlyObservableCollection<KeywordRecordViewModel> AvailableKeywords => _cache.AllKeywords;

    [Reactive] private KeywordRecordViewModel? _selectedKeyword;

    /// <summary>Available classes for filtering (from centralized cache).</summary>
    public ReadOnlyObservableCollection<ClassRecordViewModel> AvailableClasses => _cache.AllClasses;

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

    public void Dispose()
    {
        _cache.CacheLoaded -= OnCacheLoaded;
        _disposables.Dispose();
        _npcAssignmentsSource.Dispose();
    }
}
