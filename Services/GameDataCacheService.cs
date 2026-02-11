using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using Boutique.Models;
using Boutique.Services.GameData;
using Boutique.Utilities;
using Boutique.ViewModels;
using DynamicData;
using DynamicData.Kernel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using Serilog;

namespace Boutique.Services;

public class GameDataCacheService : IDisposable
{
  private readonly HashSet<string> _blacklistedPluginsSet = new(StringComparer.OrdinalIgnoreCase);
  private readonly SourceCache<ClassRecordViewModel, FormKey> _classesSource = new(x => x.FormKey);
  private readonly ContainerDataBuilder _containerDataBuilder;
  private readonly SourceCache<ContainerRecordViewModel, FormKey> _containersSource = new(x => x.FormKey);
  private readonly DistributionScannerService _discoveryService;
  private readonly CompositeDisposable _disposables = [];
  private readonly SourceCache<DistributionFileViewModel, string> _distributionFilesSource = new(x => x.FullPath);
  private readonly SourceCache<FactionRecordViewModel, FormKey> _factionsSource = new(x => x.FormKey);
  private readonly SourceCache<LocationRecordViewModel, FormKey> _locationsSource = new(x => x.FormKey);
  private readonly GuiSettingsService _guiSettings;

  private readonly SourceCache<KeywordRecordViewModel, string> _keywordsSource = new(x => x.EditorID);

  private readonly ILogger        _logger;
  private readonly MutagenService _mutagenService;

  private readonly SourceCache<NpcOutfitAssignmentViewModel, FormKey> _npcOutfitAssignmentsSource =
    new(x => x.NpcFormKey);

  private readonly SourceCache<NpcRecordViewModel, FormKey> _npcRecordsSource = new(x => x.FormKey);

  private readonly SourceCache<NpcFilterData, FormKey>         _npcsSource          = new(x => x.FormKey);
  private readonly SourceCache<OutfitRecordViewModel, FormKey> _outfitRecordsSource = new(x => x.FormKey);
  private readonly NpcOutfitResolutionService                  _outfitResolutionService;
  private readonly SourceCache<IOutfitGetter, FormKey>         _outfitsSource = new(x => x.FormKey);
  private readonly SourceCache<RaceRecordViewModel, FormKey>   _racesSource   = new(x => x.FormKey);
  private readonly SettingsViewModel                           _settings;

  public GameDataCacheService(
    MutagenService mutagenService,
    DistributionScannerService discoveryService,
    NpcOutfitResolutionService outfitResolutionService,
    SettingsViewModel settings,
    GuiSettingsService guiSettings,
    ILogger logger)
  {
    _mutagenService          = mutagenService;
    _discoveryService        = discoveryService;
    _outfitResolutionService = outfitResolutionService;
    _settings                = settings;
    _guiSettings             = guiSettings;
    _logger                  = logger.ForContext<GameDataCacheService>();
    _containerDataBuilder    = new ContainerDataBuilder(logger);

    _disposables.Add(
      _npcsSource.Connect()
                 .ObserveOn(RxApp.MainThreadScheduler)
                 .Bind(out var allNpcs)
                 .Subscribe());
    AllNpcs = allNpcs;

    _disposables.Add(
      _factionsSource.Connect()
                     .SortBy(x => x.DisplayName)
                     .ObserveOn(RxApp.MainThreadScheduler)
                     .Bind(out var allFactions)
                     .Subscribe());
    AllFactions = allFactions;

    _disposables.Add(
      _racesSource.Connect()
                  .SortBy(x => x.DisplayName)
                  .ObserveOn(RxApp.MainThreadScheduler)
                  .Bind(out var allRaces)
                  .Subscribe());
    AllRaces = allRaces;

    _disposables.Add(
      _keywordsSource.Connect()
                     .SortBy(x => x.DisplayName)
                     .ObserveOn(RxApp.MainThreadScheduler)
                     .Bind(out var allKeywords)
                     .Subscribe());
    AllKeywords = allKeywords;

    _disposables.Add(
      _classesSource.Connect()
                    .SortBy(x => x.DisplayName)
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Bind(out var allClasses)
                    .Subscribe());
    AllClasses = allClasses;

    _disposables.Add(
      _locationsSource.Connect()
                      .SortBy(x => x.DisplayName)
                      .ObserveOn(RxApp.MainThreadScheduler)
                      .Bind(out var allLocations)
                      .Subscribe());
    AllLocations = allLocations;

    _disposables.Add(
      _outfitsSource.Connect()
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Bind(out var allOutfits)
                    .Subscribe());
    AllOutfits = allOutfits;

    _disposables.Add(
      _outfitRecordsSource.Connect()
                          .SortBy(x => x.EditorID)
                          .ObserveOn(RxApp.MainThreadScheduler)
                          .Bind(out var allOutfitRecords)
                          .Subscribe());
    AllOutfitRecords = allOutfitRecords;

    _disposables.Add(
      _npcRecordsSource.Connect()
                       .ObserveOn(RxApp.MainThreadScheduler)
                       .Bind(out var allNpcRecords)
                       .Subscribe());
    AllNpcRecords = allNpcRecords;

    _disposables.Add(
      _containersSource.Connect()
                       .ObserveOn(RxApp.MainThreadScheduler)
                       .Bind(out var allContainers)
                       .Subscribe());
    AllContainers = allContainers;

    _disposables.Add(
      _distributionFilesSource.Connect()
                              .ObserveOn(RxApp.MainThreadScheduler)
                              .Bind(out var allDistributionFiles)
                              .Subscribe());
    AllDistributionFiles = allDistributionFiles;

    _disposables.Add(
      _npcOutfitAssignmentsSource.Connect()
                                 .ObserveOn(RxApp.MainThreadScheduler)
                                 .Bind(out var allNpcOutfitAssignments)
                                 .Subscribe());
    AllNpcOutfitAssignments = allNpcOutfitAssignments;

    _mutagenService.Initialized += OnMutagenInitialized;
  }

