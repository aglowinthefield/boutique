using System.Collections.ObjectModel;
using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

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
        _mutagenService.Initialized += OnMutagenInitialized;
    }

    public bool IsLoaded => _isLoaded;
    public bool IsLoading => _isLoading;
    public event EventHandler? CacheLoaded;

    public ObservableCollection<NpcFilterData> AllNpcs { get; } = [];
    public ObservableCollection<FactionRecordViewModel> AllFactions { get; } = [];
    public ObservableCollection<RaceRecordViewModel> AllRaces { get; } = [];
    public ObservableCollection<KeywordRecordViewModel> AllKeywords { get; } = [];
    public ObservableCollection<ClassRecordViewModel> AllClasses { get; } = [];
    public ObservableCollection<IOutfitGetter> AllOutfits { get; } = [];
    public ObservableCollection<NpcRecordViewModel> AllNpcRecords { get; } = [];
    public ObservableCollection<DistributionFileViewModel> AllDistributionFiles { get; } = [];
    public ObservableCollection<NpcOutfitAssignmentViewModel> AllNpcOutfitAssignments { get; } = [];

    public Dictionary<FormKey, NpcFilterData> NpcsByFormKey { get; } = [];
    public Dictionary<FormKey, FactionRecordViewModel> FactionsByFormKey { get; } = [];
    public Dictionary<FormKey, RaceRecordViewModel> RacesByFormKey { get; } = [];
    public Dictionary<FormKey, KeywordRecordViewModel> KeywordsByFormKey { get; } = [];
    public Dictionary<FormKey, ClassRecordViewModel> ClassesByFormKey { get; } = [];

    private async void OnMutagenInitialized(object? sender, EventArgs e)
    {
        _logger.Information("MutagenService initialized, loading game data cache...");
        await LoadAsync();
    }

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

        if (_mutagenService.LinkCache is not { } linkCache)
        {
            _logger.Warning("Cannot load cache - LinkCache not available.");
            return;
        }

        try
        {
            _isLoading = true;
            _logger.Information("Loading game data cache...");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var npcsResult = await Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = LoadNpcs(linkCache);
                _logger.Information("[PERF] LoadNpcs: {ElapsedMs}ms ({Count} NPCs)", sw.ElapsedMilliseconds, result.Item1.Count);
                return result;
            });
            var (npcFilterDataList, npcRecordsList) = npcsResult;

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
            var classesTask = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = LoadClasses(linkCache);
                _logger.Information("[PERF] LoadClasses: {ElapsedMs}ms ({Count} classes)", sw.ElapsedMilliseconds, result.Count);
                return result;
            });
            var outfitsTask = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = LoadOutfits(linkCache);
                _logger.Information("[PERF] LoadOutfits: {ElapsedMs}ms ({Count} outfits)", sw.ElapsedMilliseconds, result.Count);
                return result;
            });

            await Task.WhenAll(factionsTask, racesTask, keywordsTask, classesTask, outfitsTask);

            var factionsList = await factionsTask;
            var racesList = await racesTask;
            var keywordsList = await keywordsTask;
            var classesList = await classesTask;
            var outfitsList = await outfitsTask;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
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

                AllFactions.Clear();
                FactionsByFormKey.Clear();
                foreach (var faction in factionsList)
                {
                    AllFactions.Add(faction);
                    FactionsByFormKey[faction.FormKey] = faction;
                }

                AllRaces.Clear();
                RacesByFormKey.Clear();
                foreach (var race in racesList)
                {
                    AllRaces.Add(race);
                    RacesByFormKey[race.FormKey] = race;
                }

                AllKeywords.Clear();
                KeywordsByFormKey.Clear();
                foreach (var keyword in keywordsList)
                {
                    AllKeywords.Add(keyword);
                    KeywordsByFormKey[keyword.FormKey] = keyword;
                }

                AllClasses.Clear();
                ClassesByFormKey.Clear();
                foreach (var classVm in classesList)
                {
                    AllClasses.Add(classVm);
                    ClassesByFormKey[classVm.FormKey] = classVm;
                }

                AllOutfits.Clear();
                foreach (var outfit in outfitsList)
                {
                    AllOutfits.Add(outfit);
                }
            });

            await LoadDistributionDataAsync(npcFilterDataList);

            stopwatch.Stop();
            _isLoaded = true;
            _logger.Information(
                "Game data cache loaded in {ElapsedMs}ms: {NpcCount} NPCs, {FactionCount} factions, {RaceCount} races, {ClassCount} classes, {KeywordCount} keywords, {OutfitCount} outfits, {FileCount} distribution files, {AssignmentCount} NPC outfit assignments.",
                stopwatch.ElapsedMilliseconds,
                npcFilterDataList.Count,
                factionsList.Count,
                racesList.Count,
                classesList.Count,
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

    public async Task ReloadAsync()
    {
        _isLoaded = false;
        await _mutagenService.RefreshLinkCacheAsync(_settings.PatchFileName);
        await LoadAsync();
    }

    /// <summary>
    /// Refreshes only the outfits from the output patch file without reloading the entire LinkCache.
    /// Much faster than a full ReloadAsync when only outfit changes need to be reflected.
    /// </summary>
    public async Task RefreshOutfitsFromPatchAsync()
    {
        var patchFileName = _settings.PatchFileName;
        if (string.IsNullOrEmpty(patchFileName))
            return;

        var patchOutfits = (await _mutagenService.LoadOutfitsFromPluginAsync(patchFileName)).ToList();
        if (patchOutfits.Count == 0)
            return;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var patchModKey = Mutagen.Bethesda.Plugins.ModKey.FromFileName(patchFileName);
            var existingPatchOutfits = AllOutfits.Where(o => o.FormKey.ModKey == patchModKey).ToList();
            foreach (var outfit in existingPatchOutfits)
            {
                AllOutfits.Remove(outfit);
            }

            foreach (var outfit in patchOutfits)
            {
                AllOutfits.Add(outfit);
            }
        });

        _logger.Information("Refreshed {Count} outfit(s) from patch file {Patch}.", patchOutfits.Count, patchFileName);
    }

    public async Task EnsureLoadedAsync()
    {
        if (_isLoaded)
            return;

        if (_isLoading)
        {
            while (_isLoading)
                await Task.Delay(50);
            return;
        }

        if (!_mutagenService.IsInitialized)
        {
            var dataPath = _settings.SkyrimDataPath;
            if (string.IsNullOrWhiteSpace(dataPath) || !System.IO.Directory.Exists(dataPath))
            {
                _logger.Warning("Cannot ensure cache loaded - Skyrim data path not set or doesn't exist: {DataPath}", dataPath);
                return;
            }

            _logger.Information("Initializing MutagenService for cache load...");
            await _mutagenService.InitializeAsync(dataPath);
        }

        await LoadAsync();
    }

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
            var discoverSw = System.Diagnostics.Stopwatch.StartNew();
            _logger.Debug("Discovering distribution files in {DataPath}...", dataPath);
            var discoveredFiles = await _discoveryService.DiscoverAsync(dataPath);
            _logger.Information("[PERF] DiscoverAsync: {ElapsedMs}ms ({Count} files)", discoverSw.ElapsedMilliseconds, discoveredFiles.Count);

            var outfitFiles = discoveredFiles
                .Where(f => f.OutfitDistributionCount > 0)
                .ToList();

            _logger.Debug("Found {Count} distribution files with outfit distributions.", outfitFiles.Count);

            var fileViewModels = outfitFiles
                .Select(f => new DistributionFileViewModel(f))
                .ToList();

            var resolveSw = System.Diagnostics.Stopwatch.StartNew();
            _logger.Debug("Resolving NPC outfit assignments...");
            var assignments = await _outfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(
                outfitFiles,
                npcFilterDataList);
            _logger.Information("[PERF] ResolveNpcOutfitsWithFiltersAsync: {ElapsedMs}ms ({Count} assignments)", resolveSw.ElapsedMilliseconds, assignments.Count);

            _logger.Debug("Resolved {Count} NPC outfit assignments.", assignments.Count);

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
        var enumSw = System.Diagnostics.Stopwatch.StartNew();
        var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
        _logger.Information("[PERF] LoadNpcs enumeration: {ElapsedMs}ms ({Count} total NPCs)", enumSw.ElapsedMilliseconds, allNpcs.Count);

        var processSw = System.Diagnostics.Stopwatch.StartNew();
        var validNpcs = allNpcs
            .Where(npc => npc.FormKey != FormKey.Null && !string.IsNullOrWhiteSpace(npc.EditorID))
            .ToList();

        var filterDataBag = new System.Collections.Concurrent.ConcurrentBag<NpcFilterData>();
        var recordsBag = new System.Collections.Concurrent.ConcurrentBag<NpcRecordViewModel>();

        System.Threading.Tasks.Parallel.ForEach(validNpcs, npc =>
        {
            try
            {
                var originalModKey = npc.FormKey.ModKey;
                var filterData = BuildNpcFilterData(npc, linkCache, originalModKey);
                if (filterData != null)
                {
                    filterDataBag.Add(filterData);
                }

                var record = new NpcRecord(
                    npc.FormKey,
                    npc.EditorID,
                    Utilities.NpcDataExtractor.GetName(npc),
                    originalModKey);
                recordsBag.Add(new NpcRecordViewModel(record));
            }
            catch
            {
            }
        });

        _logger.Information("[PERF] LoadNpcs processing: {ElapsedMs}ms ({ValidCount} valid NPCs)",
            processSw.ElapsedMilliseconds, validNpcs.Count);

        return ([.. filterDataBag], [.. recordsBag]);
    }

    private static NpcFilterData? BuildNpcFilterData(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, ModKey originalModKey)
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
            var skillValues = Utilities.NpcDataExtractor.ExtractSkillValues(npc);

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
                TemplateEditorId = templateEditorId,
                SkillValues = skillValues
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<FactionRecordViewModel> LoadFactions(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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

    private static List<RaceRecordViewModel> LoadRaces(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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

    private static List<KeywordRecordViewModel> LoadKeywords(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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

    private static List<ClassRecordViewModel> LoadClasses(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        return linkCache.WinningOverrides<IClassGetter>()
            .Where(c => !string.IsNullOrWhiteSpace(c.EditorID))
            .Select(c => new ClassRecordViewModel(new ClassRecord(
                c.FormKey,
                c.EditorID,
                c.Name?.String,
                c.FormKey.ModKey)))
            .OrderBy(c => c.DisplayName)
            .ToList();
    }

    private static List<IOutfitGetter> LoadOutfits(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache) => linkCache.WinningOverrides<IOutfitGetter>().ToList();
}
