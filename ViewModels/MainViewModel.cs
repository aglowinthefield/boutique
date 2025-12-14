using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;
using Boutique.Models;
using Boutique.Services;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Boutique.ViewModels;

public class MainViewModel : ReactiveObject
{
    private static readonly Regex OutfitNameSanitizer = new("[^A-Za-z]", RegexOptions.Compiled);
    private static readonly BipedObjectFlag[] BipedObjectFlags = Enum.GetValues<BipedObjectFlag>()
        .Where(f => f != 0 && ((uint)f & ((uint)f - 1)) == 0) // Only single-bit flags (powers of 2)
        .ToArray();
    private readonly ObservableCollection<ExistingOutfitViewModel> _existingOutfits = [];
    private readonly ILogger _logger;
    private readonly MutagenService _mutagenService;
    private readonly ObservableCollection<OutfitDraftViewModel> _outfitDrafts = [];
    private readonly PatchingService _patchingService;
    private readonly ArmorPreviewService _previewService;
    private int _activeLoadingOperations;

    private string? _lastLoadedOutfitPlugin;
    private string? _lastLoadedTargetPlugin;
    private ObservableCollection<ArmorRecordViewModel> _outfitArmors = [];
    private ICollectionView? _outfitArmorsView;
    private IList _selectedOutfitArmors = new List<ArmorRecordViewModel>();
    private IList _selectedSourceArmors = new List<ArmorRecordViewModel>();
    private ObservableCollection<ArmorRecordViewModel> _sourceArmors = [];
    private ICollectionView? _sourceArmorsView;
    private ObservableCollection<ArmorRecordViewModel> _targetArmors = [];
    private ICollectionView? _targetArmorsView;