  public bool IsLoaded { get; private set; }

  public bool IsLoading { get; private set; }

  public ReadOnlyObservableCollection<NpcFilterData> AllNpcs { get; }
  public ReadOnlyObservableCollection<FactionRecordViewModel> AllFactions { get; }
  public ReadOnlyObservableCollection<RaceRecordViewModel> AllRaces { get; }
  public ReadOnlyObservableCollection<KeywordRecordViewModel> AllKeywords { get; }
  public ReadOnlyObservableCollection<ClassRecordViewModel> AllClasses { get; }
  public ReadOnlyObservableCollection<LocationRecordViewModel> AllLocations { get; }
  public ReadOnlyObservableCollection<IOutfitGetter> AllOutfits { get; }
  public ReadOnlyObservableCollection<OutfitRecordViewModel> AllOutfitRecords { get; }
  public ReadOnlyObservableCollection<NpcRecordViewModel> AllNpcRecords { get; }
  public ReadOnlyObservableCollection<ContainerRecordViewModel> AllContainers { get; }
  public ReadOnlyObservableCollection<DistributionFileViewModel> AllDistributionFiles { get; }
  public ReadOnlyObservableCollection<NpcOutfitAssignmentViewModel> AllNpcOutfitAssignments { get; }

  public void Dispose()
  {
    _mutagenService.Initialized -= OnMutagenInitialized;
    _disposables.Dispose();
    _npcsSource.Dispose();
    _factionsSource.Dispose();
    _racesSource.Dispose();
    _keywordsSource.Dispose();
    _classesSource.Dispose();
    _locationsSource.Dispose();
    _outfitsSource.Dispose();
    _outfitRecordsSource.Dispose();
    _npcRecordsSource.Dispose();
    _containersSource.Dispose();
    _distributionFilesSource.Dispose();
    _npcOutfitAssignmentsSource.Dispose();
    GC.SuppressFinalize(this);
  }

  private bool IsBlacklisted(ModKey modKey) =>
    _blacklistedPluginsSet.Contains(modKey.FileName);

  public Optional<NpcFilterData> LookupNpc(FormKey key) => _npcsSource.Lookup(key);

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

      _blacklistedPluginsSet.Clear();
      if (_guiSettings.BlacklistedPlugins != null)
      {
        foreach (var plugin in _guiSettings.BlacklistedPlugins)
        {
          _blacklistedPluginsSet.Add(plugin);
        }
      }

      List<FactionRecordViewModel>   factionsList;
      List<RaceRecordViewModel>      racesList;
      List<KeywordRecordViewModel>   keywordsList;
      List<ClassRecordViewModel>     classesList;
      List<LocationRecordViewModel>  locationsList;
      List<IOutfitGetter>            outfitsList;
      List<ContainerRecordViewModel> containersList;

      var factionsTask  = Task.Run(() => RecordLoaders.LoadFactions(linkCache, IsBlacklisted));
      var racesTask     = Task.Run(() => RecordLoaders.LoadRaces(linkCache, IsBlacklisted));
      var keywordsTask  = Task.Run(() => RecordLoaders.LoadKeywords(linkCache, IsBlacklisted));
      var classesTask   = Task.Run(() => RecordLoaders.LoadClasses(linkCache, IsBlacklisted));
      var locationsTask = Task.Run(() => RecordLoaders.LoadLocations(linkCache, IsBlacklisted));
      var outfitsTask   = Task.Run(() => RecordLoaders.LoadOutfits(linkCache, IsBlacklisted));

