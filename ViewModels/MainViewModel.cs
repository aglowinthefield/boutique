using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Boutique.Models;
using Boutique.Services;
using DynamicData;
using DynamicData.Binding;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

public partial class MainViewModel : ReactiveObject, IDisposable
{
  public const string AllPluginsOption = "(All Plugins)";

  private readonly SourceList<string> _availablePluginsSource = new();
  private readonly CompositeDisposable _disposables = new();
  private readonly IObservable<bool> _isMapGlamOnly;
  private readonly IObservable<bool> _isMapSelected;
  private readonly ILogger _logger;

  private readonly IObservable<bool> _matchesCountGreaterThanZero;
  private readonly MutagenService _mutagenService;
  private readonly PatchingService _patchingService;
  private readonly ArmorPreviewService _previewService;
  private readonly SourceList<ArmorRecordViewModel> _sourceArmorsSource = new();
  private readonly SourceList<ArmorRecordViewModel> _targetArmorsSource = new();
  private int _activeLoadingOperations;

  [Reactive] private bool _isLoading;

  [Reactive] private bool _isPatching;

  private string? _lastLoadedTargetPlugin;

  [Reactive] private int _mainTabIndex;

  [ReactiveCollection] private ObservableCollection<ArmorMatchViewModel> _matches = [];

  [Reactive] private int _progressCurrent;

  [Reactive] private int _progressTotal;

  private IList _selectedSourceArmors = new List<ArmorRecordViewModel>();

  [Reactive] private ArmorRecordViewModel? _selectedTargetArmor;

  [Reactive] private int _sourceArmorsTotalCount;

  [Reactive] private string _sourceSearchText = string.Empty;

  [Reactive] private string _statusMessage = "Ready";

  [Reactive] private int _targetArmorsTotalCount;

  [Reactive] private string _targetSearchText = string.Empty;

  [Reactive] private bool _targetSortAscending = true;

  [Reactive] private string _targetSortProperty = nameof(ArmorRecordViewModel.DisplayName);

  public MainViewModel(
    MutagenService mutagenService,
    PatchingService patchingService,
    ArmorPreviewService previewService,
    SettingsViewModel settingsViewModel,
    DistributionViewModel distributionViewModel,
    OutfitCreatorViewModel outfitCreatorViewModel,
    ILoggingService loggingService)
  {
    _mutagenService = mutagenService;
    _patchingService = patchingService;
    _previewService = previewService;
    Settings = settingsViewModel;
    Distribution = distributionViewModel;
    OutfitCreator = outfitCreatorViewModel;
    _logger = loggingService.ForContext<MainViewModel>();

    // Forward preview interactions from Distribution and OutfitCreator
    Distribution.ShowPreview.RegisterHandler(async interaction =>
    {
      await ShowPreview.Handle(interaction.Input);
      interaction.SetOutput(Unit.Default);
    });

    OutfitCreator.ShowPreview.RegisterHandler(async interaction =>
    {
      await ShowPreview.Handle(interaction.Input);
      interaction.SetOutput(Unit.Default);
    });

    // Forward outfit copy requests from Distribution to OutfitCreator
    Distribution.OutfitCopiedToCreator += async (_, copiedOutfit) =>
      await OutfitCreator.OnOutfitCopiedToCreator(copiedOutfit);

    // Forward interactions from OutfitCreator to main window
    OutfitCreator.ConfirmDelete.RegisterHandler(async interaction =>
    {
      var result = await ConfirmDelete.Handle(interaction.Input);
      interaction.SetOutput(result);
    });

    OutfitCreator.ShowError.RegisterHandler(async interaction =>
    {
      await ShowError.Handle(interaction.Input);
      interaction.SetOutput(Unit.Default);
    });

    OutfitCreator.HandleMissingMasters.RegisterHandler(async interaction =>
    {
      var result = await HandleMissingMasters.Handle(interaction.Input);
      interaction.SetOutput(result);
    });

    // Forward status messages from OutfitCreator to main status bar
    OutfitCreator.WhenAnyValue(x => x.StatusMessage)
      .Where(msg => !string.IsNullOrEmpty(msg))
      .Subscribe(msg => StatusMessage = msg);

    _mutagenService.PluginsChanged += OnPluginsChanged;

    ConfigureArmorFiltering();
    ConfigureAvailablePlugins();

    this.WhenAnyValue(x => x.IsPatching)
      .Subscribe(_ => this.RaisePropertyChanged(nameof(IsProgressActive)));

    _matchesCountGreaterThanZero = this.WhenAnyValue(x => x.Matches.Count, count => count > 0);

    _isMapSelected = this.WhenAnyValue(
      x => x.SelectedSourceArmors,
      x => x.SelectedTargetArmor,
      (sources, target) => sources.OfType<ArmorRecordViewModel>().Any() && target is not null);

    _isMapGlamOnly = this.WhenAnyValue(
      x => x.SelectedSourceArmors,
      sources => sources.OfType<ArmorRecordViewModel>().Any());
  }

