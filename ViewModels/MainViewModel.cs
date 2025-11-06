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
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using Serilog;

namespace Boutique.ViewModels;

public class MainViewModel : ReactiveObject
{
    private static readonly Regex OutfitNameSanitizer = new("[^A-Za-z]", RegexOptions.Compiled);
    private readonly ILogger _logger;
    private readonly IMatchingService _matchingService;
    private readonly IMutagenService _mutagenService;
    private readonly ObservableCollection<OutfitDraftViewModel> _outfitDrafts = new();
    private readonly IPatchingService _patchingService;
    private readonly IArmorPreviewService _previewService;
    private int _activeLoadingOperations;
    private double _autoMatchThreshold = 0.6;

    private ObservableCollection<string> _availablePlugins = new();
    private bool _hasOutfitDrafts;
    private bool _isCreatingOutfits;
    private bool _isLoading;
    private bool _isPatching;
    private ObservableCollection<ArmorMatchViewModel> _matches = new();
    private ObservableCollection<ArmorRecordViewModel> _outfitArmors = new();
    private ICollectionView? _outfitArmorsView;
    private string _outfitSearchText = string.Empty;
    private int _progressCurrent;
    private int _progressTotal;
    private int _selectedOutfitArmorCount;
    private IList _selectedOutfitArmors = new List<ArmorRecordViewModel>();
    private string? _selectedOutfitPlugin;
    private IList _selectedSourceArmors = new List<ArmorRecordViewModel>();

    private string? _selectedSourcePlugin;
    private ArmorRecordViewModel? _selectedTargetArmor;
    private string? _selectedTargetPlugin;
    private ObservableCollection<ArmorRecordViewModel> _sourceArmors = new();
    private ICollectionView? _sourceArmorsView;
    private string _sourceSearchText = string.Empty;
    private string _statusMessage = "Ready";
    private ObservableCollection<ArmorRecordViewModel> _targetArmors = new();
    private ICollectionView? _targetArmorsView;
    private string _targetSearchText = string.Empty;

    public MainViewModel(
        IMutagenService mutagenService,
        IMatchingService matchingService,
        IPatchingService patchingService,
        IArmorPreviewService previewService,
        SettingsViewModel settingsViewModel,
        DistributionViewModel distributionViewModel,
        ILoggingService loggingService)
    {
        _mutagenService = mutagenService;
        _matchingService = matchingService;
        _patchingService = patchingService;
        _previewService = previewService;
        Settings = settingsViewModel;
        Distribution = distributionViewModel;
        _logger = loggingService.ForContext<MainViewModel>();

        ConfigureSourceArmorsView();
        ConfigureTargetArmorsView();
        ConfigureOutfitArmorsView();
        OutfitDrafts = new ReadOnlyObservableCollection<OutfitDraftViewModel>(_outfitDrafts);
        HasOutfitDrafts = _outfitDrafts.Count > 0;
        _outfitDrafts.CollectionChanged += (_, _) => HasOutfitDrafts = _outfitDrafts.Count > 0;

        InitializeCommand = ReactiveCommand.CreateFromTask(InitializeAsync);
        AutoMatchCommand = ReactiveCommand.CreateFromTask(AutoMatchAsync,
            this.WhenAnyValue(
                x => x.SourceArmors.Count,
                x => x.TargetArmors.Count,
                (source, target) => source > 0 && target > 0));
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
    }

    public Interaction<string, Unit> PatchCreatedNotification { get; } = new();
    public Interaction<string, bool> ConfirmOverwritePatch { get; } = new();
    public Interaction<string, string?> RequestOutfitName { get; } = new();
    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    public SettingsViewModel Settings { get; }
    public DistributionViewModel Distribution { get; }

    public ObservableCollection<string> AvailablePlugins
    {
        get => _availablePlugins;
        set => this.RaiseAndSetIfChanged(ref _availablePlugins, value);
    }

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

    public ObservableCollection<ArmorMatchViewModel> Matches
    {
        get => _matches;
        set => this.RaiseAndSetIfChanged(ref _matches, value);
    }

