using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using DynamicData;
using DynamicData.Kernel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using Serilog;

namespace Boutique.Services;

public class GameDataCacheService : IDisposable
{
    private readonly DistributionDiscoveryService _discoveryService;
    private readonly CompositeDisposable _disposables = new();
    private readonly GuiSettingsService _guiSettings;
    private readonly ILogger _logger;
    private readonly MutagenService _mutagenService;
    private readonly NpcOutfitResolutionService _outfitResolutionService;
    private readonly SettingsViewModel _settings;

    private readonly SourceCache<NpcFilterData, FormKey> _npcsSource = new(x => x.FormKey);
    private readonly SourceCache<FactionRecordViewModel, FormKey> _factionsSource = new(x => x.FormKey);
    private readonly SourceCache<RaceRecordViewModel, FormKey> _racesSource = new(x => x.FormKey);
    private readonly SourceCache<KeywordRecordViewModel, FormKey> _keywordsSource = new(x => x.FormKey);
    private readonly SourceCache<ClassRecordViewModel, FormKey> _classesSource = new(x => x.FormKey);
    private readonly SourceCache<IOutfitGetter, FormKey> _outfitsSource = new(x => x.FormKey);
    private readonly SourceCache<NpcRecordViewModel, FormKey> _npcRecordsSource = new(x => x.FormKey);
    private readonly SourceCache<DistributionFileViewModel, string> _distributionFilesSource = new(x => x.FullPath);
    private readonly SourceCache<NpcOutfitAssignmentViewModel, FormKey> _npcOutfitAssignmentsSource = new(x => x.NpcFormKey);

    public GameDataCacheService(
        MutagenService mutagenService,
        DistributionDiscoveryService discoveryService,
        NpcOutfitResolutionService outfitResolutionService,
        SettingsViewModel settings,
        GuiSettingsService guiSettings,
        ILogger logger)
    {
        _mutagenService = mutagenService;
        _discoveryService = discoveryService;
        _outfitResolutionService = outfitResolutionService;
        _settings = settings;
        _guiSettings = guiSettings;
        _logger = logger.ForContext<GameDataCacheService>();

        _disposables.Add(_npcsSource.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allNpcs)
            .Subscribe());
        AllNpcs = allNpcs;