  public Interaction<string, Unit> PatchCreatedNotification { get; } = new();
  public Interaction<string, bool> ConfirmOverwritePatch { get; } = new();
  public Interaction<string, bool> ConfirmDelete { get; } = new();
  public Interaction<ArmorPreviewSceneCollection, Unit> ShowPreview { get; } = new();
  public Interaction<MissingMastersResult, bool> HandleMissingMasters { get; } = new();
  public Interaction<(string Title, string Message), Unit> ShowError { get; } = new();

  public SettingsViewModel Settings { get; }
  public DistributionViewModel Distribution { get; }
  public OutfitCreatorViewModel OutfitCreator { get; }

  public ReadOnlyObservableCollection<string> AvailablePlugins { get; private set; } = null!;

  public int AvailablePluginsTotalCount => _availablePluginsSource.Count;

  public ReadOnlyObservableCollection<ArmorRecordViewModel> FilteredSourceArmors { get; private set; } = null!;
  public ReadOnlyObservableCollection<ArmorRecordViewModel> FilteredTargetArmors { get; private set; } = null!;

  public IList SelectedSourceArmors
  {
    get => _selectedSourceArmors;
    set
    {
      if (value.Equals(_selectedSourceArmors))
      {
        return;
      }

      _selectedSourceArmors = value;
      this.RaisePropertyChanged();

      var primary = SelectedSourceArmor;
      UpdateTargetSlotCompatibility();

      if (_targetArmorsSource.Count == 0 || primary is null)
      {
        SelectedTargetArmor = null;
        return;
      }

      var existing = Matches.FirstOrDefault(m => m.Source.Armor.FormKey == primary.Armor.FormKey);
      if (existing?.Target is not null)
      {
        SelectedTargetArmor =
          _targetArmorsSource.Items.FirstOrDefault(t => t.Armor.FormKey == existing.Target.Armor.FormKey);
      }
      else
      {
        SelectedTargetArmor = _targetArmorsSource.Items.FirstOrDefault(t => primary.SharesSlotWith(t));
      }
    }
  }

  private ArmorRecordViewModel? SelectedSourceArmor =>
    _selectedSourceArmors.OfType<ArmorRecordViewModel>().FirstOrDefault();

  public string? SelectedSourcePlugin
  {
    get => field;
    set
    {
      if (string.Equals(value, field, StringComparison.Ordinal))
      {
        return;
      }

      this.RaiseAndSetIfChanged(ref field, value);
      _logger.Information("Selected source plugin set to {Plugin}", value ?? "<none>");

      ClearMappingsInternal();
      _sourceArmorsSource.Clear();
      SourceArmorsTotalCount = 0;
      SelectedSourceArmors = Array.Empty<ArmorRecordViewModel>();
      SourceSearchText = string.Empty;

      if (string.IsNullOrWhiteSpace(value))
      {
        return;
      }

      _ = LoadSourceArmorsAsync(value);
    }
  }

  public string? SelectedTargetPlugin
  {
    get => field;
    set
    {
      if (string.Equals(value, field, StringComparison.Ordinal))
      {
        return;
      }

      this.RaiseAndSetIfChanged(ref field, value);
      _logger.Information("Selected target plugin set to {Plugin}", value ?? "<none>");

      _lastLoadedTargetPlugin = null;
      TargetSearchText = string.Empty;

      if (string.IsNullOrWhiteSpace(value))
      {
        ClearMappingsInternal();
        _targetArmorsSource.Clear();
        TargetArmorsTotalCount = 0;
        SelectedTargetArmor = null;
        return;
      }

      _ = LoadTargetPluginAsync(true);
    }
  }

