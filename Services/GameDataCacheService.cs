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
/// Supports cross-session caching for expensive data (NPCs, distribution files).
/// </summary>
public class GameDataCacheService
{
    private readonly MutagenService _mutagenService;
    private readonly DistributionDiscoveryService _discoveryService;
    private readonly NpcOutfitResolutionService _outfitResolutionService;
    private readonly CrossSessionCacheService _crossSessionCache;
    private readonly SettingsViewModel _settings;
    private readonly ILogger _logger;
    private bool _isLoaded;
    private bool _isLoading;

    public GameDataCacheService(
        MutagenService mutagenService,
        DistributionDiscoveryService discoveryService,
        NpcOutfitResolutionService outfitResolutionService,
        CrossSessionCacheService crossSessionCache,
        SettingsViewModel settings,
        ILogger logger)
    {
        _mutagenService = mutagenService;
        _discoveryService = discoveryService;
        _outfitResolutionService = outfitResolutionService;
        _crossSessionCache = crossSessionCache;
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
    /// Tries to load expensive data (NPCs, distribution files) from cross-session cache first.
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
            var dataPath = _settings.SkyrimDataPath;

            // Try to load expensive data from cross-session cache
            List<NpcFilterData>? cachedNpcData = null;
            List<DistributionFile>? cachedDistributionFiles = null;
            var usedCrossSessionCache = false;

            if (!string.IsNullOrWhiteSpace(dataPath))
            {
                var crossSessionData = await _crossSessionCache.TryLoadCacheAsync(dataPath);
                if (crossSessionData != null)
                {
                    _logger.Information("Using cross-session cache for NPCs and distribution files.");
                    cachedNpcData = crossSessionData.NpcFilterData.Select(dto => dto.FromDto()).ToList();
                    cachedDistributionFiles = crossSessionData.DistributionFiles.Select(dto => dto.FromDto()).ToList();
                    usedCrossSessionCache = true;
                }
            }

            // Load data - use cache for NPCs if available, otherwise load from Mutagen
            List<NpcFilterData> npcFilterDataList;
            List<NpcRecordViewModel> npcRecordsList;

            if (cachedNpcData != null)
            {
                npcFilterDataList = cachedNpcData;
                // Build NpcRecordViewModel from cached data
                npcRecordsList = cachedNpcData
                    .Select(npc => new NpcRecordViewModel(new NpcRecord(
                        npc.FormKey,
                        npc.EditorId,
                        npc.Name,
                        npc.SourceMod)))
                    .ToList();
                _logger.Information("*** USING CROSS-SESSION CACHE: Loaded {Count} NPCs (skipped Mutagen scan) ***", npcFilterDataList.Count);
            }
            else
            {
                // Load NPCs from Mutagen
                _logger.Information("*** FRESH LOAD: No valid cache, loading NPCs from Mutagen (this may take a while) ***");
                var npcsResult = await Task.Run(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var result = LoadNpcs(linkCache);
                    _logger.Information("[PERF] LoadNpcs: {ElapsedMs}ms ({Count} NPCs)", sw.ElapsedMilliseconds, result.Item1.Count);
                    return result;
                });
                (npcFilterDataList, npcRecordsList) = npcsResult;
            }

