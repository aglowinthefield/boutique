using System.Collections.ObjectModel;
using System.Windows.Input;
using ReactiveUI;
using RequiemGlamPatcher.Models;
using RequiemGlamPatcher.Services;

namespace RequiemGlamPatcher.ViewModels;

public class MainViewModel : ReactiveObject
{
    private readonly IMutagenService _mutagenService;
    private readonly IMatchingService _matchingService;
    private readonly IPatchingService _patchingService;

    private ObservableCollection<string> _availablePlugins = new();
    private ObservableCollection<ArmorRecordViewModel> _sourceArmors = new();
    private ObservableCollection<ArmorRecordViewModel> _targetArmors = new();
    private ObservableCollection<ArmorMatchViewModel> _matches = new();

    private string? _selectedSourcePlugin;
    private string? _selectedTargetPlugin;
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
        set => this.RaiseAndSetIfChanged(ref _sourceArmors, value);
    }

    public ObservableCollection<ArmorRecordViewModel> TargetArmors
    {
        get => _targetArmors;
        set => this.RaiseAndSetIfChanged(ref _targetArmors, value);
    }

    public ObservableCollection<ArmorMatchViewModel> Matches
    {
        get => _matches;
        set => this.RaiseAndSetIfChanged(ref _matches, value);
    }

    public string? SelectedSourcePlugin
    {
        get => _selectedSourcePlugin;
        set => this.RaiseAndSetIfChanged(ref _selectedSourcePlugin, value);
    }

    public string? SelectedTargetPlugin
    {
        get => _selectedTargetPlugin;
        set => this.RaiseAndSetIfChanged(ref _selectedTargetPlugin, value);
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
    public ICommand LoadSourceArmorsCommand { get; }
    public ICommand LoadTargetArmorsCommand { get; }
    public ICommand AutoMatchCommand { get; }
    public ICommand CreatePatchCommand { get; }
    public ICommand SelectAllSourceCommand { get; }
    public ICommand SelectOutfitCommand { get; }

    public MainViewModel(
        IMutagenService mutagenService,
        IMatchingService matchingService,
        IPatchingService patchingService,
        SettingsViewModel settingsViewModel)
    {
        _mutagenService = mutagenService;
        _matchingService = matchingService;
        _patchingService = patchingService;
        Settings = settingsViewModel;

        InitializeCommand = ReactiveCommand.CreateFromTask(InitializeAsync);
        LoadSourceArmorsCommand = ReactiveCommand.CreateFromTask(LoadSourceArmorsAsync,
            this.WhenAnyValue(x => x.SelectedSourcePlugin, x => !string.IsNullOrEmpty(x)));
        LoadTargetArmorsCommand = ReactiveCommand.CreateFromTask(LoadTargetArmorsAsync,
            this.WhenAnyValue(x => x.SelectedTargetPlugin, x => !string.IsNullOrEmpty(x)));
        AutoMatchCommand = ReactiveCommand.CreateFromTask(AutoMatchAsync,
            this.WhenAnyValue(
                x => x.SourceArmors.Count,
                x => x.TargetArmors.Count,
                (source, target) => source > 0 && target > 0));
        CreatePatchCommand = ReactiveCommand.CreateFromTask(CreatePatchAsync,
            this.WhenAnyValue(x => x.Matches.Count, count => count > 0));
        SelectAllSourceCommand = ReactiveCommand.Create(SelectAllSource);
        SelectOutfitCommand = ReactiveCommand.Create(SelectOutfit);
    }

    private async Task InitializeAsync()
    {
        IsLoading = true;
        StatusMessage = "Initializing Mutagen...";

        try
        {
            await _mutagenService.InitializeAsync(Settings.SkyrimDataPath);

            var plugins = await _mutagenService.GetAvailablePluginsAsync();
            AvailablePlugins = new ObservableCollection<string>(plugins);

            StatusMessage = $"Loaded {AvailablePlugins.Count} plugins";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSourceArmorsAsync()
    {
        if (string.IsNullOrEmpty(SelectedSourcePlugin))
            return;

        IsLoading = true;
        StatusMessage = $"Loading armors from {SelectedSourcePlugin}...";

        try
        {
            var armors = await _mutagenService.LoadArmorsFromPluginAsync(SelectedSourcePlugin);
            SourceArmors = new ObservableCollection<ArmorRecordViewModel>(
                armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));

            StatusMessage = $"Loaded {SourceArmors.Count} armors from {SelectedSourcePlugin}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading source armors: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadTargetArmorsAsync()
    {
        if (string.IsNullOrEmpty(SelectedTargetPlugin))
            return;

        IsLoading = true;
        StatusMessage = $"Loading armors from {SelectedTargetPlugin}...";

        try
        {
            var armors = await _mutagenService.LoadArmorsFromPluginAsync(SelectedTargetPlugin);
            TargetArmors = new ObservableCollection<ArmorRecordViewModel>(
                armors.Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache)));

            StatusMessage = $"Loaded {TargetArmors.Count} armors from {SelectedTargetPlugin}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading target armors: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AutoMatchAsync()
    {
        await Task.Run(() =>
        {
            StatusMessage = "Auto-matching armors...";

            var sourceList = SourceArmors.Select(vm => vm.Armor).ToList();
            var targetList = TargetArmors.Select(vm => vm.Armor).ToList();

            var matchResults = _matchingService.AutoMatchArmors(sourceList, targetList, AutoMatchThreshold);

            Matches = new ObservableCollection<ArmorMatchViewModel>(
                matchResults.Select(m => new ArmorMatchViewModel(m, TargetArmors.ToList(), _mutagenService.LinkCache)));

            var matchedCount = Matches.Count(m => m.Match.TargetArmor != null);
            StatusMessage = $"Auto-matched {matchedCount}/{Matches.Count} armors";
        });
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

            var matchesToPatch = Matches.Select(m => m.Match).ToList();

            var (success, message) = await _patchingService.CreatePatchAsync(
                matchesToPatch,
                Settings.FullOutputPath,
                progress);

            StatusMessage = message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error creating patch: {ex.Message}";
        }
        finally
        {
            IsPatching = false;
        }
    }

    private void SelectAllSource()
    {
        foreach (var armor in SourceArmors)
        {
            armor.IsSelected = true;
        }
    }

    private void SelectOutfit()
    {
        var selectedArmors = SourceArmors.Where(a => a.IsSelected).Select(a => a.Armor).ToList();

        if (!selectedArmors.Any())
        {
            StatusMessage = "Please select at least one armor to group as outfit";
            return;
        }

        // Group by outfit and select all in the same outfit
        var groups = _matchingService.GroupByOutfit(selectedArmors);

        foreach (var group in groups)
        {
            foreach (var armor in group)
            {
                var vm = SourceArmors.FirstOrDefault(a => a.Armor.FormKey == armor.FormKey);
                if (vm != null)
                    vm.IsSelected = true;
            }
        }

        StatusMessage = $"Selected outfit group";
    }
}

public class ArmorMatchViewModel : ReactiveObject
{
    private readonly List<ArmorRecordViewModel> _availableTargets;
    private ArmorRecordViewModel? _selectedTarget;

    public ArmorMatch Match { get; }
    public ArmorRecordViewModel Source { get; }

    public ArmorRecordViewModel? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTarget, value);
            Match.TargetArmor = value?.Armor;
            Match.IsManualMatch = true;
        }
    }

    public List<ArmorRecordViewModel> AvailableTargets => _availableTargets;
    public double Confidence => Match.MatchConfidence;
    public string ConfidenceText => $"{Confidence:P0}";

    public ArmorMatchViewModel(ArmorMatch match, List<ArmorRecordViewModel> availableTargets, Mutagen.Bethesda.Plugins.Cache.ILinkCache? linkCache)
    {
        Match = match;
        _availableTargets = availableTargets;
        Source = new ArmorRecordViewModel(match.SourceArmor, linkCache);

        if (match.TargetArmor != null)
        {
            _selectedTarget = availableTargets.FirstOrDefault(t => t.Armor.FormKey == match.TargetArmor.FormKey);
        }
    }
}