  public bool IsProgressActive => IsPatching || OutfitCreator.IsCreatingOutfits;

  public void Dispose()
  {
    _disposables.Dispose();
    _sourceArmorsSource.Dispose();
    _targetArmorsSource.Dispose();
    _availablePluginsSource.Dispose();
    GC.SuppressFinalize(this);
  }

  private void ConfigureArmorFiltering()
  {
    var sourceArmorFilter = this.WhenAnyValue(vm => vm.SourceSearchText)
      .Throttle(TimeSpan.FromMilliseconds(200))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Select(searchText => new Func<ArmorRecordViewModel, bool>(armor => armor.MatchesSearch(searchText)));

    _disposables.Add(_sourceArmorsSource.Connect()
      .Filter(sourceArmorFilter)
      .Sort(SortExpressionComparer<ArmorRecordViewModel>.Ascending(a => a.DisplayName))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Bind(out var filteredSourceArmors)
      .Subscribe());
    FilteredSourceArmors = filteredSourceArmors;

    var targetArmorFilter = this.WhenAnyValue(vm => vm.TargetSearchText)
      .Throttle(TimeSpan.FromMilliseconds(200))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Select(searchText => new Func<ArmorRecordViewModel, bool>(armor =>
        armor.MatchesSearch(searchText) && armor.IsSlotCompatible));

    var targetSortComparer = this.WhenAnyValue(
        vm => vm.TargetSortProperty,
        vm => vm.TargetSortAscending)
      .Select(tuple => BuildTargetSortComparer(tuple.Item1, tuple.Item2));

    _disposables.Add(_targetArmorsSource.Connect()
      .AutoRefresh(a => a.IsSlotCompatible)
      .Filter(targetArmorFilter)
      .Sort(targetSortComparer)
      .ObserveOn(RxApp.MainThreadScheduler)
      .Bind(out var filteredTargetArmors)
      .Subscribe());
    FilteredTargetArmors = filteredTargetArmors;
  }

  private static SortExpressionComparer<ArmorRecordViewModel> BuildTargetSortComparer(string? propertyName, bool ascending)
  {
    var comparer = SortExpressionComparer<ArmorRecordViewModel>.Ascending(a => a.SlotCompatibilityPriority);

    return propertyName switch
    {
      nameof(ArmorRecordViewModel.DisplayName) => ascending
        ? comparer.ThenByAscending(a => a.DisplayName)
        : comparer.ThenByDescending(a => a.DisplayName),
      nameof(ArmorRecordViewModel.SlotSummary) => ascending
        ? comparer.ThenByAscending(a => a.SlotSummary)
        : comparer.ThenByDescending(a => a.SlotSummary),
      nameof(ArmorRecordViewModel.FormIdSortable) => ascending
        ? comparer.ThenByAscending(a => a.FormIdSortable)
        : comparer.ThenByDescending(a => a.FormIdSortable),
      nameof(ArmorRecordViewModel.ModDisplayName) => ascending
        ? comparer.ThenByAscending(a => a.ModDisplayName)
        : comparer.ThenByDescending(a => a.ModDisplayName),
      _ => comparer.ThenByAscending(a => a.DisplayName)
    };
  }

  private void ConfigureAvailablePlugins()
  {
    _disposables.Add(_availablePluginsSource.Connect()
      .Sort(SortExpressionComparer<string>.Ascending(p => p))
      .ObserveOn(RxApp.MainThreadScheduler)
      .Bind(out var availablePlugins)
      .Subscribe());
    AvailablePlugins = availablePlugins;
  }

  private void UpdateTargetSlotCompatibility()
  {
    var sources = SelectedSourceArmors.OfType<ArmorRecordViewModel>().ToList();

    if (sources.Count == 0)
    {
      foreach (var target in _targetArmorsSource.Items)
      {
        target.IsSlotCompatible = true;
      }

      return;
    }

    foreach (var target in _targetArmorsSource.Items)
    {
      target.IsSlotCompatible = sources.All(source => source.SharesSlotWith(target));
    }
  }

