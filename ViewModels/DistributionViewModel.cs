using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using RequiemGlamPatcher.Services;
using Serilog;

namespace RequiemGlamPatcher.ViewModels;

public class DistributionViewModel : ReactiveObject
{
    private readonly IDistributionDiscoveryService _discoveryService;
    private readonly SettingsViewModel _settings;
    private readonly ILogger _logger;

    private ObservableCollection<DistributionFileViewModel> _files = new();
    private DistributionFileViewModel? _selectedFile;
    private bool _isLoading;
    private string _statusMessage = "Distribution files not loaded.";

    public ObservableCollection<DistributionFileViewModel> Files
    {
        get => _files;
        private set => this.RaiseAndSetIfChanged(ref _files, value);
    }

    public DistributionFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set => this.RaiseAndSetIfChanged(ref _selectedFile, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public string DataPath => _settings.SkyrimDataPath;

    public DistributionViewModel(
        IDistributionDiscoveryService discoveryService,
        SettingsViewModel settings,
        ILogger logger)
    {
        _discoveryService = discoveryService;
        _settings = settings;
        _logger = logger.ForContext<DistributionViewModel>();

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);

        _settings.WhenAnyValue(x => x.SkyrimDataPath)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(DataPath)));
    }

    public async Task RefreshAsync()
    {
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;
            var dataPath = _settings.SkyrimDataPath;

            if (string.IsNullOrWhiteSpace(dataPath))
            {
                Files = new ObservableCollection<DistributionFileViewModel>();
                StatusMessage = "Set the Skyrim data path in Settings to scan distribution files.";
                return;
            }

            if (!Directory.Exists(dataPath))
            {
                Files = new ObservableCollection<DistributionFileViewModel>();
                StatusMessage = $"Skyrim data path does not exist: {dataPath}";
                return;
            }

            StatusMessage = "Scanning for distribution files...";
            var discovered = await _discoveryService.DiscoverAsync(dataPath);
            var viewModels = discovered
                .Select(file => new DistributionFileViewModel(file))
                .ToList();

            Files = new ObservableCollection<DistributionFileViewModel>(viewModels);
            SelectedFile = Files.FirstOrDefault();

            StatusMessage = Files.Count == 0
                ? "No distribution files found."
                : $"Found {Files.Count} distribution file(s).";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh distribution files.");
            StatusMessage = $"Error loading distribution files: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
