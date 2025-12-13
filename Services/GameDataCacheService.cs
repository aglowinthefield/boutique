using System.Collections.ObjectModel;
using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

/// <summary>
/// Centralized cache for game data (NPCs, factions, races, keywords, outfits, distribution files, outfit assignments).
/// Loads all data once at app startup and makes it available to all ViewModels.
/// </summary>
public class GameDataCacheService
{
    private readonly MutagenService _mutagenService;
    private readonly DistributionDiscoveryService _discoveryService;
    private readonly NpcOutfitResolutionService _outfitResolutionService;
    private readonly SettingsViewModel _settings;
    private readonly ILogger _logger;
    private bool _isLoaded;
    private bool _isLoading;

    public GameDataCacheService(
        MutagenService mutagenService,
        DistributionDiscoveryService discoveryService,
        NpcOutfitResolutionService outfitResolutionService,
        SettingsViewModel settings,
        ILogger logger)
    {
        _mutagenService = mutagenService;
        _discoveryService = discoveryService;
        _outfitResolutionService = outfitResolutionService;
        _settings = settings;
        _logger = logger.ForContext<GameDataCacheService>();

        // Subscribe to MutagenService initialization to auto-load cache
        _mutagenService.Initialized += OnMutagenInitialized;
    }

    /// <summary>Whether the cache has been loaded.</summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>Whether the cache is currently loading.</summary>
    public bool IsLoading => _isLoading;

    /// <summary>Event raised when cache loading completes.</summary>
    public event EventHandler? CacheLoaded;

    // ============================================================================
    // CACHED COLLECTIONS - Bind directly to these in ViewModels
    // ============================================================================

    /// <summary>All NPCs with full filter data for SPID-style matching.</summary>
    public ObservableCollection<NpcFilterData> AllNpcs { get; } = [];

    /// <summary>All factions from the load order.</summary>
    public ObservableCollection<FactionRecordViewModel> AllFactions { get; } = [];

    /// <summary>All races from the load order.</summary>
    public ObservableCollection<RaceRecordViewModel> AllRaces { get; } = [];

    /// <summary>All keywords from the load order (filtered to NPC-relevant ones).</summary>
    public ObservableCollection<KeywordRecordViewModel> AllKeywords { get; } = [];

    /// <summary>All outfits from the load order.</summary>
    public ObservableCollection<IOutfitGetter> AllOutfits { get; } = [];

    /// <summary>Simple NPC records (for distribution entry NPC selection).</summary>
    public ObservableCollection<NpcRecordViewModel> AllNpcRecords { get; } = [];

    /// <summary>All discovered distribution files (SPID/SkyPatcher INIs).</summary>
    public ObservableCollection<DistributionFileViewModel> AllDistributionFiles { get; } = [];

    /// <summary>All resolved NPC outfit assignments from distribution files.</summary>
    public ObservableCollection<NpcOutfitAssignmentViewModel> AllNpcOutfitAssignments { get; } = [];

    // ============================================================================
    // LOOKUP DICTIONARIES - Fast access by FormKey
    // ============================================================================

    /// <summary>NPC filter data by FormKey for fast SPID matching.</summary>
    public Dictionary<FormKey, NpcFilterData> NpcsByFormKey { get; } = [];

    /// <summary>Factions by FormKey.</summary>
    public Dictionary<FormKey, FactionRecordViewModel> FactionsByFormKey { get; } = [];

    /// <summary>Races by FormKey.</summary>
    public Dictionary<FormKey, RaceRecordViewModel> RacesByFormKey { get; } = [];

    /// <summary>Keywords by FormKey.</summary>
    public Dictionary<FormKey, KeywordRecordViewModel> KeywordsByFormKey { get; } = [];

    // ============================================================================
    // LOADING
    // ============================================================================

    private async void OnMutagenInitialized(object? sender, EventArgs e)
    {
        _logger.Information("MutagenService initialized, loading game data cache...");
        await LoadAsync();
    }

    /// <summary>
    /// Loads all game data into the cache. Called automatically when MutagenService initializes.
    /// </summary>
    public async Task LoadAsync()
    {
        if (_isLoading)
        {
            _logger.Debug("Cache is already loading, skipping duplicate request.");
            return;
        }

        if (!_mutagenService.IsInitialized)
        {
            _logger.Warning("Cannot load cache - MutagenService not initialized.");
            return;
        }

        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            _logger.Warning("Cannot load cache - LinkCache not available.");
            return;
        }

