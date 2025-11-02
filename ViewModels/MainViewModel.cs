using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Windows.Data;
using System.Windows.Input;
using ReactiveUI;
using RequiemGlamPatcher.Models;
using RequiemGlamPatcher.Services;
using Serilog;
using System.Reactive.Threading.Tasks;

namespace RequiemGlamPatcher.ViewModels;

public class MainViewModel : ReactiveObject
{
    private readonly IMutagenService _mutagenService;
    private readonly IMatchingService _matchingService;
    private readonly IPatchingService _patchingService;
    private readonly ILogger _logger;

    public Interaction<string, Unit> PatchCreatedNotification { get; } = new();

    private ObservableCollection<string> _availablePlugins = new();
    private ObservableCollection<ArmorRecordViewModel> _sourceArmors = new();
    private ObservableCollection<ArmorRecordViewModel> _targetArmors = new();
    private ObservableCollection<ArmorMatchViewModel> _matches = new();
    private ICollectionView? _sourceArmorsView;
    private ICollectionView? _targetArmorsView;
    private string _sourceSearchText = string.Empty;
    private string _targetSearchText = string.Empty;
    private IList _selectedSourceArmors = new List<ArmorRecordViewModel>();
    private ArmorRecordViewModel? _selectedTargetArmor;

    private string? _selectedSourcePlugin;
    private string? _selectedTargetPlugin;
    private int _activeLoadingOperations;
    private bool _isLoading;
    private bool _isPatching;
    private string _statusMessage = "Ready";
    private int _progressCurrent;
    private int _progressTotal;
    private double _autoMatchThreshold = 0.6;

    public SettingsViewModel Settings { get; }

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

    public IList SelectedSourceArmors
    {
        get => _selectedSourceArmors;
        set
        {
            if (value == _selectedSourceArmors)
                return;

            _selectedSourceArmors = value ?? Array.Empty<object>();
            this.RaisePropertyChanged(nameof(SelectedSourceArmors));

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
            {
                SelectedTargetArmor = _targetArmors.FirstOrDefault(t => t.Armor.FormKey == existing.Target.Armor.FormKey);
            }
            else
            {
                SelectedTargetArmor = _targetArmors.FirstOrDefault(t => primary.SharesSlotWith(t));
            }
        }
    }

