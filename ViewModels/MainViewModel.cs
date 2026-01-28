using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using DynamicData;
using DynamicData.Binding;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

public partial class MainViewModel : ReactiveObject, IDisposable
{
    private static readonly BipedObjectFlag[] _bipedObjectFlags = Enum.GetValues<BipedObjectFlag>()
        .Where(f => f != 0 && ((uint)f & ((uint)f - 1)) == 0) // Only single-bit flags (powers of 2)
        .ToArray();

    private readonly Subject<Unit> _autoSaveTrigger = new();
    private readonly IObservable<bool> _canCopyExistingOutfits;
    private readonly IObservable<bool> _canCreateOutfit;
    private readonly IObservable<bool> _canLoadOutfitPlugin;
    private readonly IObservable<bool> _canSaveOutfits;
    private readonly CompositeDisposable _disposables = new();
    private readonly ObservableCollection<ExistingOutfitViewModel> _existingOutfits = [];
    private readonly GameDataCacheService _gameDataCache;
    private readonly IObservable<bool> _isMapGlamOnly;
    private readonly IObservable<bool> _isMapSelected;
    private readonly ILogger _logger;

    private readonly IObservable<bool> _matchesCountGreaterThanZero;
    private readonly MutagenService _mutagenService;
    private readonly SourceList<ArmorRecordViewModel> _outfitArmorsSource = new();
    private readonly SourceList<OutfitDraftViewModel> _outfitDraftsSource = new();
    private readonly ReadOnlyObservableCollection<OutfitDraftViewModel> _outfitDrafts;
    private readonly PatchingService _patchingService;
    private readonly List<string> _pendingOutfitDeletions = [];
    private readonly ArmorPreviewService _previewService;
    private readonly SourceList<ArmorRecordViewModel> _sourceArmorsSource = new();
    private readonly SourceList<ArmorRecordViewModel> _targetArmorsSource = new();
    private int _activeLoadingOperations;

    private readonly SourceList<string> _availablePluginsSource = new();

    [Reactive] private int _outfitArmorsTotalCount;

    [Reactive] private int _sourceArmorsTotalCount;

    [Reactive] private int _targetArmorsTotalCount;

    [Reactive] private bool _hasExistingPluginOutfits;

    [Reactive] private bool _hasOutfitDrafts;

    [Reactive] private bool _hasPendingOutfitDeletions;

    [Reactive] private bool _isCreatingOutfits;

    [Reactive] private bool _isLoading;

    [Reactive] private bool _isPatching;

    private string? _lastLoadedOutfitPlugin;
    private string? _lastLoadedTargetPlugin;

    [Reactive] private int _mainTabIndex;

    [ReactiveCollection] private ObservableCollection<ArmorMatchViewModel> _matches = [];

    [Reactive] private string _outfitPluginSearchText = string.Empty;

    [Reactive] private string _outfitSearchText = string.Empty;

    [Reactive] private int _progressCurrent;

    [Reactive] private int _progressTotal;

    [Reactive] private int _selectedOutfitArmorCount;

    private IList _selectedOutfitArmors = new List<ArmorRecordViewModel>();
    private IList _selectedSourceArmors = new List<ArmorRecordViewModel>();

    [Reactive] private ArmorRecordViewModel? _selectedTargetArmor;

    [Reactive] private string _sourceSearchText = string.Empty;

    [Reactive] private string _statusMessage = "Ready";

    private bool _suppressAutoSave;

    [Reactive] private string _targetSearchText = string.Empty;

    [Reactive] private string _targetSortProperty = nameof(ArmorRecordViewModel.DisplayName);

    [Reactive] private bool _targetSortAscending = true;

    public MainViewModel(
        MutagenService mutagenService,
        PatchingService patchingService,
        ArmorPreviewService previewService,
        SettingsViewModel settingsViewModel,
        DistributionViewModel distributionViewModel,
        GameDataCacheService gameDataCache,
        ILoggingService loggingService)
    {
        _mutagenService = mutagenService;
        _patchingService = patchingService;
        _previewService = previewService;
        _gameDataCache = gameDataCache;
        Settings = settingsViewModel;
        Distribution = distributionViewModel;
        _logger = loggingService.ForContext<MainViewModel>();

        Distribution.ShowPreview.RegisterHandler(async interaction =>
        {
            await ShowPreview.Handle(interaction.Input);
            interaction.SetOutput(Unit.Default);
        });

        Distribution.OutfitCopiedToCreator += OnOutfitCopiedToCreator;

        _mutagenService.PluginsChanged += OnPluginsChanged;

        _disposables.Add(_outfitDraftsSource.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var outfitDrafts)
            .Subscribe());
        _outfitDrafts = outfitDrafts;

        _disposables.Add(_outfitDraftsSource.Connect()
            .Select(_ => _outfitDraftsSource.Count > 0)
            .Subscribe(hasDrafts => HasOutfitDrafts = hasDrafts));

        ConfigureArmorFiltering();

        OutfitDrafts = _outfitDrafts;
        HasOutfitDrafts = _outfitDraftsSource.Count > 0;

        _disposables.Add(_autoSaveTrigger
            .Throttle(TimeSpan.FromMilliseconds(1500))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Where(_ => (HasOutfitDrafts || HasPendingOutfitDeletions) && !IsCreatingOutfits)
            .SelectMany(_ => Observable.FromAsync(SaveOutfitsAsync))
            .Subscribe());

        ExistingOutfits = new ReadOnlyObservableCollection<ExistingOutfitViewModel>(_existingOutfits);
        HasExistingPluginOutfits = _existingOutfits.Count > 0;
        _existingOutfits.CollectionChanged += (_, _) => HasExistingPluginOutfits = _existingOutfits.Count > 0;

