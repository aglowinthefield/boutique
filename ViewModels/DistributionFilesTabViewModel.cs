using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Boutique.ViewModels;

public class DistributionFilesTabViewModel : ReactiveObject
{
    private readonly IDistributionDiscoveryService _discoveryService;
    private readonly IArmorPreviewService _armorPreviewService;
    private readonly IMutagenService _mutagenService;
    private readonly ILogger _logger;
    private readonly SettingsViewModel _settings;

    public DistributionFilesTabViewModel(
        IDistributionDiscoveryService discoveryService,
        IArmorPreviewService armorPreviewService,
        IMutagenService mutagenService,
        SettingsViewModel settings,
        ILogger logger)
    {
        _discoveryService = discoveryService;
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _settings = settings;
        _logger = logger.ForContext<DistributionFilesTabViewModel>();

        var notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        PreviewLineCommand = ReactiveCommand.CreateFromTask<DistributionLine>(PreviewLineAsync, notLoading);

        // Update FilteredLines when SelectedFile or LineFilter changes
        this.WhenAnyValue(vm => vm.SelectedFile, vm => vm.LineFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FilteredLines)));
    }

    [Reactive] public ObservableCollection<DistributionFileViewModel> Files { get; private set; } = new();

    [Reactive] public DistributionFileViewModel? SelectedFile { get; set; }

    [Reactive] public bool IsLoading { get; private set; }

    [Reactive] public string StatusMessage { get; private set; } = "Distribution files not loaded.";

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<DistributionLine, Unit> PreviewLineCommand { get; }

    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    [Reactive] public string LineFilter { get; set; } = string.Empty;

    public IEnumerable<DistributionLine> FilteredLines
    {
        get
        {
            var lines = SelectedFile?.Lines ?? Array.Empty<DistributionLine>();

            if (string.IsNullOrWhiteSpace(LineFilter))
                return lines;

            var term = LineFilter.Trim();
            return lines.Where(line => line.RawText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string DataPath => _settings.SkyrimDataPath;

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
                Files = [];
                StatusMessage = "Set the Skyrim data path in Settings to scan distribution files.";
                return;
            }

            if (!Directory.Exists(dataPath))
            {
                Files = [];
                StatusMessage = $"Skyrim data path does not exist: {dataPath}";
                return;
            }

            StatusMessage = "Scanning for distribution files...";
            var discovered = await _discoveryService.DiscoverAsync(dataPath);
            var outfitFiles = discovered
                .Where(file => file.OutfitDistributionCount > 0)
                .ToList();

            var viewModels = outfitFiles
                .Select(file => new DistributionFileViewModel(file))
                .ToList();

            Files = new ObservableCollection<DistributionFileViewModel>(viewModels);
            LineFilter = string.Empty;
            SelectedFile = Files.FirstOrDefault();

            StatusMessage = Files.Count == 0
                ? "No outfit distributions found."
                : $"Found {Files.Count} outfit distribution file(s).";
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

    private async Task PreviewLineAsync(DistributionLine? line)
    {
        if (line == null)
            return;

        if (line.OutfitFormKeys.Count == 0)
        {
            StatusMessage = "Selected line does not reference an outfit.";
            return;
        }

        if (!_mutagenService.IsInitialized || _mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            StatusMessage = "Initialize Skyrim data path before previewing outfits.";
            return;
        }

        List<IOutfitGetter>? cachedOutfits = null;

        foreach (var keyString in line.OutfitFormKeys)
        {
            if (!OutfitResolver.TryResolve(keyString, linkCache, ref cachedOutfits, out var outfit, out var label))
                continue;

            var armorPieces = OutfitResolver.GatherArmorPieces(outfit, linkCache);
            if (armorPieces.Count == 0)
                continue;

            try
            {
                StatusMessage = $"Building preview for {label}...";
                var scene = await _armorPreviewService.BuildPreviewAsync(armorPieces, GenderedModelVariant.Female);
                await ShowPreview.Handle(scene);
                StatusMessage = $"Preview ready for {label}.";
                return;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to preview outfit {Identifier}", label);
                StatusMessage = $"Failed to preview outfit: {ex.Message}";
                return;
            }
        }

        StatusMessage = "Unable to resolve outfit for preview.";
    }
}