      if (_guiSettings.ShowContainersTab)
      {
        var containersTask = Task.Run(() => _containerDataBuilder.LoadContainers(linkCache, IsBlacklisted));
        await Task.WhenAll(
                    factionsTask,
                    racesTask,
                    keywordsTask,
                    classesTask,
                    locationsTask,
                    outfitsTask,
                    containersTask)
                  .ContinueWith(_ => { });
        factionsList   = await SafeAwaitAsync(factionsTask, "Factions");
        racesList      = await SafeAwaitAsync(racesTask, "Races");
        keywordsList   = await SafeAwaitAsync(keywordsTask, "Keywords");
        classesList    = await SafeAwaitAsync(classesTask, "Classes");
        locationsList  = await SafeAwaitAsync(locationsTask, "Locations");
        outfitsList    = await SafeAwaitAsync(outfitsTask, "Outfits");
        containersList = await SafeAwaitAsync(containersTask, "Containers");
      }
      else
      {
        await Task.WhenAll(factionsTask, racesTask, keywordsTask, classesTask, locationsTask, outfitsTask)
                  .ContinueWith(_ => { });
        factionsList   = await SafeAwaitAsync(factionsTask, "Factions");
        racesList      = await SafeAwaitAsync(racesTask, "Races");
        keywordsList   = await SafeAwaitAsync(keywordsTask, "Keywords");
        classesList    = await SafeAwaitAsync(classesTask, "Classes");
        locationsList  = await SafeAwaitAsync(locationsTask, "Locations");
        outfitsList    = await SafeAwaitAsync(outfitsTask, "Outfits");
        containersList = [];
      }

      var keywordLookup = keywordsList.ToDictionary(k => k.FormKey, k => k.EditorID);
      var factionLookup = factionsList.ToDictionary(f => f.FormKey, f => f.DisplayName);
      var raceLookup    = racesList.ToDictionary(r => r.FormKey, r => r.DisplayName);
      var classLookup   = classesList.ToDictionary(c => c.FormKey, c => c.DisplayName);
      var outfitLookup  = outfitsList.ToDictionary(o => o.FormKey, o => o.EditorID ?? string.Empty);

