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
    private readonly DistributionDiscoveryService _discoveryService;
    private readonly ArmorPreviewService _armorPreviewService;
    private readonly MutagenService _mutagenService;
    private readonly ILogger _logger;
    private readonly SettingsViewModel _settings;

    public DistributionFilesTabViewModel(
        DistributionDiscoveryService discoveryService,
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
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

    [Reactive] public ObservableCollection<DistributionFileViewModel> Files { get; private set; } = [];

    private DistributionFileViewModel? _selectedFile;

    public DistributionFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (Equals(_selectedFile, value))
                return;

            _logger.Debug("SelectedFile changed from {Old} to {New} (Lines: {LineCount})",
                _selectedFile?.FileName ?? "null",
                value?.FileName ?? "null",
                value?.Lines.Count ?? 0);

            this.RaiseAndSetIfChanged(ref _selectedFile, value);
            // Explicitly notify FilteredLines when SelectedFile changes
            this.RaisePropertyChanged(nameof(FilteredLines));
        }
    }

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
                Files.Clear();
                StatusMessage = "Set the Skyrim data path in Settings to scan distribution files.";
                return;
            }

            if (!Directory.Exists(dataPath))
            {
                Files.Clear();
                StatusMessage = $"Skyrim data path does not exist: {dataPath}";
                return;
            }

            StatusMessage = "Scanning for distribution files...";
            _logger.Debug("Starting distribution file discovery in {DataPath}", dataPath);

            var discovered = await _discoveryService.DiscoverAsync(dataPath);
            _logger.Debug("Discovery service returned {Count} files", discovered.Count);

            var outfitFiles = discovered
                .Where(file => file.OutfitDistributionCount > 0)
                .ToList();

            _logger.Debug("Filtered to {Count} files with outfit distributions", outfitFiles.Count);

            var viewModels = outfitFiles
                .Select(file => new DistributionFileViewModel(file))
                .ToList();

            // Update collection in place to maintain bindings
            Files.Clear();
            foreach (var vm in viewModels)
            {
                Files.Add(vm);
            }

            LineFilter = string.Empty;
            var newSelectedFile = Files.FirstOrDefault();
            SelectedFile = newSelectedFile;

            // Explicitly notify that FilteredLines may have changed (in case SelectedFile didn't change reference)
            this.RaisePropertyChanged(nameof(FilteredLines));

            // Explicitly notify that Files collection changed (for WhenAnyValue subscribers)
            this.RaisePropertyChanged(nameof(Files));

            if (Files.Count == 0)
            {
                StatusMessage = discovered.Count == 0
                    ? "No distribution files found. Check that your data path is correct and contains *_DISTR.ini files or SkyPatcher INI files."
                    : $"Found {discovered.Count} distribution file(s), but none contain outfit distributions.";
                _logger.Warning("No outfit distribution files found. Discovered {TotalCount} files total, but {OutfitCount} had outfit distributions",
                    discovered.Count, outfitFiles.Count);
            }
            else
            {
                StatusMessage = $"Found {Files.Count} outfit distribution file(s).";
                _logger.Information("Successfully loaded {Count} outfit distribution file(s)", Files.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh distribution files.");
            Files.Clear();
            SelectedFile = null;
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
