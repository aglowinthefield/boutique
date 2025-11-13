using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Boutique.Models;
using Boutique.Services;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using Serilog;

namespace Boutique.ViewModels;

public class DistributionViewModel : ReactiveObject
{
    private readonly IDistributionDiscoveryService _discoveryService;
    private readonly ILogger _logger;
    private readonly IMutagenService _mutagenService;
    private readonly IArmorPreviewService _armorPreviewService;
    private readonly SettingsViewModel _settings;

    private ObservableCollection<DistributionFileViewModel> _files = new();
    private bool _isLoading;
    private DistributionFileViewModel? _selectedFile;
    private string _statusMessage = "Distribution files not loaded.";
    private string _lineFilter = string.Empty;

    public DistributionViewModel(
        IDistributionDiscoveryService discoveryService,
        SettingsViewModel settings,
        IArmorPreviewService armorPreviewService,
        IMutagenService mutagenService,
        ILogger logger)
    {
        _discoveryService = discoveryService;
        _settings = settings;
        _armorPreviewService = armorPreviewService;
        _mutagenService = mutagenService;
        _logger = logger.ForContext<DistributionViewModel>();

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        PreviewLineCommand = ReactiveCommand.CreateFromTask<DistributionLine>(PreviewLineAsync, 
            this.WhenAnyValue(vm => vm.IsLoading).Select(loading => !loading));

        _settings.WhenAnyValue(x => x.SkyrimDataPath)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(DataPath)));
    }

    public ObservableCollection<DistributionFileViewModel> Files
    {
        get => _files;
        private set => this.RaiseAndSetIfChanged(ref _files, value);
    }

    public DistributionFileViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            var previous = _selectedFile;
            this.RaiseAndSetIfChanged(ref _selectedFile, value);
            if (!Equals(previous, value))
                this.RaisePropertyChanged(nameof(FilteredLines));
        }
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
    public ReactiveCommand<DistributionLine, Unit> PreviewLineCommand { get; }
    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    public string LineFilter
    {
        get => _lineFilter;
        set
        {
            var previous = _lineFilter;
            this.RaiseAndSetIfChanged(ref _lineFilter, value ?? string.Empty);
            if (!string.Equals(previous, _lineFilter, StringComparison.OrdinalIgnoreCase))
                this.RaisePropertyChanged(nameof(FilteredLines));
        }
    }

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
            if (!TryResolveOutfit(keyString, linkCache, ref cachedOutfits, out var outfit, out var label))
                continue;

            var armorPieces = GatherArmorPieces(outfit, linkCache);
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

    private bool TryResolveOutfit(
        string identifier,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ref List<IOutfitGetter>? cachedOutfits,
        [NotNullWhen(true)] out IOutfitGetter? outfit,
        out string label)
    {
        outfit = null;
        label = string.Empty;

        if (TryCreateFormKey(identifier, out var formKey) &&
            linkCache.TryResolve<IOutfitGetter>(formKey, out var resolvedFromFormKey))
        {
            outfit = resolvedFromFormKey;
            label = outfit.EditorID ?? formKey.ToString();
            return true;
        }

        if (TryResolveOutfitByEditorId(identifier, linkCache, ref cachedOutfits, out var resolvedFromEditorId))
        {
            outfit = resolvedFromEditorId;
            label = outfit.EditorID ?? identifier;
            return true;
        }

        return false;
    }

    private bool TryResolveOutfitByEditorId(
        string identifier,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ref List<IOutfitGetter>? cachedOutfits,
        [NotNullWhen(true)] out IOutfitGetter? outfit)
    {
        outfit = null;

        if (!TryParseEditorIdReference(identifier, out var modKey, out var editorId))
            return false;

        cachedOutfits ??= linkCache.PriorityOrder.WinningOverrides<IOutfitGetter>().ToList();

        IEnumerable<IOutfitGetter> query = cachedOutfits
            .Where(o => string.Equals(o.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

        if (modKey.HasValue)
            query = query.Where(o => o.FormKey.ModKey == modKey.Value);

        outfit = query.FirstOrDefault();
        return outfit != null;
    }

    private static bool TryParseEditorIdReference(string identifier, out ModKey? modKey, out string editorId)
    {
        modKey = null;
        editorId = string.Empty;

        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        var trimmed = identifier.Trim();
        string? modCandidate = null;
        string? editorCandidate = null;

        var pipeIndex = trimmed.IndexOf('|');
        var tildeIndex = trimmed.IndexOf('~');

        if (pipeIndex >= 0)
        {
            var firstPart = trimmed[..pipeIndex].Trim();
            var secondPart = trimmed[(pipeIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(secondPart) && TryParseModKey(secondPart, out var modFromSecond))
            {
                modKey = modFromSecond;
                editorCandidate = firstPart;
            }
            else if (!string.IsNullOrWhiteSpace(firstPart) && TryParseModKey(firstPart, out var modFromFirst))
            {
                modKey = modFromFirst;
                editorCandidate = secondPart;
            }
            else
            {
                editorCandidate = firstPart;
                modCandidate = secondPart;
            }
        }
        else if (tildeIndex >= 0)
        {
            var firstPart = trimmed[..tildeIndex].Trim();
            var secondPart = trimmed[(tildeIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(secondPart) && TryParseModKey(secondPart, out var modFromSecond))
            {
                modKey = modFromSecond;
                editorCandidate = firstPart;
            }
            else if (!string.IsNullOrWhiteSpace(firstPart) && TryParseModKey(firstPart, out var modFromFirst))
            {
                modKey = modFromFirst;
                editorCandidate = secondPart;
            }
            else
            {
                editorCandidate = firstPart;
                modCandidate = secondPart;
            }
        }
        else
        {
            editorCandidate = trimmed;
        }

        if (!modKey.HasValue && !string.IsNullOrWhiteSpace(modCandidate) && TryParseModKey(modCandidate, out var parsedMod))
            modKey = parsedMod;

        if (string.IsNullOrWhiteSpace(editorCandidate))
            return false;

        editorId = editorCandidate;
        return true;
    }

    private static List<ArmorRecordViewModel> GatherArmorPieces(
        IOutfitGetter outfit,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var pieces = new List<ArmorRecordViewModel>();

        var items = outfit.Items ?? Array.Empty<IFormLinkGetter<IOutfitTargetGetter>>();

        foreach (var itemLink in items)
        {
            if (itemLink == null)
                continue;

            var targetKeyNullable = itemLink.FormKeyNullable;
            if (!targetKeyNullable.HasValue || targetKeyNullable.Value == FormKey.Null)
                continue;

            var targetKey = targetKeyNullable.Value;

            if (!linkCache.TryResolve<IItemGetter>(targetKey, out var itemRecord))
                continue;

            if (itemRecord is not IArmorGetter armor)
                continue;

            var vm = new ArmorRecordViewModel(armor, linkCache);
            pieces.Add(vm);
        }

        return pieces;
    }

    private static bool TryCreateFormKey(string text, out FormKey formKey)
    {
        formKey = FormKey.Null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        string modPart;
        string formIdPart;

        if (trimmed.Contains('|'))
        {
            var parts = trimmed.Split('|', 2);
            modPart = parts[0].Trim();
            formIdPart = parts[1].Trim();
        }
        else if (trimmed.Contains('~'))
        {
            var parts = trimmed.Split('~', 2);
            formIdPart = parts[0].Trim();
            modPart = parts[1].Trim();
        }
        else
        {
            return false;
        }

        if (!TryParseModKey(modPart, out var modKey))
            return false;

        if (!TryParseFormId(formIdPart, out var id))
            return false;

        formKey = new FormKey(modKey, id);
        return true;
    }

    private static bool TryParseModKey(string input, out ModKey modKey)
    {
        try
        {
            modKey = ModKey.FromNameAndExtension(input);
            return true;
        }
        catch
        {
            modKey = ModKey.Null;
            return false;
        }
    }

    private static bool TryParseFormId(string text, out uint id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];

        return uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id);
    }
}