    public MainViewModel(
        MutagenService mutagenService,
        PatchingService patchingService,
        ArmorPreviewService previewService,
        SettingsViewModel settingsViewModel,
        DistributionViewModel distributionViewModel,
        ILoggingService loggingService)
    {
        _mutagenService = mutagenService;
        _patchingService = patchingService;
        _previewService = previewService;
        Settings = settingsViewModel;
        Distribution = distributionViewModel;
        _logger = loggingService.ForContext<MainViewModel>();

        // Subscribe to plugin list changes so we can refresh the available plugins dropdown
        _mutagenService.PluginsChanged += OnPluginsChanged;

        ConfigureSourceArmorsView();
        ConfigureTargetArmorsView();
        ConfigureOutfitArmorsView();
        OutfitDrafts = new ReadOnlyObservableCollection<OutfitDraftViewModel>(_outfitDrafts);
        HasOutfitDrafts = _outfitDrafts.Count > 0;
        _outfitDrafts.CollectionChanged += (_, _) => HasOutfitDrafts = _outfitDrafts.Count > 0;

        ExistingOutfits = new ReadOnlyObservableCollection<ExistingOutfitViewModel>(_existingOutfits);
        HasExistingPluginOutfits = _existingOutfits.Count > 0;
        _existingOutfits.CollectionChanged += (_, _) => HasExistingPluginOutfits = _existingOutfits.Count > 0;

        // Update IsProgressActive when IsPatching or IsCreatingOutfits changes
        this.WhenAnyValue(x => x.IsPatching, x => x.IsCreatingOutfits)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsProgressActive)));

        // Refresh views when search text changes
        this.WhenAnyValue(x => x.SourceSearchText)
            .Subscribe(_ => SourceArmorsView?.Refresh());
        this.WhenAnyValue(x => x.TargetSearchText)
            .Subscribe(_ => TargetArmorsView?.Refresh());
        this.WhenAnyValue(x => x.OutfitSearchText)
            .Subscribe(_ => OutfitArmorsView?.Refresh());

        InitializeCommand = ReactiveCommand.CreateFromTask(InitializeAsync);
        CreatePatchCommand = ReactiveCommand.CreateFromTask(CreatePatchAsync,
            this.WhenAnyValue(x => x.Matches.Count, count => count > 0));
        ClearMappingsCommand = ReactiveCommand.Create(ClearMappings,
            this.WhenAnyValue(x => x.Matches.Count, count => count > 0));
        MapSelectedCommand = ReactiveCommand.Create(MapSelected,
            this.WhenAnyValue(
                x => x.SelectedSourceArmors,
                x => x.SelectedTargetArmor,
                (sources, target) => sources.OfType<ArmorRecordViewModel>().Any() && target != null));
        MapGlamOnlyCommand = ReactiveCommand.Create(MapSelectedAsGlamOnly,
            this.WhenAnyValue(
                x => x.SelectedSourceArmors,
                sources => sources.OfType<ArmorRecordViewModel>().Any()));
        RemoveMappingCommand = ReactiveCommand.Create<ArmorMatchViewModel>(RemoveMapping);

        var canCreateOutfit = this.WhenAnyValue(x => x.SelectedOutfitArmorCount, count => count > 0);
        var canSaveOutfits = this.WhenAnyValue(x => x.HasOutfitDrafts, x => x.IsCreatingOutfits,
            (hasDrafts, isBusy) => hasDrafts && !isBusy);

        CreateOutfitCommand = ReactiveCommand.CreateFromTask(CreateOutfitAsync, canCreateOutfit);
        SaveOutfitsCommand = ReactiveCommand.CreateFromTask(SaveOutfitsAsync, canSaveOutfits);

        var canLoadOutfitPlugin =
            this.WhenAnyValue(x => x.SelectedOutfitPlugin, plugin => !string.IsNullOrWhiteSpace(plugin));
        LoadOutfitPluginCommand =
            ReactiveCommand.CreateFromTask(() => LoadOutfitPluginAsync(forceReload: true), canLoadOutfitPlugin);

        var canCopyExistingOutfits = this.WhenAnyValue(x => x.HasExistingPluginOutfits);
        CopyExistingOutfitsCommand = ReactiveCommand.Create(CopyExistingOutfits, canCopyExistingOutfits);
    }

    public Interaction<string, Unit> PatchCreatedNotification { get; } = new();
    public Interaction<string, bool> ConfirmOverwritePatch { get; } = new();
    public Interaction<string, string?> RequestOutfitName { get; } = new();
    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    public SettingsViewModel Settings { get; }
    public DistributionViewModel Distribution { get; }

    [Reactive] public ObservableCollection<string> AvailablePlugins { get; set; } = [];

    public ObservableCollection<ArmorRecordViewModel> SourceArmors
    {
        get => _sourceArmors;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceArmors, value);
            ConfigureSourceArmorsView();
        }
    }

    public ObservableCollection<ArmorRecordViewModel> TargetArmors
    {
        get => _targetArmors;
        set
        {
            this.RaiseAndSetIfChanged(ref _targetArmors, value);
            ConfigureTargetArmorsView();
        }
    }

    [Reactive] public ObservableCollection<ArmorMatchViewModel> Matches { get; set; } = [];

    [Reactive] public string SourceSearchText { get; set; } = string.Empty;
    [Reactive] public string TargetSearchText { get; set; } = string.Empty;

    public ObservableCollection<ArmorRecordViewModel> OutfitArmors
    {
        get => _outfitArmors;
        set
        {
            this.RaiseAndSetIfChanged(ref _outfitArmors, value);
            ConfigureOutfitArmorsView();
        }
    }

    public ICollectionView? OutfitArmorsView
    {
        get => _outfitArmorsView;
        private set
        {
            _outfitArmorsView = value;
            this.RaisePropertyChanged();
        }
    }

    [Reactive] public string OutfitSearchText { get; set; } = string.Empty;

    public IList SelectedOutfitArmors
    {
        get => _selectedOutfitArmors;
        set
        {
            if (value.Equals(_selectedOutfitArmors))
                return;

            _selectedOutfitArmors = value;
            this.RaisePropertyChanged();
            SelectedOutfitArmorCount = _selectedOutfitArmors.OfType<ArmorRecordViewModel>().Count();
        }
    }

    [Reactive] public int SelectedOutfitArmorCount { get; private set; }

    public string? SelectedOutfitPlugin
    {
        get => field;
        set
        {
            if (string.Equals(value, field, StringComparison.Ordinal))
                return;

            _existingOutfits.Clear();

            this.RaiseAndSetIfChanged(ref field, value);
            _logger.Information("Selected outfit plugin set to {Plugin}", value ?? "<none>");

            _lastLoadedOutfitPlugin = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                OutfitArmors = [];
                SelectedOutfitArmors = Array.Empty<ArmorRecordViewModel>();
                OutfitSearchText = string.Empty;
                return;
            }

            _ = LoadOutfitPluginAsync(forceReload: true);
        }
    }

    public ReadOnlyObservableCollection<OutfitDraftViewModel> OutfitDrafts { get; }

    public ReadOnlyObservableCollection<ExistingOutfitViewModel> ExistingOutfits { get; }

    [Reactive] public bool IsCreatingOutfits { get; private set; }

    [Reactive] public bool HasOutfitDrafts { get; private set; }

    [Reactive] public bool HasExistingPluginOutfits { get; private set; }

    public IList SelectedSourceArmors
    {
        get => _selectedSourceArmors;
        set
        {
            if (value.Equals(_selectedSourceArmors))
                return;

            _selectedSourceArmors = value;
            this.RaisePropertyChanged();

            var primary = SelectedSourceArmor;
            UpdateTargetSlotCompatibility();

            if (_targetArmors.Count == 0 || primary == null)
            {
                SelectedTargetArmor = null;
                return;
            }

            var existing = Matches.FirstOrDefault(m => m.Source.Armor.FormKey == primary.Armor.FormKey);
            if (existing?.Target != null)
                SelectedTargetArmor =
                    _targetArmors.FirstOrDefault(t => t.Armor.FormKey == existing.Target.Armor.FormKey);
            else
                SelectedTargetArmor = _targetArmors.FirstOrDefault(t => primary.SharesSlotWith(t));
        }
    }

    private ArmorRecordViewModel? SelectedSourceArmor =>
        _selectedSourceArmors.OfType<ArmorRecordViewModel>().FirstOrDefault();

    [Reactive] public ArmorRecordViewModel? SelectedTargetArmor { get; set; }

    public ICollectionView? SourceArmorsView
    {
        get => _sourceArmorsView;
        private set
        {
            _sourceArmorsView = value;
            this.RaisePropertyChanged();
        }
    }

    public ICollectionView? TargetArmorsView
    {
        get => _targetArmorsView;
        private set
        {
            _targetArmorsView = value;
            this.RaisePropertyChanged();
        }
    }

    public string? SelectedSourcePlugin
    {
        get => field;
        set
        {
            if (string.Equals(value, field, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref field, value);
            _logger.Information("Selected source plugin set to {Plugin}", value ?? "<none>");

            ClearMappingsInternal();
            SourceArmors = [];
            SelectedSourceArmors = Array.Empty<ArmorRecordViewModel>();
            SourceSearchText = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return;

            _ = LoadSourceArmorsAsync(value);
        }
    }

    public string? SelectedTargetPlugin
    {
        get => field;
        set
        {
            if (string.Equals(value, field, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref field, value);
            _logger.Information("Selected target plugin set to {Plugin}", value ?? "<none>");

            _lastLoadedTargetPlugin = null;
            TargetSearchText = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                ClearMappingsInternal();
                TargetArmors = [];
                SelectedTargetArmor = null;
                return;
            }

            _ = LoadTargetPluginAsync(forceOutfitReload: true);
        }
    }

    [Reactive] public bool IsLoading { get; set; }

    [Reactive] public bool IsPatching { get; set; }

    public bool IsProgressActive => IsPatching || IsCreatingOutfits;

    [Reactive] public string StatusMessage { get; set; } = "Ready";

    [Reactive] public int ProgressCurrent { get; set; }

    [Reactive] public int ProgressTotal { get; set; }

    public ICommand InitializeCommand { get; }
    public ICommand CreatePatchCommand { get; }
    public ICommand ClearMappingsCommand { get; }
    public ICommand MapSelectedCommand { get; }
    public ICommand MapGlamOnlyCommand { get; }
    public ReactiveCommand<ArmorMatchViewModel, Unit> RemoveMappingCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateOutfitCommand { get; }

    public ReactiveCommand<Unit, Unit> SaveOutfitsCommand { get; }

    /// <summary>Command to load outfit plugin.</summary>
    public ReactiveCommand<Unit, Unit> LoadOutfitPluginCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyExistingOutfitsCommand { get; }

    private async Task<int> LoadExistingOutfitsAsync(string plugin)
    {
        _existingOutfits.Clear();

        if (_mutagenService.LinkCache == null)
        {
            _logger.Warning("Link cache unavailable; skipping outfit discovery for {Plugin}.", plugin);
            return 0;
        }

        var outfits = (await _mutagenService.LoadOutfitsFromPluginAsync(plugin)).ToList();

        if (!string.Equals(SelectedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
            return 0;

        var linkCache = _mutagenService.LinkCache;
        var pluginModKey = ModKey.FromFileName(plugin);
        var discoveredCount = 0;

        foreach (var outfit in outfits)
        {
            if (outfit.FormKey.ModKey != pluginModKey)
                continue;

            var itemLinks = outfit.Items ?? Array.Empty<IFormLinkGetter<IOutfitTargetGetter>>();
            var armorPieces = new List<IArmorGetter>();

            foreach (var entry in itemLinks)
            {
                if (entry == null)
                    continue;

                var formKeyNullable = entry.FormKeyNullable;
                if (!formKeyNullable.HasValue || formKeyNullable.Value == FormKey.Null)
                    continue;

                var formKey = formKeyNullable.Value;

                if (!linkCache.TryResolve<IItemGetter>(formKey, out var item))
                {
                    _logger.Debug("Unable to resolve outfit item {FormKey} for outfit {EditorId} in {Plugin}.",
                        formKey, outfit.EditorID ?? "(No EditorID)", plugin);
                    continue;
                }

                if (item is not IArmorGetter armor)
                {
                    _logger.Debug("Skipping non-armor item {FormKey} ({Type}) in outfit {EditorId}.",
                        formKey, item.GetType().Name, outfit.EditorID ?? "(No EditorID)");
                    continue;
                }

                armorPieces.Add(armor);
            }

            var distinctPieces = armorPieces
                .GroupBy(p => p.FormKey)
                .Select(g => g.First())
                .ToList();

            if (distinctPieces.Count == 0)
                continue;

            var editorId = outfit.EditorID ?? SanitizeOutfitName(outfit.FormKey.ToString());
            var displayName = editorId;

            var existing = new ExistingOutfitViewModel(
                displayName,
                editorId,
                outfit.FormKey,
                distinctPieces);

            _existingOutfits.Add(existing);
            discoveredCount++;

            _logger.Information("Discovered existing outfit {EditorId} in {Plugin} with {PieceCount} piece(s).",
                editorId, plugin, distinctPieces.Count);
        }

        return discoveredCount;
    }

    private void CopyExistingOutfits()
    {
        if (_existingOutfits.Count == 0)
        {
            StatusMessage = "No existing outfits to copy.";
            _logger.Debug("CopyExistingOutfits invoked with no discovered outfits.");
            return;
        }

        if (_mutagenService.LinkCache == null)
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
                _logger.Debug("Skipping existing outfit {EditorId} because it is already queued.", existing.EditorId);
                continue;
            }

            var baseName = SanitizeOutfitName(existing.EditorId);
            var uniqueName = EnsureUniqueOutfitName(baseName, null);

            if (!string.Equals(uniqueName, baseName, StringComparison.Ordinal))
                _logger.Debug("Adjusted outfit name from {Original} to {Adjusted} when copying existing outfit.",
                    baseName, uniqueName);

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
                PreviewDraftAsync);

            draft.FormKey = existing.FormKey;
            draft.PropertyChanged += OutfitDraftOnPropertyChanged;
            _outfitDrafts.Add(draft);
            copied++;
        }

        _existingOutfits.Clear();

        if (copied > 0)
        {
            StatusMessage = $"Copied {copied} existing outfit(s) into the queue.";
            _logger.Information("Copied {CopiedCount} existing outfit(s) into the queue from plugin {Plugin}.",
                copied,
                SelectedOutfitPlugin ?? "<none>");
        }
        else
        {
            StatusMessage = "Existing outfits are already queued or could not be copied.";
            _logger.Information("No existing outfits were copied; they may already be queued or lacked valid pieces.");
        }
    }

    private void ConfigureSourceArmorsView()
    {
        SourceArmorsView = CollectionViewSource.GetDefaultView(_sourceArmors);
        SourceArmorsView?.Filter = SourceArmorsFilter;
    }

    private void ConfigureTargetArmorsView()
    {
        TargetArmorsView = CollectionViewSource.GetDefaultView(_targetArmors);
        if (TargetArmorsView != null)
        {
            TargetArmorsView.Filter = TargetArmorsFilter;
            ApplyTargetSort();
        }

        UpdateTargetSlotCompatibility();
    }

    private void ConfigureOutfitArmorsView()
    {
        OutfitArmorsView = CollectionViewSource.GetDefaultView(_outfitArmors);
        OutfitArmorsView?.Filter = OutfitArmorsFilter;
    }

    private bool SourceArmorsFilter(object? item)
    {
        if (item is not ArmorRecordViewModel record)
            return false;

        return record.MatchesSearch(SourceSearchText);
    }

    private bool TargetArmorsFilter(object? item)
    {
        if (item is not ArmorRecordViewModel record)
            return false;

        return record.MatchesSearch(TargetSearchText);
    }

    private bool OutfitArmorsFilter(object? item)
    {
        if (item is not ArmorRecordViewModel record)
            return false;

        return record.MatchesSearch(OutfitSearchText);
    }

    private void UpdateTargetSlotCompatibility()
    {
        var sources = SelectedSourceArmors.OfType<ArmorRecordViewModel>().ToList();

        if (sources.Count == 0)
        {
            foreach (var target in _targetArmors)
                target.IsSlotCompatible = true;
            return;
        }

        foreach (var target in _targetArmors)
            target.IsSlotCompatible = sources.All(source => source.SharesSlotWith(target));

        TargetArmorsView?.Refresh();
    }

    private void MapSelected()
    {
        var sources = SelectedSourceArmors.OfType<ArmorRecordViewModel>().ToList();
        var target = SelectedTargetArmor;

        if (sources.Count == 0 || target == null)
        {
            _logger.Debug(
                "MapSelected invoked without valid selections. SourceCount={SourceCount}, HasTarget={HasTarget}",
                sources.Count, target != null);
            return;
        }

        try
        {
            foreach (var source in sources)
            {
                var existing = Matches.FirstOrDefault(m => m.Source.Armor.FormKey == source.Armor.FormKey);
                if (existing != null)
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
            _logger.Information("Mapped {SourceCount} armor(s) to target {TargetName} ({TargetFormKey})", sources.Count,
                target.DisplayName, target.Armor.FormKey);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to map {SourceCount} armor(s) to {TargetName}", sources.Count,
                target.DisplayName);
            StatusMessage = $"Error mapping armors: {ex.Message}";
        }
    }

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
                if (existing != null)
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

    private void ClearMappings()
    {
        if (!ClearMappingsInternal())
            return;
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
            _activeLoadingOperations--;

        IsLoading = _activeLoadingOperations > 0;
    }

    private bool ClearMappingsInternal()
    {
        if (Matches.Count == 0)
            return false;

        foreach (var mapping in Matches.ToList())
            mapping.Source.IsMapped = false;

        Matches.Clear();
        return true;
    }

    private void RemoveMapping(ArmorMatchViewModel mapping)
    {
        if (!Matches.Contains(mapping))
            return;
        Matches.Remove(mapping);
        mapping.Source.IsMapped = Matches.Any(m => m.Source.Armor.FormKey == mapping.Source.Armor.FormKey);
        StatusMessage = $"Removed mapping for {mapping.Source.DisplayName}";
        _logger.Information("Removed mapping for source {SourceName} ({SourceFormKey})", mapping.Source.DisplayName,
            mapping.Source.Armor.FormKey);
    }

    public async Task LoadTargetPluginAsync(bool forceOutfitReload = false)
    {
        var plugin = SelectedTargetPlugin;
        if (string.IsNullOrWhiteSpace(plugin))
            return;

        var needsReload = forceOutfitReload ||
                          !string.Equals(_lastLoadedTargetPlugin, plugin, StringComparison.OrdinalIgnoreCase);

        if (needsReload)
        {
            ClearMappingsInternal();
            TargetArmors = [];
            SelectedTargetArmor = null;
            await LoadTargetArmorsAsync(plugin);
        }

        await SyncOutfitPluginWithTargetAsync(plugin, forceOutfitReload);
    }

    public async Task LoadOutfitPluginAsync(bool forceReload = false)
    {
        var plugin = SelectedOutfitPlugin;
        if (string.IsNullOrWhiteSpace(plugin))
            return;

        if (!forceReload && string.Equals(_lastLoadedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
            return;

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
            await LoadOutfitPluginAsync(true);
    }

    private async Task InitializeAsync()
    {
        BeginLoading();
        StatusMessage = "Initializing Mutagen...";
        _logger.Information("Initializing Mutagen with data path {DataPath}", Settings.SkyrimDataPath);

        try
        {
            await _mutagenService.InitializeAsync(Settings.SkyrimDataPath);

            var plugins = await _mutagenService.GetAvailablePluginsAsync();
            AvailablePlugins = new ObservableCollection<string>(plugins);

            StatusMessage = $"Loaded {AvailablePlugins.Count} plugins";
            _logger.Information("Loaded {PluginCount} plugins from {DataPath}", AvailablePlugins.Count,
                Settings.SkyrimDataPath);

            // GameDataCacheService automatically loads when MutagenService.Initialized fires
            // No need to manually trigger refresh here - it would invalidate the cross-session cache
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
            var plugins = await _mutagenService.GetAvailablePluginsAsync();
            var previousCount = AvailablePlugins.Count;
            AvailablePlugins = new ObservableCollection<string>(plugins);

            _logger.Information("Available plugins refreshed: {PreviousCount} → {NewCount} plugins.",
                previousCount, AvailablePlugins.Count);
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
                return;

            SourceArmors = new ObservableCollection<ArmorRecordViewModel>(
                armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
            SourceSearchText = string.Empty;
            SourceArmorsView?.Refresh();

            var firstSource = SourceArmors.FirstOrDefault();
            SelectedSourceArmors = firstSource != null
                ? new List<ArmorRecordViewModel> { firstSource }
                : Array.Empty<ArmorRecordViewModel>();

            StatusMessage = $"Loaded {SourceArmors.Count} armors from {plugin}";
            _logger.Information("Loaded {ArmorCount} source armors from {Plugin}", SourceArmors.Count, plugin);
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
                return;

            TargetArmors = new ObservableCollection<ArmorRecordViewModel>(
                armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
            _lastLoadedTargetPlugin = plugin;
            TargetSearchText = string.Empty;
            TargetArmorsView?.Refresh();
            var primary = SelectedSourceArmor;
            SelectedTargetArmor = primary != null
                ? TargetArmors.FirstOrDefault(t => primary.SharesSlotWith(t))
                : TargetArmors.FirstOrDefault();

            StatusMessage = $"Loaded {TargetArmors.Count} armors from {plugin}";
            _logger.Information("Loaded {ArmorCount} target armors from {Plugin}", TargetArmors.Count, plugin);
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
            OutfitArmors = [];
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
                return;

            OutfitArmors = new ObservableCollection<ArmorRecordViewModel>(
                armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
            _lastLoadedOutfitPlugin = plugin;
            OutfitSearchText = string.Empty;
            OutfitArmorsView?.Refresh();

            SelectedOutfitArmors = OutfitArmors.Any()
                ? new List<ArmorRecordViewModel> { OutfitArmors[0] }
                : Array.Empty<ArmorRecordViewModel>();

            var existingOutfitCount = await LoadExistingOutfitsAsync(plugin);

            StatusMessage = existingOutfitCount > 0
                ? $"Loaded {OutfitArmors.Count} armors from {plugin}. {existingOutfitCount} existing outfit(s) available to copy."
                : $"Loaded {OutfitArmors.Count} armors from {plugin} for outfit creation.";
            _logger.Information(
                "Loaded {ArmorCount} outfit armors from {Plugin}. Existing outfits available to copy: {ExistingCount}.",
                OutfitArmors.Count,
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
                continue;

            // Use cached enum values instead of calling Enum.GetValues repeatedly
            foreach (var flag in BipedObjectFlags)
            {
                if (!mask.HasFlag(flag))
                    continue;

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

        while (_outfitDrafts.Any(o =>
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
            return string.Empty;

        var builder = new StringBuilder();
        while (index > 0)
        {
            index--;
            builder.Insert(0, (char)('A' + (index % 26)));
            index /= 26;
        }

        return builder.ToString();
    }

    private static string SanitizeOutfitName(string? value)
    {
        var sanitized = value is null ? string.Empty : OutfitNameSanitizer.Replace(value, string.Empty);
        return string.IsNullOrEmpty(sanitized) ? "Outfit" : sanitized;
    }

    private static List<ArmorRecordViewModel> DistinctArmorPieces(IEnumerable<ArmorRecordViewModel> pieces)
    {
        return pieces
            .GroupBy(p => p.Armor.FormKey)
            .Select(g => g.First())
            .ToList();
    }

    private async Task CreateOutfitAsync()
    {
        var selectedPieces = SelectedOutfitArmors
            .OfType<ArmorRecordViewModel>()
            .ToList();

        await CreateOutfitFromPiecesAsync(selectedPieces);
    }

    public async Task CreateOutfitFromPiecesAsync(IReadOnlyList<ArmorRecordViewModel> pieces)
    {
        // Move validation to background thread to avoid blocking UI
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

        // Yield to UI thread to allow UI to update before showing popup
        await Task.Yield();

        const string namePrompt = "Enter the outfit name (also used as the EditorID):";
        var outfitName = await RequestOutfitName.Handle(namePrompt).ToTask();

        if (string.IsNullOrWhiteSpace(outfitName))
        {
            StatusMessage = "Outfit creation canceled.";
            _logger.Information("Outfit creation canceled by user.");
            return;
        }

        var trimmedName = outfitName.Trim();
        var sanitizedName = SanitizeOutfitName(trimmedName);
        sanitizedName = EnsureUniqueOutfitName(sanitizedName, null);

        if (!string.Equals(trimmedName, sanitizedName, StringComparison.Ordinal))
            _logger.Debug("Outfit name sanitized from {Original} to {Sanitized}", trimmedName, sanitizedName);

        var draft = new OutfitDraftViewModel(
            sanitizedName,
            sanitizedName,
            distinctPieces,
            RemoveOutfitDraft,
            RemoveOutfitPiece,
            PreviewDraftAsync);

        draft.PropertyChanged += OutfitDraftOnPropertyChanged;
        _outfitDrafts.Add(draft);

        StatusMessage = $"Queued outfit '{draft.Name}' with {distinctPieces.Count} piece(s).";
        _logger.Information("Queued outfit draft {EditorId} with {PieceCount} pieces.", draft.EditorId,
            distinctPieces.Count);
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
            var scene = await _previewService.BuildPreviewAsync(pieces, GenderedModelVariant.Female);
            await ShowPreview.Handle(scene);
            StatusMessage = $"Preview ready for '{draft.EditorId}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview error: {ex.Message}";
            _logger.Error(ex, "Failed to build outfit preview for {EditorId}.", draft.EditorId);
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
            if (existingConflict != null)
            {
                var overlap = piece.SlotMask & existingConflict.SlotMask;
                var slot = overlap != 0 ? overlap.ToString() : piece.SlotSummary;
                StatusMessage = $"Slot conflict: {piece.DisplayName} overlaps {existingConflict.DisplayName} ({slot}).";
                _logger.Warning(
                    "Prevented adding {Piece} to outfit {EditorId} due to conflict with {Existing} on slot {Slot}.",
                    piece.DisplayName, draft.EditorId, existingConflict.DisplayName, slot);
                return false;
            }

            var stagedConflict = stagedPieces.FirstOrDefault(sp => piece.ConflictsWithSlot(sp));
            if (stagedConflict != null)
            {
                var overlap = piece.SlotMask & stagedConflict.SlotMask;
                var slot = overlap != 0 ? overlap.ToString() : piece.SlotSummary;
                StatusMessage = $"Slot conflict: {piece.DisplayName} overlaps {stagedConflict.DisplayName} ({slot}).";
                _logger.Warning(
                    "Prevented adding {Piece} to outfit {EditorId} due to conflict with staged piece {Staged} on slot {Slot}.",
                    piece.DisplayName, draft.EditorId, stagedConflict.DisplayName, slot);
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

    private void OutfitDraftOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OutfitDraftViewModel draft)
            return;

        if (e.PropertyName == nameof(OutfitDraftViewModel.Name))
            HandleOutfitDraftRename(draft);
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
            _logger.Information("Adjusted outfit draft name from {Original} to {Adjusted} to ensure uniqueness.",
                original, uniqueName);
            return;
        }

        StatusMessage = $"Renamed outfit to '{draft.Name}'.";
        _logger.Information("Renamed outfit draft to {Name}", draft.Name);
    }

    private void RemoveOutfitDraft(OutfitDraftViewModel draft)
    {
        draft.PropertyChanged -= OutfitDraftOnPropertyChanged;

        if (!_outfitDrafts.Remove(draft))
            return;
        StatusMessage = $"Removed outfit '{draft.EditorId}'.";
        _logger.Information("Removed outfit draft {EditorId}.", draft.EditorId);
    }

    private void RemoveOutfitPiece(OutfitDraftViewModel draft, ArmorRecordViewModel piece)
    {
        draft.RemovePiece(piece);
        StatusMessage = $"Removed {piece.DisplayName} from outfit '{draft.EditorId}'.";
        _logger.Information("Removed armor {Armor} from outfit draft {EditorId}.", piece.DisplayName, draft.EditorId);
    }

    private async Task SaveOutfitsAsync()
    {
        if (_outfitDrafts.Count == 0)
        {
            StatusMessage = "No outfits queued for creation.";
            _logger.Debug("SaveOutfitsAsync invoked with no drafts.");
            return;
        }

        var populatedDrafts = _outfitDrafts.Where(d => d.HasPieces).ToList();
        if (populatedDrafts.Count == 0)
        {
            StatusMessage = "All queued outfits are empty.";
            _logger.Warning("SaveOutfitsAsync found no outfit drafts with pieces.");
            return;
        }

        IsCreatingOutfits = true;

        try
        {
            ProgressCurrent = 0;
            ProgressTotal = populatedDrafts.Count;

            var requests = populatedDrafts
                .Select(d => new OutfitCreationRequest(
                    d.Name,
                    d.EditorId,
                    d.GetPieces().Select(p => p.Armor).ToList()))
                .ToList();

            var progress = new Progress<(int current, int total, string message)>(p =>
            {
                ProgressCurrent = p.current;
                ProgressTotal = p.total;
                StatusMessage = p.message;
            });

            var outputPath = Settings.FullOutputPath;
            _logger.Information("Saving {Count} outfit(s) to patch {OutputPath}.", requests.Count, outputPath);

            var (success, message, results) = await _patchingService.CreateOrUpdateOutfitsAsync(
                requests,
                outputPath,
                progress);

            StatusMessage = message;

            if (success && results.Count > 0)
                foreach (var result in results)
                {
                    var draft = _outfitDrafts.FirstOrDefault(d =>
                        string.Equals(d.EditorId, result.EditorId, StringComparison.OrdinalIgnoreCase));

                    draft?.FormKey = result.FormKey;
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

    public void ApplyTargetSort(string? propertyName = nameof(ArmorRecordViewModel.DisplayName),
        ListSortDirection direction = ListSortDirection.Ascending)
    {
        if (TargetArmorsView is not ListCollectionView view)
            return;
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(nameof(ArmorRecordViewModel.SlotCompatibilityPriority),
            ListSortDirection.Ascending));

        if (!string.IsNullOrEmpty(propertyName))
            view.SortDescriptions.Add(new SortDescription(propertyName, direction));

        view.Refresh();
    }

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
                .Where(m => m.Match.TargetArmor != null || m.Match.IsGlamOnly)
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

            _logger.Information("Starting patch creation for {MatchCount} matches to {OutputPath}",
                matchesToPatch.Count, Settings.FullOutputPath);

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
}

public class ArmorMatchViewModel : ReactiveObject
{
    public ArmorMatchViewModel(
        ArmorMatch match,
        ArmorRecordViewModel source,
        ArmorRecordViewModel? target)
    {
        Match = match;
        Source = source;

        if (target != null)
            ApplyAutoTarget(target);
        else if (match.IsGlamOnly)
            ApplyGlamOnly();
        else
            RefreshState();
    }

    public ArmorMatch Match { get; }
    public ArmorRecordViewModel Source { get; }

    [Reactive] public ArmorRecordViewModel? Target { get; private set; }

    public bool HasTarget => Match.IsGlamOnly || Target != null;
    public bool IsGlamOnly => Match.IsGlamOnly;
    public string SourceSummary => Source.SummaryLine;

    public string TargetSummary
    {
        get
        {
            if (Match.IsGlamOnly)
                return "✨ Glam-only (armor rating set to 0)";
            if (Target != null)
                return Target.SummaryLine;
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