  [ReactiveCommand(CanExecute = nameof(_isMapSelected))]
  private void MapSelected()
  {
    var sources = SelectedSourceArmors.OfType<ArmorRecordViewModel>().ToList();
    var target = SelectedTargetArmor;

    if (sources.Count == 0 || target is null)
    {
      _logger.Debug(
        "MapSelected invoked without valid selections. SourceCount={SourceCount}, HasTarget={HasTarget}",
        sources.Count,
        target is not null);
      return;
    }

    try
    {
      foreach (var source in sources)
      {
        var existing = Matches.FirstOrDefault(m => m.Source.Armor.FormKey == source.Armor.FormKey);
        if (existing is not null)
        {
          existing.ApplyManualTarget(target);
        }
        else
        {
          var match = new ArmorMatch(source.Armor, target.Armor, true);
          var mapping = new ArmorMatchViewModel(match, source, target);
          Matches.Add(mapping);
        }

        source.IsMapped = true;
      }

      StatusMessage = $"Mapped {sources.Count} armors to {target.DisplayName}";
      _logger.Information(
        "Mapped {SourceCount} armor(s) to target {TargetName} ({TargetFormKey})",
        sources.Count,
        target.DisplayName,
        target.Armor.FormKey);
    }
    catch (Exception ex)
    {
      _logger.Error(
        ex,
        "Failed to map {SourceCount} armor(s) to {TargetName}",
        sources.Count,
        target.DisplayName);
      StatusMessage = $"Error mapping armors: {ex.Message}";
    }
  }

  [ReactiveCommand(CanExecute = nameof(_isMapGlamOnly))]
  private void MapSelectedAsGlamOnly()
  {
    var sources = SelectedSourceArmors.OfType<ArmorRecordViewModel>().ToList();

    if (sources.Count == 0)
    {
      _logger.Debug("MapSelectedAsGlamOnly invoked without source selection.");
      return;
    }

    try
    {
      foreach (var source in sources)
      {
        var existing = Matches.FirstOrDefault(m => m.Source.Armor.FormKey == source.Armor.FormKey);
        if (existing is not null)
        {
          existing.ApplyGlamOnly();
        }
        else
        {
          var match = new ArmorMatch(source.Armor, null, true);
          var mapping = new ArmorMatchViewModel(match, source, null);
          Matches.Add(mapping);
        }

        source.IsMapped = true;
      }

      StatusMessage = $"Marked {sources.Count} armor(s) as glam-only.";
      _logger.Information("Marked {SourceCount} armor(s) as glam-only.", sources.Count);
    }
    catch (Exception ex)
    {
      StatusMessage = $"Error marking glam-only: {ex.Message}";
      _logger.Error(ex, "Failed to mark {SourceCount} armor(s) as glam-only.", sources.Count);
    }
  }

  [ReactiveCommand(CanExecute = nameof(_matchesCountGreaterThanZero))]
  private void ClearMappings()
  {
    if (!ClearMappingsInternal())
    {
      return;
    }

    StatusMessage = "Cleared all mappings.";
    _logger.Information("Cleared all manual mappings.");
  }

  private void BeginLoading()
  {
    _activeLoadingOperations++;
    IsLoading = true;
  }

  private void EndLoading()
  {
    if (_activeLoadingOperations > 0)
    {
      _activeLoadingOperations--;
    }

    IsLoading = _activeLoadingOperations > 0;
  }