            // Load factions, races, keywords, outfits from Mutagen (fast enough, not cached)
            var factionsTask = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = LoadFactions(linkCache);
                _logger.Information("[PERF] LoadFactions: {ElapsedMs}ms ({Count} factions)", sw.ElapsedMilliseconds, result.Count);
                return result;
            });
            var racesTask = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = LoadRaces(linkCache);
                _logger.Information("[PERF] LoadRaces: {ElapsedMs}ms ({Count} races)", sw.ElapsedMilliseconds, result.Count);
                return result;
            });
            var keywordsTask = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = LoadKeywords(linkCache);
                _logger.Information("[PERF] LoadKeywords: {ElapsedMs}ms ({Count} keywords)", sw.ElapsedMilliseconds, result.Count);
                return result;
            });
            var outfitsTask = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = LoadOutfits(linkCache);
                _logger.Information("[PERF] LoadOutfits: {ElapsedMs}ms ({Count} outfits)", sw.ElapsedMilliseconds, result.Count);
                return result;
            });

            await Task.WhenAll(factionsTask, racesTask, keywordsTask, outfitsTask);

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

            // Load distribution data - use cache if available
            List<DistributionFile> distributionFilesForCache;
            if (cachedDistributionFiles != null)
            {
                distributionFilesForCache = cachedDistributionFiles;
                await LoadDistributionDataFromCacheAsync(cachedDistributionFiles, npcFilterDataList);
            }
            else
            {
                distributionFilesForCache = await LoadDistributionDataAsync(npcFilterDataList);
            }

            // Save to cross-session cache if we didn't use it (i.e., we loaded fresh data)
            if (!usedCrossSessionCache && !string.IsNullOrWhiteSpace(dataPath))
            {
                _ = Task.Run(async () =>
                {
                    await _crossSessionCache.SaveCacheAsync(npcFilterDataList, distributionFilesForCache, dataPath);
                });
            }

            stopwatch.Stop();
            _isLoaded = true;
            _logger.Information(
                "Game data cache loaded in {ElapsedMs}ms{CacheNote}: {NpcCount} NPCs, {FactionCount} factions, {RaceCount} races, {KeywordCount} keywords, {OutfitCount} outfits, {FileCount} distribution files, {AssignmentCount} NPC outfit assignments.",
                stopwatch.ElapsedMilliseconds,
                usedCrossSessionCache ? " (from cross-session cache)" : "",
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
    /// Invalidates the cross-session cache to force a fresh load.
    /// </summary>
    public async Task ReloadAsync()
    {
        _isLoaded = false;
        _crossSessionCache.InvalidateCache();
        await LoadAsync();
    }

    /// <summary>
    /// Ensures the cache is loaded. If already loaded, returns immediately.
    /// If currently loading, waits for completion. Does NOT invalidate the cache.
    /// Use this for initial startup when you want to use cached data if available.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded)
            return;

        if (_isLoading)
        {
            // Wait for current load to complete
            while (_isLoading)
                await Task.Delay(50);
            return;
        }

        // Not loaded and not loading - start a load
        await LoadAsync();
    }

    /// <summary>
    /// Invalidates (clears) the cross-session cache.
    /// The next load will rebuild the cache from scratch.
    /// </summary>
    public void InvalidateCrossSessionCache()
    {
        _crossSessionCache.InvalidateCache();
    }

    /// <summary>
    /// Gets information about the cross-session cache.
    /// </summary>
    public CacheInfo? GetCrossSessionCacheInfo()
    {
        return _crossSessionCache.GetCacheInfo();
    }

    /// <summary>
    /// Loads distribution files and resolves NPC outfit assignments.
    /// Returns the list of distribution files for caching.
    /// </summary>
    private async Task<List<DistributionFile>> LoadDistributionDataAsync(List<NpcFilterData> npcFilterDataList)
    {
        var dataPath = _settings.SkyrimDataPath;
        if (string.IsNullOrWhiteSpace(dataPath) || !System.IO.Directory.Exists(dataPath))
        {
            _logger.Warning("Cannot load distribution data - Skyrim data path not set or doesn't exist.");
            return [];
        }

        try
        {
            // Discover distribution files
            var discoverSw = System.Diagnostics.Stopwatch.StartNew();
            _logger.Debug("Discovering distribution files in {DataPath}...", dataPath);
            var discoveredFiles = await _discoveryService.DiscoverAsync(dataPath);
            _logger.Information("[PERF] DiscoverAsync: {ElapsedMs}ms ({Count} files)", discoverSw.ElapsedMilliseconds, discoveredFiles.Count);

            var outfitFiles = discoveredFiles
                .Where(f => f.OutfitDistributionCount > 0)
                .ToList();

            _logger.Debug("Found {Count} distribution files with outfit distributions.", outfitFiles.Count);

            // Create ViewModels for the files
            var fileViewModels = outfitFiles
                .Select(f => new DistributionFileViewModel(f))
                .ToList();

            // Resolve NPC outfit assignments
            var resolveSw = System.Diagnostics.Stopwatch.StartNew();
            _logger.Debug("Resolving NPC outfit assignments...");
            var assignments = await _outfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(
                outfitFiles,
                npcFilterDataList);
            _logger.Information("[PERF] ResolveNpcOutfitsWithFiltersAsync: {ElapsedMs}ms ({Count} assignments)", resolveSw.ElapsedMilliseconds, assignments.Count);

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

            return outfitFiles;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load distribution data.");
            return [];
        }
    }

    /// <summary>
    /// Loads distribution data from cached distribution files.
    /// </summary>
    private async Task LoadDistributionDataFromCacheAsync(List<DistributionFile> cachedFiles, List<NpcFilterData> npcFilterDataList)
    {
        try
        {
            _logger.Information("[PERF] Using {Count} cached distribution files", cachedFiles.Count);

            // Create ViewModels for the files
            var fileViewModels = cachedFiles
                .Select(f => new DistributionFileViewModel(f))
                .ToList();

            // Resolve NPC outfit assignments using cached distribution files
            var resolveSw = System.Diagnostics.Stopwatch.StartNew();
            _logger.Debug("Resolving NPC outfit assignments from cached distribution files...");
            var assignments = await _outfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(
                cachedFiles,
                npcFilterDataList);
            _logger.Information("[PERF] ResolveNpcOutfitsWithFiltersAsync (cached): {ElapsedMs}ms ({Count} assignments)",
                resolveSw.ElapsedMilliseconds, assignments.Count);

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
            _logger.Error(ex, "Failed to load distribution data from cache.");
        }
    }

    private (List<NpcFilterData>, List<NpcRecordViewModel>) LoadNpcs(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var enumSw = System.Diagnostics.Stopwatch.StartNew();
        var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
        _logger.Information("[PERF] LoadNpcs enumeration: {ElapsedMs}ms ({Count} total NPCs)", enumSw.ElapsedMilliseconds, allNpcs.Count);

        var processSw = System.Diagnostics.Stopwatch.StartNew();

        // Filter to valid NPCs first
        var validNpcs = allNpcs
            .Where(npc => npc.FormKey != FormKey.Null && !string.IsNullOrWhiteSpace(npc.EditorID))
            .ToList();

        // Use parallel processing for building filter data (LinkCache is thread-safe for reads)
        var filterDataBag = new System.Collections.Concurrent.ConcurrentBag<NpcFilterData>();
        var recordsBag = new System.Collections.Concurrent.ConcurrentBag<NpcRecordViewModel>();

        System.Threading.Tasks.Parallel.ForEach(validNpcs, npc =>
        {
            try
            {
                // Use FormKey.ModKey directly (no expensive FindOriginalMaster)
                var originalModKey = npc.FormKey.ModKey;

                // Build NpcFilterData
                var filterData = BuildNpcFilterData(npc, linkCache, originalModKey);
                if (filterData != null)
                {
                    filterDataBag.Add(filterData);
                }

                // Build simple NpcRecordViewModel
                var record = new NpcRecord(
                    npc.FormKey,
                    npc.EditorID,
                    Utilities.NpcDataExtractor.GetName(npc),
                    originalModKey);
                recordsBag.Add(new NpcRecordViewModel(record));
            }
            catch
            {
                // Silently skip failed NPCs
            }
        });

        _logger.Information("[PERF] LoadNpcs processing: {ElapsedMs}ms ({ValidCount} valid NPCs)",
            processSw.ElapsedMilliseconds, validNpcs.Count);

        return (filterDataBag.ToList(), recordsBag.ToList());
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
}