        _disposables.Add(_factionsSource.Connect()
            .SortBy(x => x.DisplayName)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allFactions)
            .Subscribe());
        AllFactions = allFactions;

        _disposables.Add(_racesSource.Connect()
            .SortBy(x => x.DisplayName)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allRaces)
            .Subscribe());
        AllRaces = allRaces;

        _disposables.Add(_keywordsSource.Connect()
            .SortBy(x => x.DisplayName)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allKeywords)
            .Subscribe());
        AllKeywords = allKeywords;

        _disposables.Add(_classesSource.Connect()
            .SortBy(x => x.DisplayName)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allClasses)
            .Subscribe());
        AllClasses = allClasses;

        _disposables.Add(_outfitsSource.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allOutfits)
            .Subscribe());
        AllOutfits = allOutfits;

        _disposables.Add(_npcRecordsSource.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allNpcRecords)
            .Subscribe());
        AllNpcRecords = allNpcRecords;

        _disposables.Add(_distributionFilesSource.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allDistributionFiles)
            .Subscribe());
        AllDistributionFiles = allDistributionFiles;

        _disposables.Add(_npcOutfitAssignmentsSource.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var allNpcOutfitAssignments)
            .Subscribe());
        AllNpcOutfitAssignments = allNpcOutfitAssignments;

        _mutagenService.Initialized += OnMutagenInitialized;
    }

    private bool IsBlacklisted(ModKey modKey) =>
        _guiSettings.BlacklistedPlugins?.Any(p => p.Equals(modKey.FileName, StringComparison.OrdinalIgnoreCase)) == true;

    public bool IsLoaded { get; private set; }

    public bool IsLoading { get; private set; }

    public ReadOnlyObservableCollection<NpcFilterData> AllNpcs { get; }
    public ReadOnlyObservableCollection<FactionRecordViewModel> AllFactions { get; }
    public ReadOnlyObservableCollection<RaceRecordViewModel> AllRaces { get; }
    public ReadOnlyObservableCollection<KeywordRecordViewModel> AllKeywords { get; }
    public ReadOnlyObservableCollection<ClassRecordViewModel> AllClasses { get; }
    public ReadOnlyObservableCollection<IOutfitGetter> AllOutfits { get; }
    public ReadOnlyObservableCollection<NpcRecordViewModel> AllNpcRecords { get; }
    public ReadOnlyObservableCollection<DistributionFileViewModel> AllDistributionFiles { get; }
    public ReadOnlyObservableCollection<NpcOutfitAssignmentViewModel> AllNpcOutfitAssignments { get; }

    public Optional<NpcFilterData> LookupNpc(FormKey key) => _npcsSource.Lookup(key);
    public Optional<FactionRecordViewModel> LookupFaction(FormKey key) => _factionsSource.Lookup(key);
    public Optional<RaceRecordViewModel> LookupRace(FormKey key) => _racesSource.Lookup(key);
    public Optional<KeywordRecordViewModel> LookupKeyword(FormKey key) => _keywordsSource.Lookup(key);
    public Optional<ClassRecordViewModel> LookupClass(FormKey key) => _classesSource.Lookup(key);

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

            using var cacheProfiler = StartupProfiler.Instance.BeginOperation("GameDataCache.Load");

            List<NpcFilterData> npcFilterDataList;
            List<NpcRecordViewModel> npcRecordsList;
            using (StartupProfiler.Instance.BeginOperation("LoadNpcs", "GameDataCache.Load"))
            {
                var npcsResult = await Task.Run(() => LoadNpcs(linkCache));
                (npcFilterDataList, npcRecordsList) = npcsResult;
            }

            List<FactionRecordViewModel> factionsList;
            List<RaceRecordViewModel> racesList;
            List<KeywordRecordViewModel> keywordsList;
            List<ClassRecordViewModel> classesList;
            List<IOutfitGetter> outfitsList;

            using (StartupProfiler.Instance.BeginOperation("LoadRecordsParallel", "GameDataCache.Load"))
            {
                var factionsTask = Task.Run(() => LoadFactions(linkCache));
                var racesTask = Task.Run(() => LoadRaces(linkCache));
                var keywordsTask = Task.Run(() => LoadKeywords(linkCache));
                var classesTask = Task.Run(() => LoadClasses(linkCache));
                var outfitsTask = Task.Run(() => LoadOutfits(linkCache));

                await Task.WhenAll(factionsTask, racesTask, keywordsTask, classesTask, outfitsTask);

                factionsList = await factionsTask;
                racesList = await racesTask;
                keywordsList = await keywordsTask;
                classesList = await classesTask;
                outfitsList = await outfitsTask;
            }

            using (StartupProfiler.Instance.BeginOperation("PopulateCaches", "GameDataCache.Load"))
            {
                _npcsSource.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(npcFilterDataList);
                });

                _npcRecordsSource.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(npcRecordsList);
                });

                _factionsSource.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(factionsList);
                });

                _racesSource.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(racesList);
                });

                _keywordsSource.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(keywordsList);
                });

                _classesSource.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(classesList);
                });

                _outfitsSource.Edit(cache =>
                {
                    cache.Clear();
                    cache.AddOrUpdate(outfitsList);
                });
            }

            using (StartupProfiler.Instance.BeginOperation("LoadDistributionData", "GameDataCache.Load"))
            {
                await LoadDistributionDataAsync(npcFilterDataList);
            }

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

            StartupProfiler.Instance.Dispose();

            await Application.Current.Dispatcher.InvokeAsync(() =>
                CacheLoaded?.Invoke(this, EventArgs.Empty));
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

        var patchModKey = ModKey.FromFileName(patchFileName);
        _outfitsSource.Edit(cache =>
        {
            var existingPatchKeys = cache.Keys.Where(k => k.ModKey == patchModKey).ToList();
            cache.Remove(existingPatchKeys);
            cache.AddOrUpdate(patchOutfits);
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
                _logger.Warning(
                "Cannot ensure cache loaded - Skyrim data path not set or doesn't exist: {DataPath}",
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
            IReadOnlyList<DistributionFile> discoveredFiles;
            using (StartupProfiler.Instance.BeginOperation("DiscoverDistributionFiles", "LoadDistributionData"))
            {
                _logger.Debug("Discovering distribution files in {DataPath}...", dataPath);
                discoveredFiles = await _discoveryService.DiscoverAsync(dataPath);
            }

            List<KeywordRecordViewModel> virtualKeywords;
            using (StartupProfiler.Instance.BeginOperation("ExtractVirtualKeywords", "LoadDistributionData"))
            {
                virtualKeywords = ExtractVirtualKeywords(discoveredFiles);
                _logger.Information(
                    "Extracted {Count} virtual keywords from SPID distribution files.",
                    virtualKeywords.Count);
            }

            var outfitFiles = discoveredFiles
                .Where(f => f.OutfitDistributionCount > 0)
                .ToList();

            _logger.Debug("Found {Count} distribution files with outfit distributions.", outfitFiles.Count);

            var fileViewModels = outfitFiles
                .Select(f => new DistributionFileViewModel(f))
                .ToList();

            IReadOnlyList<NpcOutfitAssignment> assignments;
            using (StartupProfiler.Instance.BeginOperation("ResolveNpcOutfitAssignments", "LoadDistributionData"))
            {
                _logger.Debug("Resolving NPC outfit assignments...");
                assignments = await _outfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(
                    outfitFiles,
                    npcFilterDataList);
                _logger.Debug("Resolved {Count} NPC outfit assignments.", assignments.Count);
            }

            _distributionFilesSource.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(fileViewModels);
            });

            _npcOutfitAssignmentsSource.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(assignments.Select(a => new NpcOutfitAssignmentViewModel(a)));
            });

            _keywordsSource.Edit(cache =>
            {
                var existingEditorIds = cache.Items
                    .Select(k => k.EditorID)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var newKeywords = virtualKeywords
                    .Where(k => !existingEditorIds.Contains(k.EditorID ?? string.Empty));

                cache.AddOrUpdate(newKeywords);
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
                    if (existingEditorIds.Add(line.KeywordIdentifier))
                    {
                        var record = new KeywordRecord(FormKey.Null, line.KeywordIdentifier, ModKey.Null, sourceName);
                        virtualKeywords.Add(new KeywordRecordViewModel(record));
                    }
                }
            }
        }

        return virtualKeywords.OrderBy(k => k.DisplayName).ToList();
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

    private (List<NpcFilterData> filterData, List<NpcRecordViewModel> viewModels) LoadNpcs(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var validNpcs = linkCache.WinningOverrides<INpcGetter>()
            .Where(npc => npc.FormKey != FormKey.Null && !string.IsNullOrWhiteSpace(npc.EditorID))
            .Where(npc => !IsBlacklisted(npc.FormKey.ModKey))
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
            catch
            {
            }
        });

        return ([.. filterDataBag], [.. recordsBag]);
    }

    private static NpcFilterData? BuildNpcFilterData(
        INpcGetter npc,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
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

    private List<FactionRecordViewModel> LoadFactions(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        return linkCache.WinningOverrides<IFactionGetter>()
            .Where(f => !string.IsNullOrWhiteSpace(f.EditorID))
            .Where(f => !IsBlacklisted(f.FormKey.ModKey))
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
            .Where(r => !IsBlacklisted(r.FormKey.ModKey))
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
            .Where(k => !IsBlacklisted(k.FormKey.ModKey))
            .Select(k => new KeywordRecordViewModel(new KeywordRecord(
                k.FormKey,
                k.EditorID,
                k.FormKey.ModKey)))
            .OrderBy(k => k.DisplayName)
            .ToList();
    }

    private List<ClassRecordViewModel> LoadClasses(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        return linkCache.WinningOverrides<IClassGetter>()
            .Where(c => !string.IsNullOrWhiteSpace(c.EditorID))
            .Where(c => !IsBlacklisted(c.FormKey.ModKey))
            .Select(c => new ClassRecordViewModel(new ClassRecord(
                c.FormKey,
                c.EditorID,
                c.Name?.String,
                c.FormKey.ModKey)))
            .OrderBy(c => c.DisplayName)
            .ToList();
    }

    private List<IOutfitGetter> LoadOutfits(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache) =>
        linkCache.WinningOverrides<IOutfitGetter>()
            .Where(o => !IsBlacklisted(o.FormKey.ModKey))
            .ToList();

    public void Dispose()
    {
        _mutagenService.Initialized -= OnMutagenInitialized;
        _disposables.Dispose();
        _npcsSource.Dispose();
        _factionsSource.Dispose();
        _racesSource.Dispose();
        _keywordsSource.Dispose();
        _classesSource.Dispose();
        _outfitsSource.Dispose();
        _npcRecordsSource.Dispose();
        _distributionFilesSource.Dispose();
        _npcOutfitAssignmentsSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