  private async Task<int> LoadArmorsIntoSourceListAsync(
    string plugin,
    Func<string?> getSelectedPlugin,
    SourceList<ArmorRecordViewModel> sourceList)
  {
    BeginLoading();

    try
    {
      if (string.IsNullOrWhiteSpace(plugin))
      {
        sourceList.Clear();
        return 0;
      }

      var armors = await _mutagenService.LoadArmorsFromPluginAsync(plugin);

      if (!string.Equals(getSelectedPlugin(), plugin, StringComparison.OrdinalIgnoreCase))
      {
        return 0;
      }

      sourceList.Edit(list =>
      {
        list.Clear();
        list.AddRange(armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
      });

      return sourceList.Count;
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Error loading armors from {Plugin}", plugin);
      return 0;
    }
    finally
    {
      EndLoading();
    }
  }

  private bool ClearMappingsInternal()
  {
    if (Matches.Count == 0)
    {
      return false;
    }

    foreach (var mapping in Matches.ToList())
    {
      mapping.Source.IsMapped = false;
    }

    Matches.Clear();
    return true;
  }

  [ReactiveCommand]
  private void RemoveMapping(ArmorMatchViewModel mapping)
  {
    if (!Matches.Contains(mapping))
    {
      return;
    }

    Matches.Remove(mapping);
    mapping.Source.IsMapped = Matches.Any(m => m.Source.Armor.FormKey == mapping.Source.Armor.FormKey);
    StatusMessage = $"Removed mapping for {mapping.Source.DisplayName}";
    _logger.Information(
      "Removed mapping for source {SourceName} ({SourceFormKey})",
      mapping.Source.DisplayName,
      mapping.Source.Armor.FormKey);
  }

  public async Task LoadTargetPluginAsync(bool forceOutfitReload = false)
  {
    var plugin = SelectedTargetPlugin;
    if (string.IsNullOrWhiteSpace(plugin))
    {
      return;
    }

    var needsReload = forceOutfitReload ||
                      !string.Equals(_lastLoadedTargetPlugin, plugin, StringComparison.OrdinalIgnoreCase);

    if (needsReload)
    {
      ClearMappingsInternal();
      _targetArmorsSource.Clear();
      TargetArmorsTotalCount = 0;
      SelectedTargetArmor = null;
      await LoadTargetArmorsAsync(plugin);
    }

    // Sync outfit plugin with target
    await OutfitCreator.SyncOutfitPluginWithTargetAsync(plugin, forceOutfitReload);
  }

  [ReactiveCommand]
  private async Task InitializeAsync()
  {
    BeginLoading();
    StatusMessage = "Initializing Mutagen...";
    _logger.Information("Initializing Mutagen with data path {DataPath}", Settings.SkyrimDataPath);

    try
    {
      await _mutagenService.InitializeAsync(Settings.SkyrimDataPath);

      var plugins = await _mutagenService.GetPluginsWithArmorsOrOutfitsAsync();
      var pluginList = plugins.ToList();
      _availablePluginsSource.Edit(list =>
      {
        list.Clear();
        list.Add(AllPluginsOption);
        list.AddRange(pluginList);
      });
      this.RaisePropertyChanged(nameof(AvailablePluginsTotalCount));

      StatusMessage = $"Loaded {pluginList.Count} plugins with armors/outfits";
      _logger.Information(
        "Loaded {PluginCount} plugins with armors/outfits from {DataPath}",
        pluginList.Count,
        Settings.SkyrimDataPath);

      // LoadOutfitsFromOutputPluginAsync is now handled by OutfitCreatorViewModel
    }
    catch (Exception ex)
    {
      StatusMessage = $"Error: {ex.Message}";
      _logger.Error(ex, "Failed to initialize Mutagen services.");
    }
    finally
    {
      EndLoading();
    }
  }

  private async void OnPluginsChanged(object? sender, EventArgs e)
  {
    _logger.Debug("PluginsChanged event received, refreshing available plugins list...");

    try
    {
      var previousOutfitPlugin = OutfitCreator.SelectedOutfitPlugin;

      var plugins = await _mutagenService.GetPluginsWithArmorsOrOutfitsAsync();
      var pluginList = plugins.ToList();
      var previousCount = _availablePluginsSource.Count - 1;
      _availablePluginsSource.Edit(list =>
      {
        list.Clear();
        list.Add(AllPluginsOption);
        list.AddRange(pluginList);
      });
      this.RaisePropertyChanged(nameof(AvailablePluginsTotalCount));

      _logger.Information(
        "Available plugins refreshed: {PreviousCount} → {NewCount} plugins with armors/outfits.",
        previousCount,
        pluginList.Count);

      if (!string.IsNullOrEmpty(previousOutfitPlugin) &&
          _availablePluginsSource.Items.Contains(previousOutfitPlugin, StringComparer.OrdinalIgnoreCase))
      {
        await Application.Current.Dispatcher.InvokeAsync(
          () => OutfitCreator.RaisePropertyChanged(nameof(OutfitCreator.SelectedOutfitPlugin)),
          DispatcherPriority.Background);
      }

      // LoadOutfitsFromOutputPluginAsync is now handled by OutfitCreatorViewModel
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to refresh available plugins list.");
    }
  }

  private async Task LoadSourceArmorsAsync(string plugin)
  {
    SourceSearchText = string.Empty;
    StatusMessage = $"Loading armors from {plugin}...";
    _logger.Information("Loading source armors from {Plugin}", plugin);

    var count = await LoadArmorsIntoSourceListAsync(plugin, () => SelectedSourcePlugin, _sourceArmorsSource);

    if (!string.Equals(SelectedSourcePlugin, plugin, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    SourceArmorsTotalCount = count;

    var firstSource = FilteredSourceArmors.FirstOrDefault();
    SelectedSourceArmors = firstSource is not null
      ? new List<ArmorRecordViewModel> { firstSource }
      : Array.Empty<ArmorRecordViewModel>();

    StatusMessage = $"Loaded {count} armors from {plugin}";
    _logger.Information("Loaded {ArmorCount} source armors from {Plugin}", count, plugin);
  }

  private async Task LoadTargetArmorsAsync(string plugin)
  {
    TargetSearchText = string.Empty;
    StatusMessage = $"Loading armors from {plugin}...";
    _logger.Information("Loading target armors from {Plugin}", plugin);

    var count = await LoadArmorsIntoSourceListAsync(plugin, () => SelectedTargetPlugin, _targetArmorsSource);

    if (!string.Equals(SelectedTargetPlugin, plugin, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    TargetArmorsTotalCount = count;
    _lastLoadedTargetPlugin = plugin;
    UpdateTargetSlotCompatibility();

    var primary = SelectedSourceArmor;
    SelectedTargetArmor = primary is not null
      ? FilteredTargetArmors.FirstOrDefault(t => primary.SharesSlotWith(t))
      : FilteredTargetArmors.FirstOrDefault();

    StatusMessage = $"Loaded {count} armors from {plugin}";
    _logger.Information("Loaded {ArmorCount} target armors from {Plugin}", count, plugin);
  }

  public async Task PreviewArmorAsync(ArmorRecordViewModel armor)
  {
    try
    {
      StatusMessage = $"Building preview for '{armor.DisplayName}'...";

      var metadata = new OutfitMetadata(armor.DisplayName, armor.Armor.FormKey.ModKey.FileName.String, false);
      var collection = new ArmorPreviewSceneCollection(
        1,
        0,
        new[] { metadata },
        async (_, gender) =>
        {
          var scene = await _previewService.BuildPreviewAsync([armor], gender);
          return scene with
          {
            OutfitLabel = armor.DisplayName, SourceFile = armor.Armor.FormKey.ModKey.FileName.String
          };
        });

      await ShowPreview.Handle(collection);
      StatusMessage = $"Preview ready for '{armor.DisplayName}'.";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Preview error: {ex.Message}";
      _logger.Error(ex, "Failed to build armor preview for {Armor}.", armor.DisplayName);
    }
  }

  public void ApplyTargetSort(
    string? propertyName = nameof(ArmorRecordViewModel.DisplayName),
    ListSortDirection direction = ListSortDirection.Ascending)
  {
    TargetSortProperty = propertyName ?? nameof(ArmorRecordViewModel.DisplayName);
    TargetSortAscending = direction == ListSortDirection.Ascending;
  }

  [ReactiveCommand(CanExecute = nameof(_matchesCountGreaterThanZero))]
  private async Task CreatePatchAsync()
  {
    IsPatching = true;
    StatusMessage = "Creating patch...";

    try
    {
      var progress = new Progress<(int current, int total, string message)>(p =>
      {
        ProgressCurrent = p.current;
        ProgressTotal = p.total;
        StatusMessage = p.message;
      });

      var matchesToPatch = Matches
        .Where(m => m.Match.TargetArmor is not null || m.Match.IsGlamOnly)
        .Select(m => m.Match)
        .ToList();

      if (matchesToPatch.Count == 0)
      {
        StatusMessage = "No mapped armors to patch.";
        _logger.Warning("Patch creation aborted - no mapped armors available.");
        return;
      }

      var outputPath = Settings.FullOutputPath;
      if (File.Exists(outputPath))
      {
        const string confirmationMessage =
          "The selected patch file already exists. Adding new data will overwrite any records with matching FormIDs in that ESP.\n\nDo you want to continue?";
        var confirmed = await ConfirmOverwritePatch.Handle(confirmationMessage).ToTask();
        if (!confirmed)
        {
          StatusMessage = "Patch creation canceled.";
          _logger.Information(
            "Patch creation canceled by user to avoid overwriting existing patch at {OutputPath}",
            outputPath);
          return;
        }
      }

      _logger.Information(
        "Starting patch creation for {MatchCount} matches to {OutputPath}",
        matchesToPatch.Count,
        Settings.FullOutputPath);

      var (success, message) = await _patchingService.CreatePatchAsync(
        matchesToPatch,
        outputPath,
        progress);

      StatusMessage = message;

      if (success)
      {
        _logger.Information("Patch creation completed successfully: {Message}", message);
        await PatchCreatedNotification.Handle(message).ToTask();
      }
      else
      {
        _logger.Warning("Patch creation failed: {Message}", message);
        await ShowError.Handle(("Failed to Create Patch", message));
      }
    }
    catch (Exception ex)
    {
      var errorMessage = $"Error creating patch: {ex.Message}";
      StatusMessage = errorMessage;
      _logger.Error(ex, "Unexpected error while creating patch.");
      await ShowError.Handle(("Unexpected Error", errorMessage));
    }
    finally
    {
      IsPatching = false;
    }
  }
}

public partial class ArmorMatchViewModel : ReactiveObject
{
  [Reactive] private ArmorRecordViewModel? _target;

  public ArmorMatchViewModel(
    ArmorMatch match,
    ArmorRecordViewModel source,
    ArmorRecordViewModel? target)
  {
    Match = match;
    Source = source;

    if (target is not null)
    {
      ApplyAutoTarget(target);
    }
    else if (match.IsGlamOnly)
    {
      ApplyGlamOnly();
    }
    else
    {
      RefreshState();
    }
  }

  public ArmorMatch Match { get; }
  public ArmorRecordViewModel Source { get; }

  public bool HasTarget => Match.IsGlamOnly || Target is not null;
  public bool IsGlamOnly => Match.IsGlamOnly;
  public string SourceSummary => Source.SummaryLine;

  public string TargetSummary
  {
    get
    {
      if (Match.IsGlamOnly)
      {
        return "✨ Glam-only (armor rating set to 0)";
      }

      if (Target is not null)
      {
        return Target.SummaryLine;
      }

      return "Not mapped";
    }
  }

  public string CombinedSummary => $"{SourceSummary} <> {TargetSummary}";

  public void ApplyManualTarget(ArmorRecordViewModel target)
  {
    Match.IsGlamOnly = false;
    ApplyTargetInternal(target);
  }

  public void ApplyAutoTarget(ArmorRecordViewModel target)
  {
    Match.IsGlamOnly = false;
    ApplyTargetInternal(target);
  }

  public void ClearTarget()
  {
    Match.TargetArmor = null;
    Match.IsGlamOnly = false;
    Target = null;
  }

  public void ApplyGlamOnly()
  {
    Match.IsGlamOnly = true;
    Match.TargetArmor = null;
    Target = null;
    RefreshState();
  }

  private void ApplyTargetInternal(ArmorRecordViewModel target)
  {
    Match.IsGlamOnly = false;
    Match.TargetArmor = target.Armor;
    Target = target;
    RefreshState();
  }

  private void RefreshState()
  {
    this.RaisePropertyChanged(nameof(TargetSummary));
    this.RaisePropertyChanged(nameof(CombinedSummary));
    this.RaisePropertyChanged(nameof(HasTarget));
    this.RaisePropertyChanged(nameof(IsGlamOnly));
  }
}