        try
        {
            _isLoading = true;
            _logger.Information("Loading game data cache...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Load all data types in parallel
            var npcsTask = Task.Run(() => LoadNpcs(linkCache));
            var factionsTask = Task.Run(() => LoadFactions(linkCache));
            var racesTask = Task.Run(() => LoadRaces(linkCache));
            var keywordsTask = Task.Run(() => LoadKeywords(linkCache));
            var outfitsTask = Task.Run(() => LoadOutfits(linkCache));

            await Task.WhenAll(npcsTask, factionsTask, racesTask, keywordsTask, outfitsTask);

            var (npcFilterDataList, npcRecordsList) = await npcsTask;
            var factionsList = await factionsTask;
            var racesList = await racesTask;
            var keywordsList = await keywordsTask;
            var outfitsList = await outfitsTask;

            // Populate collections on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // NPCs
                AllNpcs.Clear();
                NpcsByFormKey.Clear();
                AllNpcRecords.Clear();
                foreach (var npc in npcFilterDataList)
                {
                    AllNpcs.Add(npc);
                    NpcsByFormKey[npc.FormKey] = npc;
                }
                foreach (var npc in npcRecordsList)
                {
                    AllNpcRecords.Add(npc);
                }

                // Factions
                AllFactions.Clear();
                FactionsByFormKey.Clear();
                foreach (var faction in factionsList)
                {
                    AllFactions.Add(faction);
                    FactionsByFormKey[faction.FormKey] = faction;
                }

                // Races
                AllRaces.Clear();
                RacesByFormKey.Clear();
                foreach (var race in racesList)
                {
                    AllRaces.Add(race);
                    RacesByFormKey[race.FormKey] = race;
                }

                // Keywords
                AllKeywords.Clear();
                KeywordsByFormKey.Clear();
                foreach (var keyword in keywordsList)
                {
                    AllKeywords.Add(keyword);
                    KeywordsByFormKey[keyword.FormKey] = keyword;
                }

                // Outfits
                AllOutfits.Clear();
                foreach (var outfit in outfitsList)
                {
                    AllOutfits.Add(outfit);
                }
            });

            // Now discover distribution files and resolve NPC outfit assignments
            await LoadDistributionDataAsync(npcFilterDataList);

            stopwatch.Stop();
            _isLoaded = true;
            _logger.Information(
                "Game data cache loaded in {ElapsedMs}ms: {NpcCount} NPCs, {FactionCount} factions, {RaceCount} races, {KeywordCount} keywords, {OutfitCount} outfits, {FileCount} distribution files, {AssignmentCount} NPC outfit assignments.",
                stopwatch.ElapsedMilliseconds,
                npcFilterDataList.Count,
                factionsList.Count,
                racesList.Count,
                keywordsList.Count,
                outfitsList.Count,
                AllDistributionFiles.Count,
                AllNpcOutfitAssignments.Count);

            CacheLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load game data cache.");
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// Reloads the cache. Use after plugins change.
    /// </summary>
    public async Task ReloadAsync()
    {
        _isLoaded = false;
        await LoadAsync();
    }