      var raceKeywordLookup = new Dictionary<FormKey, HashSet<string>>();
      foreach (var race in linkCache.WinningOverrides<IRaceGetter>())
      {
        RecordProcessingHelper.TryProcessRecord(
          _logger,
          race,
          () =>
          {
            if (race.Keywords == null)
            {
              return;
            }

            var keywordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kw in race.Keywords)
            {
              if (keywordLookup.TryGetValue(kw.FormKey, out var editorId))
              {
                keywordSet.Add(editorId);
              }
            }

            raceKeywordLookup.TryAdd(race.FormKey, keywordSet);
          },
          "race");
      }

      var templateLookup = new Dictionary<FormKey, string>();
      foreach (var npc in linkCache.WinningOverrides<INpcGetter>())
      {
        RecordProcessingHelper.TryProcessRecord(
          _logger,
          npc,
          () =>
          {
            if (!string.IsNullOrWhiteSpace(npc.EditorID))
            {
              templateLookup.TryAdd(npc.FormKey, npc.EditorID);
            }
          },
          "NPC");
      }

      foreach (var lvln in linkCache.WinningOverrides<ILeveledNpcGetter>())
      {
        RecordProcessingHelper.TryProcessRecord(
          _logger,
          lvln,
          () =>
          {
            if (!string.IsNullOrWhiteSpace(lvln.EditorID))
            {
              templateLookup.TryAdd(lvln.FormKey, lvln.EditorID);
            }
          },
          "leveled NPC");
      }

      var combatStyleLookup = new Dictionary<FormKey, string>();
      foreach (var cs in linkCache.WinningOverrides<ICombatStyleGetter>())
      {
        if (!string.IsNullOrWhiteSpace(cs.EditorID))
        {
          combatStyleLookup.TryAdd(cs.FormKey, cs.EditorID);
        }
      }

      var voiceTypeLookup = new Dictionary<FormKey, string>();
      foreach (var vt in linkCache.WinningOverrides<IVoiceTypeGetter>())
      {
        if (!string.IsNullOrWhiteSpace(vt.EditorID))
        {
          voiceTypeLookup.TryAdd(vt.FormKey, vt.EditorID);
        }
      }

      var npcsResult = await Task.Run(() => NpcDataBuilder.LoadNpcs(
                                        linkCache,
                                        keywordLookup,
                                        factionLookup,
                                        raceLookup,
                                        classLookup,
                                        outfitLookup,
                                        templateLookup,
                                        combatStyleLookup,
                                        voiceTypeLookup,
                                        raceKeywordLookup,
                                        IsBlacklisted));
      var (npcFilterDataList, npcRecordsList) = npcsResult;

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

      _locationsSource.Edit(cache =>
      {
        cache.Clear();
        cache.AddOrUpdate(locationsList);
      });

      _outfitsSource.Edit(cache =>
      {
        cache.Clear();
        cache.AddOrUpdate(outfitsList);
      });

      _outfitRecordsSource.Edit(cache =>
      {
        cache.Clear();
        cache.AddOrUpdate(outfitsList.Select(o => new OutfitRecordViewModel(o)));
      });

      _containersSource.Edit(cache =>
      {
        cache.Clear();
        cache.AddOrUpdate(containersList);
      });

      await LoadDistributionDataAsync(npcFilterDataList);

      IsLoaded = true;
      var logMessage = _guiSettings.ShowContainersTab
                         ? "Game data cache loaded: {NpcCount} NPCs, {FactionCount} factions, {RaceCount} races, {ClassCount} classes, {LocationCount} locations, {KeywordCount} keywords, {OutfitCount} outfits, {ContainerCount} containers, {FileCount} distribution files, {AssignmentCount} NPC outfit assignments."
                         : "Game data cache loaded: {NpcCount} NPCs, {FactionCount} factions, {RaceCount} races, {ClassCount} classes, {LocationCount} locations, {KeywordCount} keywords, {OutfitCount} outfits, {FileCount} distribution files, {AssignmentCount} NPC outfit assignments. (Containers skipped - feature disabled)";
      _logger.Information(
        logMessage,
        npcFilterDataList.Count,
        factionsList.Count,
        racesList.Count,
        classesList.Count,
        locationsList.Count,
        keywordsList.Count,
        outfitsList.Count,
        containersList.Count,
        AllDistributionFiles.Count,
        AllNpcOutfitAssignments.Count);

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

  private async Task<T> SafeAwaitAsync<T>(Task<T> task, string taskName)
    where T : new()
  {
    try
    {
      return await task;
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to load {TaskName} data. Returning empty collection.", taskName);
      return new T();
    }
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
      _logger.Information(
        "Extracted {Count} virtual keywords from SPID distribution files.",
        virtualKeywords.Count);

      var outfitFiles = discoveredFiles
                        .Where(f => f.OutfitDistributionCount > 0)
                        .ToList();

      _logger.Debug("Found {Count} distribution files with outfit distributions.", outfitFiles.Count);

      // Include ALL discovered files in the dropdown (both outfit and keyword-only files)
      var allFileViewModels = discoveredFiles
                              .Select(f => new DistributionFileViewModel(f))
                              .ToList();

      _logger.Debug("Resolving NPC outfit assignments...");
      var assignments = await _outfitResolutionService.ResolveNpcOutfitsWithFiltersAsync(
                          outfitFiles,
                          npcFilterDataList);
      _logger.Debug("Resolved {Count} NPC outfit assignments.", assignments.Count);

      _distributionFilesSource.Edit(cache =>
      {
        cache.Clear();
        cache.AddOrUpdate(allFileViewModels);
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
                          .Where(k => !existingEditorIds.Contains(k.EditorID))
                          .ToList();

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
    var virtualKeywords   = new List<KeywordRecordViewModel>();

    var spidFiles = discoveredFiles.Where(f => f.Type == DistributionFileType.Spid);

    foreach (var file in spidFiles)
    {
      var sourceName = ExtractModFolderName(file.FullPath);
      ProcessKeywordLines(file.Lines, sourceName);
    }

    return virtualKeywords.OrderBy(k => k.DisplayName).ToList();

    void ProcessKeywordLines(IEnumerable<DistributionLine> lines, string? sourceName)
    {
      foreach (var line in lines)
      {
        if (!line.IsKeywordDistribution || string.IsNullOrWhiteSpace(line.KeywordIdentifier))
        {
          continue;
        }

        if (!existingEditorIds.Add(line.KeywordIdentifier))
        {
          continue;
        }

        var record = new KeywordRecord(FormKey.Null, line.KeywordIdentifier, ModKey.Null, sourceName);
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
}