    public string SourceSearchText
    {
        get => _sourceSearchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceSearchText, value);
            SourceArmorsView?.Refresh();
        }
    }

    public string TargetSearchText
    {
        get => _targetSearchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _targetSearchText, value);
            TargetArmorsView?.Refresh();
        }
    }

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

    public string OutfitSearchText
    {
        get => _outfitSearchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _outfitSearchText, value);
            OutfitArmorsView?.Refresh();
        }
    }

    public IList SelectedOutfitArmors
    {
        get => _selectedOutfitArmors;
        set
        {
            if (value == _selectedOutfitArmors)
                return;

            _selectedOutfitArmors = value ?? Array.Empty<object>();
            this.RaisePropertyChanged();
            SelectedOutfitArmorCount = _selectedOutfitArmors.OfType<ArmorRecordViewModel>().Count();
        }
    }

    public int SelectedOutfitArmorCount
    {
        get => _selectedOutfitArmorCount;
        private set => this.RaiseAndSetIfChanged(ref _selectedOutfitArmorCount, value);
    }

    public string? SelectedOutfitPlugin
    {
        get => _selectedOutfitPlugin;
        set
        {
            if (string.Equals(value, _selectedOutfitPlugin, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _selectedOutfitPlugin, value);
            _logger.Information("Selected outfit plugin set to {Plugin}", value ?? "<none>");

            _ = LoadOutfitArmorsAsync(value ?? string.Empty);
        }
    }

    public ReadOnlyObservableCollection<OutfitDraftViewModel> OutfitDrafts { get; }

    public bool IsCreatingOutfits
    {
        get => _isCreatingOutfits;
        private set
        {
            if (this.RaiseAndSetIfChanged(ref _isCreatingOutfits, value))
                this.RaisePropertyChanged(nameof(IsProgressActive));
        }
    }

    public bool HasOutfitDrafts
    {
        get => _hasOutfitDrafts;
        private set => this.RaiseAndSetIfChanged(ref _hasOutfitDrafts, value);
    }

    public IList SelectedSourceArmors
    {
        get => _selectedSourceArmors;
        set
        {
            if (value == _selectedSourceArmors)
                return;

            _selectedSourceArmors = value ?? Array.Empty<object>();
            this.RaisePropertyChanged();

            var primary = SelectedSourceArmor;
            UpdateTargetSlotCompatibility();

            if (_targetArmors == null || _targetArmors.Count == 0)
            {
                SelectedTargetArmor = null;
                return;
            }

            if (primary == null)
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

    private bool HasSourceSelection => _selectedSourceArmors.OfType<ArmorRecordViewModel>().Any();

    public ArmorRecordViewModel? SelectedTargetArmor
    {
        get => _selectedTargetArmor;
        set => this.RaiseAndSetIfChanged(ref _selectedTargetArmor, value);
    }

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
        get => _selectedSourcePlugin;
        set
        {
            if (string.Equals(value, _selectedSourcePlugin, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _selectedSourcePlugin, value);
            _logger.Information("Selected source plugin set to {Plugin}", value ?? "<none>");

            ClearMappingsInternal();
            SourceArmors = new ObservableCollection<ArmorRecordViewModel>();
            SelectedSourceArmors = Array.Empty<ArmorRecordViewModel>();

            if (string.IsNullOrWhiteSpace(value)) return;

            _ = LoadSourceArmorsAsync(value);
        }
    }

    public string? SelectedTargetPlugin
    {
        get => _selectedTargetPlugin;
        set
        {
            if (string.Equals(value, _selectedTargetPlugin, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _selectedTargetPlugin, value);
            _logger.Information("Selected target plugin set to {Plugin}", value ?? "<none>");

            ClearMappingsInternal();
            TargetArmors = new ObservableCollection<ArmorRecordViewModel>();
            SelectedTargetArmor = null;

            if (string.IsNullOrWhiteSpace(value)) return;

            _ = LoadTargetArmorsAsync(value);
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public bool IsPatching
    {
        get => _isPatching;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isPatching, value)) this.RaisePropertyChanged(nameof(IsProgressActive));
        }
    }

    public bool IsProgressActive => IsPatching || IsCreatingOutfits;

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public int ProgressCurrent
    {
        get => _progressCurrent;
        set => this.RaiseAndSetIfChanged(ref _progressCurrent, value);
    }

    public int ProgressTotal
    {
        get => _progressTotal;
        set => this.RaiseAndSetIfChanged(ref _progressTotal, value);
    }

    public double AutoMatchThreshold
    {
        get => _autoMatchThreshold;
        set => this.RaiseAndSetIfChanged(ref _autoMatchThreshold, value);
    }

    public ICommand InitializeCommand { get; }
    public ICommand AutoMatchCommand { get; }
    public ICommand CreatePatchCommand { get; }
    public ICommand ClearMappingsCommand { get; }
    public ICommand MapSelectedCommand { get; }
    public ICommand MapGlamOnlyCommand { get; }
    public ReactiveCommand<ArmorMatchViewModel, Unit> RemoveMappingCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateOutfitCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveOutfitsCommand { get; }

    private void ConfigureSourceArmorsView()
    {
        SourceArmorsView = CollectionViewSource.GetDefaultView(_sourceArmors);
        if (SourceArmorsView != null) SourceArmorsView.Filter = SourceArmorsFilter;
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
        if (OutfitArmorsView != null) OutfitArmorsView.Filter = OutfitArmorsFilter;
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
        if (_targetArmors == null)
            return;

        var sources = SelectedSourceArmors.OfType<ArmorRecordViewModel>().ToList();

        if (!sources.Any())
        {
            foreach (var target in _targetArmors) target.IsSlotCompatible = true;
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

        if (!sources.Any() || target == null)
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
                    var match = new ArmorMatch(source.Armor, target.Armor, 1.0, true);
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

        if (!sources.Any())
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
                    var match = new ArmorMatch(source.Armor, null, 1.0, true, true);
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
        if (ClearMappingsInternal())
        {
            StatusMessage = "Cleared all mappings.";
            _logger.Information("Cleared all manual mappings.");
        }
    }

    private void BeginLoading()
    {
        _activeLoadingOperations++;
        IsLoading = true;
    }

    private void EndLoading()
    {
        if (_activeLoadingOperations > 0) _activeLoadingOperations--;

        IsLoading = _activeLoadingOperations > 0;
    }

    private bool ClearMappingsInternal()
    {
        if (Matches.Count == 0)
            return false;

        foreach (var mapping in Matches.ToList()) mapping.Source.IsMapped = false;

        Matches.Clear();
        return true;
    }

    private void RemoveMapping(ArmorMatchViewModel mapping)
    {
        if (Matches.Contains(mapping))
        {
            Matches.Remove(mapping);
            mapping.Source.IsMapped = Matches.Any(m => m.Source.Armor.FormKey == mapping.Source.Armor.FormKey);
            StatusMessage = $"Removed mapping for {mapping.Source.DisplayName}";
            _logger.Information("Removed mapping for source {SourceName} ({SourceFormKey})", mapping.Source.DisplayName,
                mapping.Source.Armor.FormKey);
        }
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

            await Distribution.RefreshAsync();
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

            if (!string.Equals(_selectedSourcePlugin, plugin, StringComparison.OrdinalIgnoreCase))
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
            if (string.Equals(_selectedSourcePlugin, plugin, StringComparison.OrdinalIgnoreCase))
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

            if (!string.Equals(_selectedTargetPlugin, plugin, StringComparison.OrdinalIgnoreCase))
                return;

            TargetArmors = new ObservableCollection<ArmorRecordViewModel>(
                armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
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
            if (string.Equals(_selectedTargetPlugin, plugin, StringComparison.OrdinalIgnoreCase))
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
            OutfitArmors = new ObservableCollection<ArmorRecordViewModel>();
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

            if (!string.Equals(_selectedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
                return;

            OutfitArmors = new ObservableCollection<ArmorRecordViewModel>(
                armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));
            OutfitSearchText = string.Empty;
            OutfitArmorsView?.Refresh();

            SelectedOutfitArmors = OutfitArmors.Any()
                ? new List<ArmorRecordViewModel> { OutfitArmors[0] }
                : Array.Empty<ArmorRecordViewModel>();

            StatusMessage = $"Loaded {OutfitArmors.Count} armors from {plugin} for outfit creation.";
            _logger.Information("Loaded {ArmorCount} outfit armors from {Plugin}", OutfitArmors.Count, plugin);
        }
        catch (Exception ex)
        {
            if (string.Equals(_selectedOutfitPlugin, plugin, StringComparison.OrdinalIgnoreCase))
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

    private bool ValidateOutfitPieces(IReadOnlyList<ArmorRecordViewModel> pieces, out string validationMessage)
    {
        var slotsInUse = new Dictionary<BipedObjectFlag, ArmorRecordViewModel>();

        foreach (var piece in pieces)
        {
            var mask = piece.SlotMask;
            if (mask == 0)
                continue;

            foreach (var flag in Enum.GetValues<BipedObjectFlag>())
            {
                var flagValue = (uint)flag;
                if (flagValue == 0 || (flagValue & (flagValue - 1)) != 0)
                    continue;

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
            builder.Insert(0, (char)('A' + index % 26));
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
            .Where(p => p != null)
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
        var distinctPieces = DistinctArmorPieces(pieces);

        if (!distinctPieces.Any())
        {
            StatusMessage = "Select at least one armor to create an outfit.";
            _logger.Debug("CreateOutfitFromPiecesAsync invoked without any valid pieces.");
            return;
        }

        if (!ValidateOutfitPieces(distinctPieces, out var validationMessage))
        {
            StatusMessage = validationMessage;
            _logger.Warning("Outfit creation blocked due to slot conflict: {Message}", validationMessage);
            return;
        }

        var namePrompt = "Enter the outfit name (also used as the EditorID):";
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
        if (draft == null)
        {
            StatusMessage = "Unable to determine which outfit to update.";
            _logger.Warning("TryAddPiecesToDraft invoked without a target draft.");
            return false;
        }

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

        if (e.PropertyName == nameof(OutfitDraftViewModel.Name)) HandleOutfitDraftRename(draft);
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

        if (_outfitDrafts.Remove(draft))
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

                    if (draft != null) draft.FormKey = result.FormKey;
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

    private async Task AutoMatchAsync()
    {
        StatusMessage = "Auto-matching armors...";
        _logger.Information("Auto-matching armors with threshold {Threshold}", AutoMatchThreshold);

        try
        {
            var sourceList = SourceArmors.Select(vm => vm.Armor).ToList();
            var targetArmors = TargetArmors.Select(vm => vm.Armor).ToList();

            var matchResults = await Task.Run(() =>
                _matchingService.AutoMatchArmors(sourceList, targetArmors, AutoMatchThreshold).ToList());

            var sourceLookup = SourceArmors.ToDictionary(vm => vm.Armor.FormKey);
            var targetLookup = TargetArmors.ToDictionary(vm => vm.Armor.FormKey);

            var mappingViewModels = new ObservableCollection<ArmorMatchViewModel>();
            foreach (var match in matchResults)
            {
                if (!sourceLookup.TryGetValue(match.SourceArmor.FormKey, out var sourceVm))
                    continue;

                ArmorRecordViewModel? targetVm = null;
                if (match.TargetArmor != null &&
                    targetLookup.TryGetValue(match.TargetArmor.FormKey, out var foundTarget)) targetVm = foundTarget;

                mappingViewModels.Add(new ArmorMatchViewModel(match, sourceVm, targetVm));
            }

            Matches = mappingViewModels;
            var matchedCount = Matches.Count(m => m.HasTarget);
            StatusMessage = $"Auto-matched {matchedCount}/{Matches.Count} armors";
            _logger.Information("Auto-match completed with {MatchedCount} mapped armors out of {TotalMatches}",
                matchedCount, Matches.Count);

            foreach (var mapping in Matches) mapping.Source.IsMapped = mapping.HasTarget;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during auto-match: {ex.Message}";
            _logger.Error(ex, "Auto-match failed.");
        }
    }

    public void ApplyTargetSort(string? propertyName = nameof(ArmorRecordViewModel.DisplayName),
        ListSortDirection direction = ListSortDirection.Ascending)
    {
        if (TargetArmorsView is ListCollectionView view)
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(ArmorRecordViewModel.SlotCompatibilityPriority),
                ListSortDirection.Ascending));

            if (!string.IsNullOrEmpty(propertyName))
                view.SortDescriptions.Add(new SortDescription(propertyName, direction));

            view.Refresh();
        }
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
                var confirmationMessage =
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
    private ArmorRecordViewModel? _target;

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

    public ArmorRecordViewModel? Target
    {
        get => _target;
        private set
        {
            this.RaiseAndSetIfChanged(ref _target, value);
            RefreshState();
        }
    }

    public bool HasTarget => Match.IsGlamOnly || Target != null;
    public bool IsGlamOnly => Match.IsGlamOnly;
    public double Confidence => Match.MatchConfidence;
    public string ConfidenceText => Confidence > 0 ? $"{Confidence:P0}" : string.Empty;
    public string SourceSummary => Source.SummaryLine;

    public string TargetSummary => Match.IsGlamOnly
        ? "✨ Glam-only (armor rating set to 0)"
        : Target != null
            ? Target.SummaryLine
            : "Not mapped";

    public string CombinedSummary => $"{SourceSummary} <> {TargetSummary}";

    public void ApplyManualTarget(ArmorRecordViewModel target)
    {
        Match.IsManualMatch = true;
        Match.MatchConfidence = 1.0;
        Match.IsGlamOnly = false;
        ApplyTargetInternal(target);
    }

    public void ApplyAutoTarget(ArmorRecordViewModel target)
    {
        Match.IsManualMatch = false;
        Match.IsGlamOnly = false;
        ApplyTargetInternal(target);
    }

    public void ClearTarget()
    {
        Match.TargetArmor = null;
        Match.IsGlamOnly = false;
        Match.MatchConfidence = 0.0;
        Target = null;
    }

    public void ApplyGlamOnly()
    {
        Match.IsManualMatch = true;
        Match.IsGlamOnly = true;
        Match.MatchConfidence = 1.0;
        Match.TargetArmor = null;
        Target = null;
        RefreshState();
    }

    private void ApplyTargetInternal(ArmorRecordViewModel target)
    {
        Match.IsGlamOnly = false;
        Match.TargetArmor = target.Armor;
        Target = target;
    }

    private void RefreshState()
    {
        this.RaisePropertyChanged(nameof(TargetSummary));
        this.RaisePropertyChanged(nameof(CombinedSummary));
        this.RaisePropertyChanged(nameof(HasTarget));
        this.RaisePropertyChanged(nameof(IsGlamOnly));
    }
}