    private ArmorRecordViewModel? SelectedSourceArmor => _selectedSourceArmors.OfType<ArmorRecordViewModel>().FirstOrDefault();

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
            this.RaisePropertyChanged(nameof(SourceArmorsView));
        }
    }

    public ICollectionView? TargetArmorsView
    {
        get => _targetArmorsView;
        private set
        {
            _targetArmorsView = value;
            this.RaisePropertyChanged(nameof(TargetArmorsView));
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

            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

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

            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

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
        set => this.RaiseAndSetIfChanged(ref _isPatching, value);
    }

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
    public ReactiveCommand<ArmorMatchViewModel, Unit> RemoveMappingCommand { get; }

    public MainViewModel(
        IMutagenService mutagenService,
        IMatchingService matchingService,
        IPatchingService patchingService,
        SettingsViewModel settingsViewModel,
        ILoggingService loggingService)
    {
        _mutagenService = mutagenService;
        _matchingService = matchingService;
        _patchingService = patchingService;
        Settings = settingsViewModel;
        _logger = loggingService.ForContext<MainViewModel>();

        ConfigureSourceArmorsView();
        ConfigureTargetArmorsView();

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
        RemoveMappingCommand = ReactiveCommand.Create<ArmorMatchViewModel>(RemoveMapping);
    }

    private void ConfigureSourceArmorsView()
    {
        SourceArmorsView = CollectionViewSource.GetDefaultView(_sourceArmors);
        if (SourceArmorsView != null)
        {
            SourceArmorsView.Filter = SourceArmorsFilter;
        }
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

    private void UpdateTargetSlotCompatibility()
    {
        if (_targetArmors == null)
            return;

        var sources = SelectedSourceArmors.OfType<ArmorRecordViewModel>().ToList();

        if (!sources.Any())
        {
            foreach (var target in _targetArmors)
            {
                target.IsSlotCompatible = true;
            }
            return;
        }

        foreach (var target in _targetArmors)
        {
            target.IsSlotCompatible = sources.All(source => source.SharesSlotWith(target));
        }

        TargetArmorsView?.Refresh();
    }

    private void MapSelected()
    {
        var sources = SelectedSourceArmors.OfType<ArmorRecordViewModel>().ToList();
        var target = SelectedTargetArmor;

        if (!sources.Any() || target == null)
        {
            _logger.Debug("MapSelected invoked without valid selections. SourceCount={SourceCount}, HasTarget={HasTarget}", sources.Count, target != null);
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
            _logger.Information("Mapped {SourceCount} armor(s) to target {TargetName} ({TargetFormKey})", sources.Count, target.DisplayName, target.Armor.FormKey);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to map {SourceCount} armor(s) to {TargetName}", sources.Count, target.DisplayName);
            StatusMessage = $"Error mapping armors: {ex.Message}";
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
        if (_activeLoadingOperations > 0)
        {
            _activeLoadingOperations--;
        }

        IsLoading = _activeLoadingOperations > 0;
    }

    private bool ClearMappingsInternal()
    {
        if (Matches.Count == 0)
            return false;

        foreach (var mapping in Matches.ToList())
        {
            mapping.Source.IsMapped = false;
        }

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
            _logger.Information("Removed mapping for source {SourceName} ({SourceFormKey})", mapping.Source.DisplayName, mapping.Source.Armor.FormKey);
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
            _logger.Information("Loaded {PluginCount} plugins from {DataPath}", AvailablePlugins.Count, Settings.SkyrimDataPath);
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
                if (match.TargetArmor != null && targetLookup.TryGetValue(match.TargetArmor.FormKey, out var foundTarget))
                {
                    targetVm = foundTarget;
                }

                mappingViewModels.Add(new ArmorMatchViewModel(match, sourceVm, targetVm));
            }

            Matches = mappingViewModels;
            var matchedCount = Matches.Count(m => m.HasTarget);
            StatusMessage = $"Auto-matched {matchedCount}/{Matches.Count} armors";
            _logger.Information("Auto-match completed with {MatchedCount} mapped armors out of {TotalMatches}", matchedCount, Matches.Count);

            foreach (var mapping in Matches)
            {
                mapping.Source.IsMapped = mapping.HasTarget;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during auto-match: {ex.Message}";
            _logger.Error(ex, "Auto-match failed.");
        }
    }

    public void ApplyTargetSort(string? propertyName = nameof(ArmorRecordViewModel.DisplayName), ListSortDirection direction = ListSortDirection.Ascending)
    {
        if (TargetArmorsView is ListCollectionView view)
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(ArmorRecordViewModel.SlotCompatibilityPriority), ListSortDirection.Ascending));

            if (!string.IsNullOrEmpty(propertyName))
            {
                view.SortDescriptions.Add(new SortDescription(propertyName, direction));
            }

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
                .Where(m => m.Match.TargetArmor != null)
                .Select(m => m.Match)
                .ToList();

            if (matchesToPatch.Count == 0)
            {
                StatusMessage = "No mapped armors to patch.";
                _logger.Warning("Patch creation aborted — no mapped armors available.");
                return;
            }

            _logger.Information("Starting patch creation for {MatchCount} matches to {OutputPath}", matchesToPatch.Count, Settings.FullOutputPath);

            var (success, message) = await _patchingService.CreatePatchAsync(
                matchesToPatch,
                Settings.FullOutputPath,
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

    public ArmorMatch Match { get; }
    public ArmorRecordViewModel Source { get; }

    public ArmorRecordViewModel? Target
    {
        get => _target;
        private set
        {
            this.RaiseAndSetIfChanged(ref _target, value);
            this.RaisePropertyChanged(nameof(TargetSummary));
            this.RaisePropertyChanged(nameof(CombinedSummary));
            this.RaisePropertyChanged(nameof(HasTarget));
        }
    }

    public bool HasTarget => Target != null;
    public double Confidence => Match.MatchConfidence;
    public string ConfidenceText => Confidence > 0 ? $"{Confidence:P0}" : string.Empty;
    public string SourceSummary => Source.SummaryLine;
    public string TargetSummary => Target != null ? Target.SummaryLine : "Not mapped";
    public string CombinedSummary => $"{SourceSummary} <> {TargetSummary}";

    public ArmorMatchViewModel(
        ArmorMatch match,
        ArmorRecordViewModel source,
        ArmorRecordViewModel? target)
    {
        Match = match;
        Source = source;

        if (target != null)
        {
            ApplyAutoTarget(target);
        }
    }

    public void ApplyManualTarget(ArmorRecordViewModel target)
    {
        Match.IsManualMatch = true;
        Match.MatchConfidence = 1.0;
        ApplyTargetInternal(target);
    }

    public void ApplyAutoTarget(ArmorRecordViewModel target)
    {
        Match.IsManualMatch = false;
        ApplyTargetInternal(target);
    }

    public void ClearTarget()
    {
        Match.TargetArmor = null;
        Target = null;
    }

    private void ApplyTargetInternal(ArmorRecordViewModel target)
    {
        Match.TargetArmor = target.Armor;
        Target = target;
    }
}