    /// <summary>
    /// Loads distribution files and resolves NPC outfit assignments.
    /// </summary>
    private async Task LoadDistributionDataAsync(List<NpcFilterData> npcFilterDataList)
    {
        var dataPath = _settings.SkyrimDataPath;
        if (string.IsNullOrWhiteSpace(dataPath) || !System.IO.Directory.Exists(dataPath))
        {
            _logger.Warning("Cannot load distribution data - Skyrim data path not set or doesn't exist.");
            return;
        }

        try
        {
            // Discover distribution files
            _logger.Debug("Discovering distribution files in {DataPath}...", dataPath);
            var discoveredFiles = await _discoveryService.DiscoverAsync(dataPath);

            var outfitFiles = discoveredFiles
                .Where(f => f.OutfitDistributionCount > 0)
                .ToList();

            _logger.Debug("Found {Count} distribution files with outfit distributions.", outfitFiles.Count);

            // Create ViewModels for the files
            var fileViewModels = outfitFiles
                .Select(f => new DistributionFileViewModel(f))
                .ToList();

            // Resolve NPC outfit assignments
            _logger.Debug("Resolving NPC outfit assignments...");
            var assignments = await _outfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(
                outfitFiles,
                npcFilterDataList);

            _logger.Debug("Resolved {Count} NPC outfit assignments.", assignments.Count);

            // Update collections on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                AllDistributionFiles.Clear();
                foreach (var file in fileViewModels)
                {
                    AllDistributionFiles.Add(file);
                }

                AllNpcOutfitAssignments.Clear();
                foreach (var assignment in assignments)
                {
                    AllNpcOutfitAssignments.Add(new NpcOutfitAssignmentViewModel(assignment));
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load distribution data.");
        }
    }

    private (List<NpcFilterData>, List<NpcRecordViewModel>) LoadNpcs(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var filterDataList = new List<NpcFilterData>();
        var recordsList = new List<NpcRecordViewModel>();

        foreach (var npc in linkCache.WinningOverrides<INpcGetter>())
        {
            // Only skip null FormKeys - include NPCs without EditorIDs
            // (some mods create NPCs without EditorIDs but they still need to be filtered)
            if (npc.FormKey == FormKey.Null)
                continue;

            try
            {
                var originalModKey = FindOriginalMaster(linkCache, npc.FormKey);

                // Build NpcFilterData
                var filterData = BuildNpcFilterData(npc, linkCache, originalModKey);
                if (filterData != null)
                {
                    filterDataList.Add(filterData);
                }

                // Build simple NpcRecordViewModel
                var record = new NpcRecord(
                    npc.FormKey,
                    npc.EditorID,
                    Utilities.NpcDataExtractor.GetName(npc),
                    originalModKey);
                recordsList.Add(new NpcRecordViewModel(record));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to process NPC {FormKey}", npc.FormKey);
            }
        }

        return (filterDataList, recordsList);
    }

    private NpcFilterData? BuildNpcFilterData(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, ModKey originalModKey)
    {
        try
        {
            var keywords = Utilities.NpcDataExtractor.ExtractKeywords(npc, linkCache);
            var factions = Utilities.NpcDataExtractor.ExtractFactions(npc, linkCache);
            var (raceFormKey, raceEditorId) = Utilities.NpcDataExtractor.ExtractRace(npc, linkCache);
            var (classFormKey, classEditorId) = Utilities.NpcDataExtractor.ExtractClass(npc, linkCache);
            var (combatStyleFormKey, combatStyleEditorId) = Utilities.NpcDataExtractor.ExtractCombatStyle(npc, linkCache);
            var (voiceTypeFormKey, voiceTypeEditorId) = Utilities.NpcDataExtractor.ExtractVoiceType(npc, linkCache);
            var (outfitFormKey, outfitEditorId) = Utilities.NpcDataExtractor.ExtractDefaultOutfit(npc, linkCache);
            var (templateFormKey, templateEditorId) = Utilities.NpcDataExtractor.ExtractTemplate(npc, linkCache);
            var (isFemale, isUnique, isSummonable, isLeveled) = Utilities.NpcDataExtractor.ExtractTraits(npc);
            var isChild = Utilities.NpcDataExtractor.IsChildRace(raceEditorId);
            var level = Utilities.NpcDataExtractor.ExtractLevel(npc);

            return new NpcFilterData
            {
                FormKey = npc.FormKey,
                EditorId = npc.EditorID,
                Name = Utilities.NpcDataExtractor.GetName(npc),
                SourceMod = originalModKey,
                Keywords = keywords,
                Factions = factions,
                RaceFormKey = raceFormKey,
                RaceEditorId = raceEditorId,
                ClassFormKey = classFormKey,
                ClassEditorId = classEditorId,
                CombatStyleFormKey = combatStyleFormKey,
                CombatStyleEditorId = combatStyleEditorId,
                VoiceTypeFormKey = voiceTypeFormKey,
                VoiceTypeEditorId = voiceTypeEditorId,
                DefaultOutfitFormKey = outfitFormKey,
                DefaultOutfitEditorId = outfitEditorId,
                IsFemale = isFemale,
                IsUnique = isUnique,
                IsSummonable = isSummonable,
                IsChild = isChild,
                IsLeveled = isLeveled,
                Level = level,
                TemplateFormKey = templateFormKey,
                TemplateEditorId = templateEditorId
            };
        }
        catch
        {
            return null;
        }
    }

    private List<FactionRecordViewModel> LoadFactions(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        return linkCache.WinningOverrides<IFactionGetter>()
            .Where(f => !string.IsNullOrWhiteSpace(f.EditorID))
            .Select(f => new FactionRecordViewModel(new FactionRecord(
                f.FormKey,
                f.EditorID,
                f.Name?.String,
                f.FormKey.ModKey)))
            .OrderBy(f => f.DisplayName)
            .ToList();
    }

    private List<RaceRecordViewModel> LoadRaces(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        return linkCache.WinningOverrides<IRaceGetter>()
            .Where(r => !string.IsNullOrWhiteSpace(r.EditorID))
            .Select(r => new RaceRecordViewModel(new RaceRecord(
                r.FormKey,
                r.EditorID,
                r.Name?.String,
                r.FormKey.ModKey)))
            .OrderBy(r => r.DisplayName)
            .ToList();
    }

    private List<KeywordRecordViewModel> LoadKeywords(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        return linkCache.WinningOverrides<IKeywordGetter>()
            .Where(k => !string.IsNullOrWhiteSpace(k.EditorID))
            .Select(k => new KeywordRecordViewModel(new KeywordRecord(
                k.FormKey,
                k.EditorID,
                k.FormKey.ModKey)))
            .OrderBy(k => k.DisplayName)
            .ToList();
    }

    private List<IOutfitGetter> LoadOutfits(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        return linkCache.WinningOverrides<IOutfitGetter>().ToList();
    }

    private ModKey FindOriginalMaster(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, FormKey formKey)
    {
        try
        {
            var contexts = linkCache.ResolveAllContexts<INpc, INpcGetter>(formKey);
            var firstContext = contexts.FirstOrDefault();
            if (firstContext != null)
            {
                return firstContext.ModKey;
            }
        }
        catch
        {
            // Fall through to default
        }
        return formKey.ModKey;
    }
}