        this.WhenAnyValue(x => x.IsPatching, x => x.IsCreatingOutfits)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsProgressActive)));

        ConfigureOutfitPluginsFiltering();

        _matchesCountGreaterThanZero = this.WhenAnyValue(x => x.Matches.Count, count => count > 0);

        _isMapSelected = this.WhenAnyValue(
            x => x.SelectedSourceArmors,
            x => x.SelectedTargetArmor,
            (sources, target) => sources.OfType<ArmorRecordViewModel>().Any() && target is not null);

        _isMapGlamOnly = this.WhenAnyValue(
            x => x.SelectedSourceArmors,
            sources => sources.OfType<ArmorRecordViewModel>().Any());

        _canCreateOutfit = this.WhenAnyValue(x => x.SelectedOutfitArmorCount, count => count > 0);
        _canSaveOutfits = this.WhenAnyValue(
            x => x.HasOutfitDrafts,
            x => x.HasPendingOutfitDeletions,
            x => x.IsCreatingOutfits,
            (hasDrafts, hasDeletions, isBusy) => (hasDrafts || hasDeletions) && !isBusy);

        _canLoadOutfitPlugin =
            this.WhenAnyValue(x => x.SelectedOutfitPlugin, plugin => !string.IsNullOrWhiteSpace(plugin));

        _canCopyExistingOutfits = this.WhenAnyValue(x => x.HasExistingPluginOutfits);

        Settings.WhenAnyValue(x => x.PatchFileName)
            .Skip(1)
            .Where(_ => _availablePluginsSource.Count > 0)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(RxApp.MainThreadScheduler)
            .SelectMany(_ => LoadOutfitsFromOutputPluginAsync().ToObservable())
            .Subscribe();
    }

    public Interaction<string, Unit> PatchCreatedNotification { get; } = new();
    public Interaction<string, bool> ConfirmOverwritePatch { get; } = new();
    public Interaction<(string Prompt, string DefaultValue), string?> RequestOutfitName { get; } = new();
    public Interaction<ArmorPreviewSceneCollection, Unit> ShowPreview { get; } = new();
    public Interaction<MissingMastersResult, bool> HandleMissingMasters { get; } = new();

    public SettingsViewModel Settings { get; }
    public DistributionViewModel Distribution { get; }

    public ReadOnlyObservableCollection<string> FilteredOutfitPlugins { get; private set; } = null!;

    public ReadOnlyObservableCollection<string> AvailablePlugins { get; private set; } = null!;

    public int AvailablePluginsTotalCount => _availablePluginsSource.Count;

    public ReadOnlyObservableCollection<ArmorRecordViewModel> FilteredSourceArmors { get; private set; } = null!;
    public ReadOnlyObservableCollection<ArmorRecordViewModel> FilteredTargetArmors { get; private set; } = null!;
    public ReadOnlyObservableCollection<ArmorRecordViewModel> FilteredOutfitArmors { get; private set; } = null!;

    public IList SelectedOutfitArmors
    {
        get => _selectedOutfitArmors;
        set
        {
            if (value.Equals(_selectedOutfitArmors))
            {
                return;
            }

            _selectedOutfitArmors = value;
            this.RaisePropertyChanged();
            SelectedOutfitArmorCount = _selectedOutfitArmors.OfType<ArmorRecordViewModel>().Count();
        }
    }

    public string? SelectedOutfitPlugin
    {
        get => field;
        set
        {
            if (string.Equals(value, field, StringComparison.Ordinal))
            {
                return;
            }

            _existingOutfits.Clear();

            this.RaiseAndSetIfChanged(ref field, value);
            _logger.Information("Selected outfit plugin set to {Plugin}", value ?? "<none>");

            _lastLoadedOutfitPlugin = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                _outfitArmorsSource.Clear();
                OutfitArmorsTotalCount = 0;
                SelectedOutfitArmors = Array.Empty<ArmorRecordViewModel>();
                OutfitSearchText = string.Empty;
                return;
            }

            _ = LoadOutfitPluginAsync();
        }
    }

    public ReadOnlyObservableCollection<OutfitDraftViewModel> OutfitDrafts { get; }

    public ReadOnlyObservableCollection<ExistingOutfitViewModel> ExistingOutfits { get; }

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

    public bool IsProgressActive => IsPatching || IsCreatingOutfits;

    private async Task<int> LoadExistingOutfitsAsync(string plugin)
    {
        _existingOutfits.Clear();

        if (_mutagenService.LinkCache is null)
        {
            _logger.Warning("Link cache unavailable; skipping outfit discovery for {Plugin}.", plugin);
            return 0;
        }

        var outfits = (await _mutagenService.LoadOutfitsFromPluginAsync(plugin)).ToList();

        if (!string.Equals(SelectedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var linkCache = _mutagenService.LinkCache;
        var pluginModKey = ModKey.FromFileName(plugin);
        var discoveredCount = 0;

        foreach (var outfit in outfits)
        {
            if (outfit.FormKey.ModKey != pluginModKey)
            {
                continue;
            }

            var itemLinks = outfit.Items ?? [];
            var armorPieces = new List<IArmorGetter>();

            foreach (var entry in itemLinks)
            {
                if (entry is null)
                {
                    continue;
                }

                var formKeyNullable = entry.FormKeyNullable;
                if (!formKeyNullable.HasValue || formKeyNullable.Value == FormKey.Null)
                {
                    continue;
                }

                var formKey = formKeyNullable.Value;

                if (!linkCache.TryResolve<IItemGetter>(formKey, out var item))
                {
                    _logger.Debug(
                        "Unable to resolve outfit item {FormKey} for outfit {EditorId} in {Plugin}.",
                        formKey,
                        outfit.EditorID ?? "(No EditorID)",
                        plugin);
                    continue;
                }

                if (item is not IArmorGetter armor)
                {
                    _logger.Debug(
                        "Skipping non-armor item {FormKey} ({Type}) in outfit {EditorId}.",
                        formKey,
                        item.GetType().Name,
                        outfit.EditorID ?? "(No EditorID)");
                    continue;
                }

                armorPieces.Add(armor);
            }

            var distinctPieces = armorPieces
                .GroupBy(p => p.FormKey)
                .Select(g => g.First())
                .ToList();

            if (distinctPieces.Count == 0)
            {
                continue;
            }

            var editorId = outfit.EditorID ?? SanitizeOutfitName(outfit.FormKey.ToString());
            var displayName = editorId;

            var existing = new ExistingOutfitViewModel(
                displayName,
                editorId,
                outfit.FormKey,
                distinctPieces);

            _existingOutfits.Add(existing);
            discoveredCount++;

            _logger.Information(
                "Discovered existing outfit {EditorId} in {Plugin} with {PieceCount} piece(s).",
                editorId,
                plugin,
                distinctPieces.Count);
        }

        return discoveredCount;
    }

    [ReactiveCommand(CanExecute = nameof(_canCopyExistingOutfits))]
    private void CopyExistingOutfits()
    {
        if (_existingOutfits.Count == 0)
        {
            StatusMessage = "No existing outfits to copy.";
            _logger.Debug("CopyExistingOutfits invoked with no discovered outfits.");
            return;
        }

        if (_mutagenService.LinkCache is null)
        {
            StatusMessage = "Initialize Skyrim data path before copying outfits.";
            _logger.Warning("CopyExistingOutfits attempted without an active link cache.");
            return;
        }

        var linkCache = _mutagenService.LinkCache;
        var copied = 0;

        foreach (var existing in _existingOutfits.ToList())
        {
            if (_outfitDrafts.Any(d =>
                    d.FormKey.HasValue &&
                    d.FormKey.Value == existing.FormKey))
            {
                _logger.Debug("Skipping existing outfit {EditorId} because it already exists.", existing.EditorId);
                continue;
            }

            var baseName = SanitizeOutfitName(existing.EditorId);
            var uniqueName = EnsureUniqueOutfitName(baseName, null);

            if (!string.Equals(uniqueName, baseName, StringComparison.Ordinal))
            {
                _logger.Debug(
                    "Adjusted outfit name from {Original} to {Adjusted} when copying existing outfit.",
                    baseName,
                    uniqueName);
            }

            var pieces = existing.Pieces
                .Select(armor => new ArmorRecordViewModel(armor, linkCache))
                .ToList();

            if (!ValidateOutfitPieces(pieces, out var validationMessage))
            {
                _logger.Warning(
                    "Skipping existing outfit {EditorId} due to slot conflict while copying: {Message}",
                    existing.EditorId,
                    validationMessage);
                continue;
            }

            var draft = new OutfitDraftViewModel(
                uniqueName,
                uniqueName,
                pieces,
                RemoveOutfitDraft,
                RemoveOutfitPiece,
                PreviewDraftAsync,
                DuplicateDraftAsync);

            draft.FormKey = existing.FormKey;
            draft.PropertyChanged += OutfitDraftOnPropertyChanged;
            _outfitDraftsSource.Add(draft);
            copied++;
        }

        _existingOutfits.Clear();

        if (copied > 0)
        {
            StatusMessage = $"Copied {copied} existing outfit(s).";
            _logger.Information(
                "Copied {CopiedCount} existing outfit(s) from plugin {Plugin}.",
                copied,
                SelectedOutfitPlugin ?? "<none>");
        }
        else
        {
            StatusMessage = "Existing outfits already exist or could not be copied.";
            _logger.Information("No existing outfits were copied; they may already exist or lacked valid pieces.");
        }
    }

    private async Task LoadOutfitsFromOutputPluginAsync()
    {
        var outputPlugin = Settings.PatchFileName;
        if (string.IsNullOrWhiteSpace(outputPlugin))
        {
            _logger.Debug("No output plugin configured, skipping auto-load of existing outfits.");
            return;
        }

        var targetModKey = ModKey.FromFileName(outputPlugin);

        var draftsFromOtherPlugins = _outfitDraftsSource.Items
            .Where(d => d.FormKey.HasValue && d.FormKey.Value.ModKey != targetModKey && !d.IsOverride)
            .ToList();

        if (draftsFromOtherPlugins.Count > 0)
        {
            _logger.Information(
                "Clearing {Count} draft(s) from previous output plugin(s).",
                draftsFromOtherPlugins.Count);
            foreach (var draft in draftsFromOtherPlugins)
            {
                _outfitDraftsSource.Remove(draft);
            }
        }

        var patchPath = Settings.FullOutputPath;
        if (string.IsNullOrEmpty(patchPath) || !File.Exists(patchPath))
        {
            _logger.Debug("Patch file does not exist at {Path}, skipping auto-load.", patchPath);
            return;
        }

        var missingMastersResult = await _patchingService.CheckMissingMastersAsync(patchPath);
        if (missingMastersResult.HasMissingMasters)
        {
            _logger.Warning(
                "Missing masters detected in patch {Plugin}: {Masters}",
                outputPlugin,
                string.Join(", ", missingMastersResult.MissingMasters.Select(m => m.MissingMaster.FileName)));

            var shouldClean = await HandleMissingMasters.Handle(missingMastersResult);
            if (shouldClean)
            {
                var (success, message) = await _patchingService.CleanPatchMissingMastersAsync(
                    patchPath, missingMastersResult.AllAffectedOutfits);

                if (success)
                {
                    StatusMessage = message;
                    _logger.Information("Patch cleaned: {Message}", message);
                    await _mutagenService.RefreshLinkCacheAsync(outputPlugin);
                }
                else
                {
                    StatusMessage = $"Failed to clean patch: {message}";
                    _logger.Error("Failed to clean patch: {Message}", message);
                    return;
                }
            }
            else
            {
                StatusMessage =
                    "Missing masters detected. Add the master plugin(s) back to your load order and restart Boutique.";
                _logger.Information("User chose to add masters back instead of cleaning patch.");
                return;
            }
        }

        if (_mutagenService.LinkCache is null)
        {
            _logger.Warning("Link cache unavailable; skipping auto-load of outfits from {Plugin}.", outputPlugin);
            return;
        }

        _logger.Information("Output plugin {Plugin} exists, loading existing outfits for editing...", outputPlugin);

        var outfits = (await _mutagenService.LoadOutfitsFromPluginAsync(outputPlugin)).ToList();
        var linkCache = _mutagenService.LinkCache;
        var loadedCount = 0;

        _suppressAutoSave = true;
        try
        {
        var existingDraftKeys = _outfitDraftsSource.Items
                .Where(d => d.FormKey.HasValue)
                .Select(d => d.FormKey!.Value)
                .ToHashSet();

            var drafts = outfits
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .Select(outfit =>
                {
                    if (existingDraftKeys.Contains(outfit.FormKey))
                    {
                        return null;
                    }

                    var pieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
                    if (pieces.Count == 0)
                    {
                        return null;
                    }

                    var editorId = outfit.EditorID ?? SanitizeOutfitName(outfit.FormKey.ToString());

                    if (!ValidateOutfitPieces(pieces, out var validationMessage))
                    {
                        _logger.Warning(
                            "Skipping outfit {EditorId} due to slot conflict: {Message}",
                            editorId,
                            validationMessage);
                        return null;
                    }

                    var isOverride = outfit.FormKey.ModKey != targetModKey;
                    var overrideSourceMod = isOverride
                        ? GetWinningModForOutfit(outfit.FormKey, targetModKey)
                        : null;

                    var draft = new OutfitDraftViewModel(
                        editorId,
                        editorId,
                        pieces,
                        RemoveOutfitDraft,
                        RemoveOutfitPiece,
                        PreviewDraftAsync,
                        DuplicateDraftAsync)
                    {
                        FormKey = outfit.FormKey,
                        IsOverride = isOverride,
                        OverrideSourceMod = overrideSourceMod
                    };

                    return draft;
                })
                .Where(d => d != null)
                .ToList();

            var draftViewModels = new List<OutfitDraftViewModel>();
            foreach (var draft in drafts)
            {
                if (draft == null)
                {
                    continue;
                }

                draft.PropertyChanged += OutfitDraftOnPropertyChanged;
                draftViewModels.Add(draft);
                loadedCount++;

                _logger.Debug(
                    "Loaded outfit {EditorId} from output plugin {Plugin}.",
                    draft.EditorId,
                    outputPlugin);
            }

            if (draftViewModels.Count > 0)
            {
                _outfitDraftsSource.AddRange(draftViewModels);
            }
        }
        finally
        {
            _suppressAutoSave = false;
        }

        if (loadedCount > 0)
        {
            StatusMessage = $"Loaded {loadedCount} existing outfit(s) from {outputPlugin} for editing.";
            _logger.Information(
                "Loaded {Count} existing outfit(s) from output plugin {Plugin} for editing.",
                loadedCount,
                outputPlugin);
        }
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

        var outfitArmorFilter = this.WhenAnyValue(vm => vm.OutfitSearchText)
            .Throttle(TimeSpan.FromMilliseconds(200))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Select(searchText => new Func<ArmorRecordViewModel, bool>(armor => armor.MatchesSearch(searchText)));

        _disposables.Add(_outfitArmorsSource.Connect()
            .Filter(outfitArmorFilter)
            .Sort(SortExpressionComparer<ArmorRecordViewModel>.Ascending(a => a.DisplayName))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var filteredOutfitArmors)
            .Subscribe());
        FilteredOutfitArmors = filteredOutfitArmors;
    }

    private static IComparer<ArmorRecordViewModel> BuildTargetSortComparer(string? propertyName, bool ascending)
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

    private void ConfigureOutfitPluginsFiltering()
    {
        _disposables.Add(_availablePluginsSource.Connect()
            .Sort(SortExpressionComparer<string>.Ascending(p => p))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var availablePlugins)
            .Subscribe());
        AvailablePlugins = availablePlugins;

        var pluginFilter = this.WhenAnyValue(vm => vm.OutfitPluginSearchText)
            .Throttle(TimeSpan.FromMilliseconds(200))
            .Select(searchText => new Func<string, bool>(plugin =>
                string.IsNullOrWhiteSpace(searchText) ||
                plugin.Contains(searchText, StringComparison.OrdinalIgnoreCase)));

        _disposables.Add(_availablePluginsSource.Connect()
            .Filter(pluginFilter)
            .Sort(SortExpressionComparer<string>.Ascending(p => p))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var filteredPlugins)
            .Subscribe());

        FilteredOutfitPlugins = filteredPlugins;
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

        await SyncOutfitPluginWithTargetAsync(plugin, forceOutfitReload);
    }

    [ReactiveCommand(CanExecute = nameof(_canLoadOutfitPlugin))]
    public async Task LoadOutfitPluginAsync(bool forceReload = true)
    {
        var plugin = SelectedOutfitPlugin;
        if (string.IsNullOrWhiteSpace(plugin))
        {
            return;
        }

        if (!forceReload && string.Equals(_lastLoadedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await LoadOutfitArmorsAsync(plugin);
    }

    private async Task SyncOutfitPluginWithTargetAsync(string plugin, bool forceOutfitReload)
    {
        if (!string.Equals(SelectedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
        {
            SelectedOutfitPlugin = plugin;
            return;
        }

        if (forceOutfitReload)
        {
            await LoadOutfitPluginAsync();
        }
    }

    [ReactiveCommand]
    private async Task InitializeAsync()
    {
        BeginLoading();
        StatusMessage = "Initializing Mutagen...";
        _logger.Information("Initializing Mutagen with data path {DataPath}", Settings.SkyrimDataPath);

        try
        {
            using (Services.StartupProfiler.Instance.BeginOperation("MainViewModel.Initialize"))
            {
                await _mutagenService.InitializeAsync(Settings.SkyrimDataPath);

                var plugins = await _mutagenService.GetAvailablePluginsAsync();
                _availablePluginsSource.Edit(list =>
                {
                    list.Clear();
                    list.AddRange(plugins);
                });
                this.RaisePropertyChanged(nameof(AvailablePluginsTotalCount));

                StatusMessage = $"Loaded {_availablePluginsSource.Count} plugins";
                _logger.Information(
                    "Loaded {PluginCount} plugins from {DataPath}",
                    _availablePluginsSource.Count,
                    Settings.SkyrimDataPath);

                using (Services.StartupProfiler.Instance.BeginOperation("LoadOutfitsFromOutputPlugin", "MainViewModel.Initialize"))
                {
                    await LoadOutfitsFromOutputPluginAsync();
                }
            }
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
            var previousOutfitPlugin = SelectedOutfitPlugin;

            var plugins = await _mutagenService.GetAvailablePluginsAsync();
            var previousCount = _availablePluginsSource.Count;
            _availablePluginsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(plugins);
            });
            this.RaisePropertyChanged(nameof(AvailablePluginsTotalCount));

            if (!string.IsNullOrEmpty(previousOutfitPlugin) &&
                _availablePluginsSource.Items.Contains(previousOutfitPlugin, StringComparer.OrdinalIgnoreCase))
            {
                SelectedOutfitPlugin = previousOutfitPlugin;
            }

            _logger.Information(
                "Available plugins refreshed: {PreviousCount} → {NewCount} plugins.",
                previousCount,
                _availablePluginsSource.Count);

            await LoadOutfitsFromOutputPluginAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh available plugins list.");
        }
    }

    private async Task LoadSourceArmorsAsync(string plugin)
    {
        BeginLoading();
        if (string.IsNullOrWhiteSpace(plugin))
        {
            EndLoading();
            return;
        }

        StatusMessage = $"Loading armors from {plugin}...";
        _logger.Information("Loading source armors from {Plugin}", plugin);

        try
        {
            var armors = await _mutagenService.LoadArmorsFromPluginAsync(plugin);

            if (!string.Equals(SelectedSourcePlugin, plugin, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _sourceArmorsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
            });
            SourceArmorsTotalCount = _sourceArmorsSource.Count;
            SourceSearchText = string.Empty;

            var firstSource = FilteredSourceArmors.FirstOrDefault();
            SelectedSourceArmors = firstSource is not null
                ? new List<ArmorRecordViewModel> { firstSource }
                : Array.Empty<ArmorRecordViewModel>();

            StatusMessage = $"Loaded {SourceArmorsTotalCount} armors from {plugin}";
            _logger.Information("Loaded {ArmorCount} source armors from {Plugin}", SourceArmorsTotalCount, plugin);
        }
        catch (Exception ex)
        {
            if (string.Equals(SelectedSourcePlugin, plugin, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Error loading source armors: {ex.Message}";
                _logger.Error(ex, "Error loading source armors from {Plugin}", plugin);
            }
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task LoadTargetArmorsAsync(string plugin)
    {
        BeginLoading();
        if (string.IsNullOrWhiteSpace(plugin))
        {
            EndLoading();
            return;
        }

        StatusMessage = $"Loading armors from {plugin}...";
        _logger.Information("Loading target armors from {Plugin}", plugin);

        try
        {
            var armors = await _mutagenService.LoadArmorsFromPluginAsync(plugin);

            if (!string.Equals(SelectedTargetPlugin, plugin, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _targetArmorsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
            });
            TargetArmorsTotalCount = _targetArmorsSource.Count;
            _lastLoadedTargetPlugin = plugin;
            TargetSearchText = string.Empty;
            UpdateTargetSlotCompatibility();

            var primary = SelectedSourceArmor;
            SelectedTargetArmor = primary is not null
                ? FilteredTargetArmors.FirstOrDefault(t => primary.SharesSlotWith(t))
                : FilteredTargetArmors.FirstOrDefault();

            StatusMessage = $"Loaded {TargetArmorsTotalCount} armors from {plugin}";
            _logger.Information("Loaded {ArmorCount} target armors from {Plugin}", TargetArmorsTotalCount, plugin);
        }
        catch (Exception ex)
        {
            if (string.Equals(SelectedTargetPlugin, plugin, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Error loading target armors: {ex.Message}";
                _logger.Error(ex, "Error loading target armors from {Plugin}", plugin);
            }
        }
        finally
        {
            EndLoading();
        }
    }

    private async Task LoadOutfitArmorsAsync(string plugin)
    {
        BeginLoading();

        if (string.IsNullOrWhiteSpace(plugin))
        {
            _outfitArmorsSource.Clear();
            OutfitArmorsTotalCount = 0;
            SelectedOutfitArmors = Array.Empty<ArmorRecordViewModel>();
            OutfitSearchText = string.Empty;
            EndLoading();
            return;
        }

        StatusMessage = $"Loading armors from {plugin}...";
        _logger.Information("Loading outfit armors from {Plugin}", plugin);

        try
        {
            var armors = await _mutagenService.LoadArmorsFromPluginAsync(plugin);

            if (!string.Equals(SelectedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _outfitArmorsSource.Edit(list =>
            {
                list.Clear();
                list.AddRange(armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
            });
            OutfitArmorsTotalCount = _outfitArmorsSource.Count;
            _lastLoadedOutfitPlugin = plugin;
            OutfitSearchText = string.Empty;

            SelectedOutfitArmors = FilteredOutfitArmors.Any()
                ? new List<ArmorRecordViewModel> { FilteredOutfitArmors[0] }
                : Array.Empty<ArmorRecordViewModel>();

            var existingOutfitCount = await LoadExistingOutfitsAsync(plugin);

            StatusMessage = existingOutfitCount > 0
                ? $"Loaded {OutfitArmorsTotalCount} armors from {plugin}. {existingOutfitCount} existing outfit(s) available to copy."
                : $"Loaded {OutfitArmorsTotalCount} armors from {plugin} for outfit creation.";
            _logger.Information(
                "Loaded {ArmorCount} outfit armors from {Plugin}. Existing outfits available to copy: {ExistingCount}.",
                OutfitArmorsTotalCount,
                plugin,
                existingOutfitCount);
        }
        catch (Exception ex)
        {
            if (string.Equals(SelectedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Error loading outfit armors: {ex.Message}";
                _logger.Error(ex, "Error loading outfit armors from {Plugin}", plugin);
            }
        }
        finally
        {
            EndLoading();
        }
    }

    private static bool ValidateOutfitPieces(IReadOnlyList<ArmorRecordViewModel> pieces, out string validationMessage)
    {
        var slotsInUse = new Dictionary<BipedObjectFlag, ArmorRecordViewModel>();

        foreach (var piece in pieces)
        {
            var mask = piece.SlotMask;
            if (mask == 0)
            {
                continue;
            }

            foreach (var flag in _bipedObjectFlags)
            {
                if (!mask.HasFlag(flag))
                {
                    continue;
                }

                if (slotsInUse.TryGetValue(flag, out var owner))
                {
                    validationMessage = $"Slot conflict on {flag}: {piece.DisplayName} overlaps {owner.DisplayName}.";
                    return false;
                }

                slotsInUse[flag] = piece;
            }
        }

        validationMessage = string.Empty;
        return true;
    }

    private string EnsureUniqueOutfitName(string baseName, OutfitDraftViewModel? exclude)
    {
        var sanitizedBase = string.IsNullOrEmpty(baseName) ? "Outfit" : baseName;
        var candidate = sanitizedBase;
        var suffixIndex = 0;

        while (_outfitDraftsSource.Items.Any(o =>
                   !ReferenceEquals(o, exclude) &&
                   string.Equals(o.EditorId, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            suffixIndex++;
            candidate = sanitizedBase + AlphabetSuffix(suffixIndex);
        }

        return candidate;
    }

    private static string AlphabetSuffix(int index)
    {
        if (index <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        while (index > 0)
        {
            index--;
            builder.Insert(0, (char)('A' + index % 26));
            index /= 26;
        }

        return builder.ToString();
    }

    private static string SanitizeOutfitName(string? value) =>
        InputPatterns.Identifier.SanitizeOrDefault(value, "Outfit");

    private static List<ArmorRecordViewModel> DistinctArmorPieces(IEnumerable<ArmorRecordViewModel> pieces)
    {
        return pieces
            .GroupBy(p => p.Armor.FormKey)
            .Select(g => g.First())
            .ToList();
    }

    private async void OnOutfitCopiedToCreator(object? sender, CopiedOutfit copiedOutfit)
    {
        if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            StatusMessage = "LinkCache not available. Please initialize Skyrim data path first.";
            return;
        }

        if (!linkCache.TryResolve<IOutfitGetter>(copiedOutfit.OutfitFormKey, out var outfit))
        {
            StatusMessage = $"Could not find outfit {copiedOutfit.OutfitEditorId} in load order.";
            _logger.Warning("Outfit {FormKey} not found in LinkCache", copiedOutfit.OutfitFormKey);
            return;
        }

        var armorPieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
        if (armorPieces.Count == 0)
        {
            StatusMessage = $"Outfit {copiedOutfit.OutfitEditorId} has no armor pieces.";
            return;
        }

        MainTabIndex = 1;

        if (copiedOutfit.IsOverride)
        {
            await CreateOverrideDraftAsync(outfit, armorPieces);
        }
        else
        {
            var defaultName = "btq_" + (copiedOutfit.OutfitEditorId ?? outfit.FormKey.ToString());
            await CreateOutfitFromPiecesAsync(armorPieces, defaultName);
        }
    }

    private Task CreateOverrideDraftAsync(IOutfitGetter outfit, IReadOnlyList<ArmorRecordViewModel> armorPieces)
    {
        var editorId = outfit.EditorID ?? outfit.FormKey.ToString();

        var existingDraft = _outfitDraftsSource.Items.FirstOrDefault(d =>
            d.FormKey == outfit.FormKey ||
            string.Equals(d.EditorId, editorId, StringComparison.OrdinalIgnoreCase));

        if (existingDraft != null)
        {
            StatusMessage = $"Override for {editorId} already exists.";
            return Task.CompletedTask;
        }

        var winningMod = GetWinningModForOutfit(outfit.FormKey, null);

        var draft = new OutfitDraftViewModel(
            editorId,
            editorId,
            armorPieces,
            RemoveOutfitDraft,
            RemoveOutfitPiece,
            PreviewDraftAsync,
            DuplicateDraftAsync) { FormKey = outfit.FormKey, IsOverride = true, OverrideSourceMod = winningMod };

        draft.PropertyChanged += OutfitDraftOnPropertyChanged;
        _outfitDraftsSource.Add(draft);

        StatusMessage = $"Added override for '{editorId}' with {armorPieces.Count} piece(s).";
        _logger.Information(
            "Added override for {EditorId} ({FormKey}) with {PieceCount} pieces.",
            editorId,
            outfit.FormKey,
            armorPieces.Count);

        return Task.CompletedTask;
    }

    private ModKey? GetWinningModForOutfit(FormKey formKey, ModKey? excludeMod)
    {
        if (_mutagenService.LinkCache is not { } linkCache)
        {
            return null;
        }

        try
        {
            var contexts = linkCache.ResolveAllContexts<IOutfit, IOutfitGetter>(formKey);
            foreach (var context in contexts)
            {
                if (excludeMod.HasValue && context.ModKey == excludeMod.Value)
                {
                    continue;
                }

                return context.ModKey;
            }
        }
        catch
        {
            // Fall back to FormKey origin if resolution fails
        }

        return formKey.ModKey;
    }

    [ReactiveCommand(CanExecute = nameof(_canCreateOutfit))]
    private async Task CreateOutfitAsync()
    {
        var selectedPieces = SelectedOutfitArmors
            .OfType<ArmorRecordViewModel>()
            .ToList();

        await CreateOutfitFromPiecesAsync(selectedPieces);
    }

    public async Task CreateOutfitFromPiecesAsync(
        IReadOnlyList<ArmorRecordViewModel> pieces,
        string? defaultName = null)
    {
        var (distinctPieces, isValid, validationMessage) = await Task.Run(() =>
        {
            var distinct = DistinctArmorPieces(pieces);
            var valid = ValidateOutfitPieces(distinct, out var message);
            return (distinct, valid, message);
        });

        if (distinctPieces.Count == 0)
        {
            StatusMessage = "Select at least one armor to create an outfit.";
            _logger.Debug("CreateOutfitFromPiecesAsync invoked without any valid pieces.");
            return;
        }

        if (!isValid)
        {
            StatusMessage = validationMessage;
            _logger.Warning("Outfit creation blocked due to slot conflict: {Message}", validationMessage);
            return;
        }

        string outfitName;
        if (!string.IsNullOrWhiteSpace(defaultName))
        {
            outfitName = defaultName;
        }
        else
        {
            const string namePrompt = "Enter the outfit name (also used as the EditorID):";
            outfitName = await RequestOutfitName.Handle((namePrompt, "")).ToTask();

            if (string.IsNullOrWhiteSpace(outfitName))
            {
                StatusMessage = "Outfit creation canceled.";
                _logger.Information("Outfit creation canceled by user.");
                return;
            }
        }

        var trimmedName = outfitName.Trim();
        var sanitizedName = SanitizeOutfitName(trimmedName);
        sanitizedName = EnsureUniqueOutfitName(sanitizedName, null);

        if (!string.Equals(trimmedName, sanitizedName, StringComparison.Ordinal))
        {
            _logger.Debug("Outfit name sanitized from {Original} to {Sanitized}", trimmedName, sanitizedName);
        }

        var draft = new OutfitDraftViewModel(
            sanitizedName,
            sanitizedName,
            distinctPieces,
            RemoveOutfitDraft,
            RemoveOutfitPiece,
            PreviewDraftAsync,
            DuplicateDraftAsync);

        draft.PropertyChanged += OutfitDraftOnPropertyChanged;
        _outfitDraftsSource.Add(draft);

        StatusMessage = $"Added outfit '{draft.Name}' with {distinctPieces.Count} piece(s).";
        _logger.Information(
            "Added outfit {EditorId} with {PieceCount} pieces.",
            draft.EditorId,
            distinctPieces.Count);
    }

    public async Task DuplicateDraftAsync(OutfitDraftViewModel draft)
    {
        var pieces = draft.GetPieces();
        if (pieces.Count == 0)
        {
            StatusMessage = $"Outfit '{draft.EditorId}' has no pieces to duplicate.";
            return;
        }

        const string namePrompt = "Enter a name for the duplicated outfit:";
        var defaultName = draft.Name + "_copy";
        var newName = await RequestOutfitName.Handle((namePrompt, defaultName)).ToTask();

        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusMessage = "Duplicate canceled.";
            return;
        }

        var sanitizedName = SanitizeOutfitName(newName.Trim());
        sanitizedName = EnsureUniqueOutfitName(sanitizedName, null);

        var newDraft = new OutfitDraftViewModel(
            sanitizedName,
            sanitizedName,
            pieces,
            RemoveOutfitDraft,
            RemoveOutfitPiece,
            PreviewDraftAsync,
            DuplicateDraftAsync);

        newDraft.PropertyChanged += OutfitDraftOnPropertyChanged;
        _outfitDraftsSource.Add(newDraft);

        StatusMessage = $"Duplicated outfit as '{sanitizedName}' with {pieces.Count} piece(s).";
        _logger.Information(
            "Duplicated outfit draft {OriginalEditorId} to {NewEditorId}",
            draft.EditorId,
            sanitizedName);
    }

    public async Task PreviewDraftAsync(OutfitDraftViewModel draft)
    {
        var pieces = draft.GetPieces();
        if (pieces.Count == 0)
        {
            StatusMessage = $"Outfit '{draft.EditorId}' has no pieces to preview.";
            return;
        }

        try
        {
            StatusMessage = $"Building preview for '{draft.EditorId}'...";

            var metadata = new OutfitMetadata(draft.EditorId, null, false);
            var collection = new ArmorPreviewSceneCollection(
                1,
                0,
                new[] { metadata },
                async (_, gender) =>
                {
                    var scene = await _previewService.BuildPreviewAsync(pieces, gender);
                    return scene with { OutfitLabel = draft.EditorId };
                });

            await ShowPreview.Handle(collection);
            StatusMessage = $"Preview ready for '{draft.EditorId}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview error: {ex.Message}";
            _logger.Error(ex, "Failed to build outfit preview for {EditorId}.", draft.EditorId);
        }
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

    public bool TryAddPiecesToDraft(OutfitDraftViewModel draft, IReadOnlyList<ArmorRecordViewModel> pieces)
    {
        var distinctPieces = DistinctArmorPieces(pieces);

        if (distinctPieces.Count == 0)
        {
            StatusMessage = $"No new armor pieces to add to outfit '{draft.EditorId}'.";
            _logger.Debug("TryAddPiecesToDraft invoked with no valid pieces for outfit {EditorId}.", draft.EditorId);
            return false;
        }

        var existingPieces = draft.GetPieces();
        var stagedPieces = new List<ArmorRecordViewModel>();

        foreach (var piece in distinctPieces)
        {
            var existingConflict = existingPieces.FirstOrDefault(ep => piece.ConflictsWithSlot(ep));
            if (existingConflict is not null)
            {
                var overlap = piece.SlotMask & existingConflict.SlotMask;
                var slot = overlap != 0 ? ArmorRecordViewModel.FormatSlotMask(overlap) : piece.SlotSummary;
                StatusMessage = $"Slot conflict: {piece.DisplayName} overlaps {existingConflict.DisplayName} ({slot}).";
                _logger.Warning(
                    "Prevented adding {Piece} to outfit {EditorId} due to conflict with {Existing} on slot {Slot}.",
                    piece.DisplayName,
                    draft.EditorId,
                    existingConflict.DisplayName,
                    slot);
                return false;
            }

            var stagedConflict = stagedPieces.FirstOrDefault(sp => piece.ConflictsWithSlot(sp));
            if (stagedConflict is not null)
            {
                var overlap = piece.SlotMask & stagedConflict.SlotMask;
                var slot = overlap != 0 ? ArmorRecordViewModel.FormatSlotMask(overlap) : piece.SlotSummary;
                StatusMessage = $"Slot conflict: {piece.DisplayName} overlaps {stagedConflict.DisplayName} ({slot}).";
                _logger.Warning(
                    "Prevented adding {Piece} to outfit {EditorId} due to conflict with staged piece {Staged} on slot {Slot}.",
                    piece.DisplayName,
                    draft.EditorId,
                    stagedConflict.DisplayName,
                    slot);
                return false;
            }

            stagedPieces.Add(piece);
        }

        var (added, _) = draft.AddPieces(distinctPieces);

        if (added.Count == 0)
        {
            StatusMessage = $"No new armor added to outfit '{draft.EditorId}'.";
            _logger.Information("Drop onto outfit {EditorId} contained only duplicate pieces.", draft.EditorId);
            return false;
        }

        StatusMessage = $"Added {added.Count} piece(s) to outfit '{draft.EditorId}'.";

        _logger.Information(
            "Added {AddedCount} armor(s) to outfit draft {EditorId}. Added: {AddedPieces}.",
            added.Count,
            draft.EditorId,
            string.Join(", ", added.Select(a => a.DisplayName)));

        return true;
    }

    private void OnOutfitDraftsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasOutfitDrafts = _outfitDraftsSource.Count > 0;
        TriggerAutoSave();
    }

    private void OutfitDraftOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OutfitDraftViewModel draft)
        {
            return;
        }

        if (e.PropertyName == nameof(OutfitDraftViewModel.Name))
        {
            HandleOutfitDraftRename(draft);
        }

        // Trigger auto-save on any property change
        TriggerAutoSave();
    }

    private void TriggerAutoSave()
    {
        if (!_suppressAutoSave)
        {
            _autoSaveTrigger.OnNext(Unit.Default);
        }
    }

    private void HandleOutfitDraftRename(OutfitDraftViewModel draft)
    {
        var sanitized = SanitizeOutfitName(draft.Name);
        if (!string.Equals(draft.Name, sanitized, StringComparison.Ordinal))
        {
            draft.Name = sanitized;
            return;
        }

        var uniqueName = EnsureUniqueOutfitName(draft.EditorId, draft);
        if (!string.Equals(uniqueName, draft.EditorId, StringComparison.Ordinal))
        {
            var original = draft.EditorId;
            draft.Name = uniqueName;
            _logger.Information(
                "Adjusted outfit draft name from {Original} to {Adjusted} to ensure uniqueness.",
                original,
                uniqueName);
            return;
        }

        StatusMessage = $"Renamed outfit to '{draft.Name}'.";
        _logger.Information("Renamed outfit draft to {Name}", draft.Name);
    }

    private void RemoveOutfitDraft(OutfitDraftViewModel draft)
    {
        draft.PropertyChanged -= OutfitDraftOnPropertyChanged;

        if (!_outfitDraftsSource.Remove(draft))
        {
            return;
        }

        if (draft.FormKey.HasValue)
        {
            _pendingOutfitDeletions.Add(draft.EditorId);
            HasPendingOutfitDeletions = true;
            StatusMessage = $"Removed outfit '{draft.EditorId}'. Will be deleted from patch on save.";
            _logger.Information("Marked outfit {EditorId} for deletion.", draft.EditorId);
        }
        else
        {
            StatusMessage = $"Removed outfit '{draft.EditorId}'.";
            _logger.Information("Removed outfit draft {EditorId}.", draft.EditorId);
        }
    }

    private void RemoveOutfitPiece(OutfitDraftViewModel draft, ArmorRecordViewModel piece)
    {
        draft.RemovePiece(piece);
        StatusMessage = $"Removed {piece.DisplayName} from outfit '{draft.EditorId}'.";
        _logger.Information("Removed armor {Armor} from outfit draft {EditorId}.", piece.DisplayName, draft.EditorId);
    }

    [ReactiveCommand(CanExecute = nameof(_canSaveOutfits))]
    private async Task SaveOutfitsAsync()
    {
        var populatedDrafts = _outfitDraftsSource.Items.Where(d => d.HasPieces).ToList();
        var deletionsToProcess = _pendingOutfitDeletions.ToList();

        if (populatedDrafts.Count == 0 && deletionsToProcess.Count == 0)
        {
            StatusMessage = "No outfits to save or delete.";
            _logger.Debug("SaveOutfitsAsync invoked with no drafts or deletions.");
            return;
        }

        IsCreatingOutfits = true;

        try
        {
            ProgressCurrent = 0;
            ProgressTotal = populatedDrafts.Count + deletionsToProcess.Count;

            var requests = populatedDrafts
                .ConvertAll(d => new OutfitCreationRequest(
                    d.Name,
                    d.EditorId,
                    [.. d.GetPieces().Select(p => p.Armor)],
                    d.FormKey,
                    d.IsOverride,
                    d.OverrideSourceMod));

            requests.AddRange(deletionsToProcess.Select(editorId => new OutfitCreationRequest(editorId, editorId, [])));

            var progress = new Progress<(int Current, int Total, string Message)>(p =>
            {
                ProgressCurrent = p.Current;
                ProgressTotal = p.Total;
                StatusMessage = p.Message;
            });

            var outputPath = Settings.FullOutputPath;
            _logger.Information("Saving {Count} outfit(s) to patch {OutputPath}.", requests.Count, outputPath);

            var (success, message, results) = await _patchingService.CreateOrUpdateOutfitsAsync(
                requests,
                outputPath,
                progress);

            StatusMessage = message;

            if (success)
            {
                foreach (var editorId in deletionsToProcess)
                {
                    _pendingOutfitDeletions.Remove(editorId);
                }

                HasPendingOutfitDeletions = _pendingOutfitDeletions.Count > 0;

                _suppressAutoSave = true;
                try
                {
                    foreach (var result in results)
                    {
                        var draft = _outfitDraftsSource.Items.FirstOrDefault(d =>
                            string.Equals(d.EditorId, result.EditorId, StringComparison.OrdinalIgnoreCase));

                        if (draft != null && !draft.FormKey.HasValue)
                        {
                            draft.FormKey = result.FormKey;
                        }
                    }

                    StatusMessage = "Refreshing outfits...";
                    await _gameDataCache.RefreshOutfitsFromPatchAsync();
                    StatusMessage = message;
                }
                finally
                {
                    _suppressAutoSave = false;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error creating outfits: {ex.Message}";
            _logger.Error(ex, "Unexpected error while creating outfits.");
        }
        finally
        {
            IsCreatingOutfits = false;
            ProgressCurrent = 0;
            ProgressTotal = 0;
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
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error creating patch: {ex.Message}";
            _logger.Error(ex, "Unexpected error while creating patch.");
        }
        finally
        {
            IsPatching = false;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _sourceArmorsSource.Dispose();
        _targetArmorsSource.Dispose();
        _outfitArmorsSource.Dispose();
        _availablePluginsSource.Dispose();
        GC.SuppressFinalize(this);
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
