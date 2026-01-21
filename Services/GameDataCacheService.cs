using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class GameDataCacheService
{
    private readonly DistributionDiscoveryService _discoveryService;
    private readonly ILogger _logger;
    private readonly MutagenService _mutagenService;
    private readonly NpcOutfitResolutionService _outfitResolutionService;
    private readonly SettingsViewModel _settings;

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

    public bool IsLoaded { get; private set; }

    public bool IsLoading { get; private set; }

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
    public event EventHandler? CacheLoaded;

    private async void OnMutagenInitialized(object? sender, EventArgs e)
    {
        _logger.Information("MutagenService initialized, loading game data cache...");
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (IsLoading)
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
            IsLoading = true;
            _logger.Information("Loading game data cache...");

            var npcsResult = await Task.Run(() => LoadNpcs(linkCache));
            var (npcFilterDataList, npcRecordsList) = npcsResult;

            var factionsTask = Task.Run(() => LoadFactions(linkCache));
            var racesTask = Task.Run(() => LoadRaces(linkCache));
            var keywordsTask = Task.Run(() => LoadKeywords(linkCache));
            var classesTask = Task.Run(() => LoadClasses(linkCache));
            var outfitsTask = Task.Run(() => LoadOutfits(linkCache));

            await Task.WhenAll(factionsTask, racesTask, keywordsTask, classesTask, outfitsTask);

            var factionsList = await factionsTask;
            var racesList = await racesTask;
            var keywordsList = await keywordsTask;
            var classesList = await classesTask;
            var outfitsList = await outfitsTask;

            await Application.Current.Dispatcher.InvokeAsync(() =>
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

            IsLoaded = true;
            _logger.Information(
                "Game data cache loaded: {NpcCount} NPCs, {FactionCount} factions, {RaceCount} races, {ClassCount} classes, {KeywordCount} keywords, {OutfitCount} outfits, {FileCount} distribution files, {AssignmentCount} NPC outfit assignments.",
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
            IsLoading = false;
        }
    }

    public async Task ReloadAsync()
    {
        IsLoaded = false;
        await _mutagenService.RefreshLinkCacheAsync(_settings.PatchFileName);
        await LoadAsync();
    }

    /// <summary>
    ///     Refreshes only the outfits from the output patch file without reloading the entire LinkCache.
    ///     Much faster than a full ReloadAsync when only outfit changes need to be reflected.
    /// </summary>
    public async Task RefreshOutfitsFromPatchAsync()
    {
        var patchFileName = _settings.PatchFileName;
        if (string.IsNullOrEmpty(patchFileName))
        {
            return;
        }

        var patchOutfits = (await _mutagenService.LoadOutfitsFromPluginAsync(patchFileName)).ToList();
        if (patchOutfits.Count == 0)
        {
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var patchModKey = ModKey.FromFileName(patchFileName);
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
        if (IsLoaded)
        {
            return;
        }

        if (IsLoading)
        {
            while (IsLoading)
            {
                await Task.Delay(50);
            }

            return;
        }

        if (!_mutagenService.IsInitialized)
        {
            var dataPath = _settings.SkyrimDataPath;
            if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
            {
                _logger.Warning("Cannot ensure cache loaded - Skyrim data path not set or doesn't exist: {DataPath}",
                    dataPath);
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
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
        {
            _logger.Warning("Cannot load distribution data - Skyrim data path not set or doesn't exist.");
            return;
        }

        try
        {
            _logger.Debug("Discovering distribution files in {DataPath}...", dataPath);
            var discoveredFiles = await _discoveryService.DiscoverAsync(dataPath);

            var virtualKeywords = ExtractVirtualKeywords(discoveredFiles);
            _logger.Information("Extracted {Count} virtual keywords from SPID distribution files.",
                virtualKeywords.Count);

            var outfitFiles = discoveredFiles
                .Where(f => f.OutfitDistributionCount > 0)
                .ToList();

            _logger.Debug("Found {Count} distribution files with outfit distributions.", outfitFiles.Count);

            var fileViewModels = outfitFiles
                .Select(f => new DistributionFileViewModel(f))
                .ToList();

            _logger.Debug("Resolving NPC outfit assignments...");
            var assignments = await _outfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(
                outfitFiles,
                npcFilterDataList);

            _logger.Debug("Resolved {Count} NPC outfit assignments.", assignments.Count);

            await Application.Current.Dispatcher.InvokeAsync(() =>
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

                foreach (var keyword in virtualKeywords)
                {
                    if (!AllKeywords.Any(k =>
                            string.Equals(k.EditorID, keyword.EditorID, StringComparison.OrdinalIgnoreCase)))
                    {
                        AllKeywords.Add(keyword);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load distribution data.");
        }
    }

    private static List<KeywordRecordViewModel> ExtractVirtualKeywords(IReadOnlyList<DistributionFile> discoveredFiles)
    {
        var existingEditorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var virtualKeywords = new List<KeywordRecordViewModel>();

        foreach (var file in discoveredFiles)
        {
            if (file.Type != DistributionFileType.Spid)
            {
                continue;
            }

            var sourceName = ExtractModFolderName(file.FullPath);

            foreach (var line in file.Lines)
            {
                if (line.IsKeywordDistribution && !string.IsNullOrWhiteSpace(line.KeywordIdentifier))
                {
                    AddKeywordIfNew(line.KeywordIdentifier, sourceName);
                }

                if ((line.IsKeywordDistribution || line.IsOutfitDistribution) &&
                    SpidLineParser.TryParse(line.RawText, out var filter) && filter != null)
                {
                    foreach (var keyword in GetAllKeywordIdentifiers(filter))
                    {
                        AddKeywordIfNew(keyword, sourceName);
                    }
                }
            }
        }

        return virtualKeywords.OrderBy(k => k.DisplayName).ToList();

        void AddKeywordIfNew(string editorId, string? source)
        {
            if (existingEditorIds.Add(editorId))
            {
                var record = new KeywordRecord(FormKey.Null, editorId, ModKey.Null, source);
                virtualKeywords.Add(new KeywordRecordViewModel(record));
            }
        }
    }

    private static string? ExtractModFolderName(string fullPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(fullPath);
        if (!string.IsNullOrEmpty(fileName))
        {
            var distrSuffix = "_DISTR";
            if (fileName.EndsWith(distrSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^distrSuffix.Length];
            }
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            return fileName;
        }

        return new DirectoryInfo(directory).Name;
    }

    private static IEnumerable<string> GetAllKeywordIdentifiers(SpidDistributionFilter filter)
    {
        foreach (var expr in filter.StringFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.LooksLikeKeyword)
                {
                    yield return part.Value;
                }
            }
        }
    }

    private (List<NpcFilterData>, List<NpcRecordViewModel>) LoadNpcs(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();

        var validNpcs = allNpcs
            .Where(npc => npc.FormKey != FormKey.Null && !string.IsNullOrWhiteSpace(npc.EditorID))
            .ToList();

        var filterDataBag = new ConcurrentBag<NpcFilterData>();
        var recordsBag = new ConcurrentBag<NpcRecordViewModel>();

        Parallel.ForEach(validNpcs, npc =>
        {
            try
            {
                var originalModKey = npc.FormKey.ModKey;
                var filterData = BuildNpcFilterData(npc, linkCache, originalModKey);
                if (filterData is not null)
                {
                    filterDataBag.Add(filterData);
                }

                var record = new NpcRecord(
                    npc.FormKey,
                    npc.EditorID,
                    NpcDataExtractor.GetName(npc),
                    originalModKey);
                recordsBag.Add(new NpcRecordViewModel(record));
            }
            catch { }
        });

        return ([.. filterDataBag], [.. recordsBag]);
    }

    private static NpcFilterData? BuildNpcFilterData(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ModKey originalModKey)
    {
        try
        {
            var keywords = NpcDataExtractor.ExtractKeywords(npc, linkCache);
            var factions = NpcDataExtractor.ExtractFactions(npc, linkCache);
            var (raceFormKey, raceEditorId) = NpcDataExtractor.ExtractRace(npc, linkCache);
            var (classFormKey, classEditorId) = NpcDataExtractor.ExtractClass(npc, linkCache);
            var (combatStyleFormKey, combatStyleEditorId) = NpcDataExtractor.ExtractCombatStyle(npc, linkCache);
            var (voiceTypeFormKey, voiceTypeEditorId) = NpcDataExtractor.ExtractVoiceType(npc, linkCache);
            var (outfitFormKey, outfitEditorId) = NpcDataExtractor.ExtractDefaultOutfit(npc, linkCache);
            var (templateFormKey, templateEditorId) = NpcDataExtractor.ExtractTemplate(npc, linkCache);
            var (isFemale, isUnique, isSummonable, isLeveled) = NpcDataExtractor.ExtractTraits(npc);
            var isChild = NpcDataExtractor.IsChildRace(raceEditorId);
            var level = NpcDataExtractor.ExtractLevel(npc);
            var skillValues = NpcDataExtractor.ExtractSkillValues(npc);

            return new NpcFilterData
            {
                FormKey = npc.FormKey,
                EditorId = npc.EditorID,
                Name = NpcDataExtractor.GetName(npc),
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

    private static List<IOutfitGetter> LoadOutfits(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache) =>
        linkCache.WinningOverrides<IOutfitGetter>().ToList();
}
