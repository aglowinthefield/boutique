using System.Collections.ObjectModel;
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
    private readonly ArmorPreviewService _armorPreviewService;
    private readonly MutagenService _mutagenService;
    private readonly GameDataCacheService _cache;
    private readonly ILogger _logger;
    private readonly SettingsViewModel _settings;

    public DistributionFilesTabViewModel(
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
        GameDataCacheService cache,
        SettingsViewModel settings,
        ILogger logger)
    {
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _cache = cache;
        _settings = settings;
        _logger = logger.ForContext<DistributionFilesTabViewModel>();

        var notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        PreviewLineCommand = ReactiveCommand.CreateFromTask<DistributionLine>(PreviewLineAsync, notLoading);

        // Update FilteredLines when SelectedFile or LineFilter changes
        this.WhenAnyValue(vm => vm.SelectedFile, vm => vm.LineFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FilteredLines)));

        // Subscribe to cache loaded event to populate files
        _cache.CacheLoaded += OnCacheLoaded;

        // If cache is already loaded, populate files immediately
        if (_cache.IsLoaded)
        {
            PopulateFilesFromCache();
        }
    }

    private void OnCacheLoaded(object? sender, EventArgs e)
    {
        PopulateFilesFromCache();
    }

    private void PopulateFilesFromCache()
    {
        Files.Clear();
        foreach (var file in _cache.AllDistributionFiles)
        {
            Files.Add(file);
        }

        SelectedFile = Files.FirstOrDefault();
        this.RaisePropertyChanged(nameof(FilteredLines));
        this.RaisePropertyChanged(nameof(Files));

        StatusMessage = Files.Count == 0
            ? "No outfit distribution files found."
            : $"Found {Files.Count} outfit distribution file(s).";

        _logger.Debug("Populated {Count} distribution files from cache.", Files.Count);
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
            StatusMessage = "Refreshing distribution files...";

            // Reload the cache which will re-discover files
            await _cache.ReloadAsync();

            // Cache reload will trigger OnCacheLoaded which populates the files
            LineFilter = string.Empty;
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
