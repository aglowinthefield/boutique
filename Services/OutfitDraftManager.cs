using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using DynamicData;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using Serilog;

namespace Boutique.Services;

public class OutfitDraftManager : ReactiveObject, IDisposable
{
    private static readonly BipedObjectFlag[] BipedFlags = Enum.GetValues<BipedObjectFlag>()
        .Where(f => f != 0 && ((uint)f & ((uint)f - 1)) == 0)
        .ToArray();

    private readonly SourceList<OutfitDraftViewModel> _draftsSource = new();
    private readonly ObservableCollection<ExistingOutfitViewModel> _existingOutfits = [];
    private readonly List<string> _pendingDeletions = [];
    private readonly ILogger _logger;
    private readonly CompositeDisposable _disposables = new();

    private bool _suppressAutoSave;

    public OutfitDraftManager(ILoggingService loggingService)
    {
        _logger = loggingService.ForContext<OutfitDraftManager>();

        _disposables.Add(_draftsSource.Connect()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out var drafts)
            .Subscribe());
        Drafts = drafts;

        _disposables.Add(_draftsSource.Connect()
            .Select(_ => _draftsSource.Count > 0)
            .Subscribe(hasDrafts => HasDrafts = hasDrafts));

        ExistingOutfits = new ReadOnlyObservableCollection<ExistingOutfitViewModel>(_existingOutfits);
        _existingOutfits.CollectionChanged += (_, _) =>
            HasExistingOutfits = _existingOutfits.Count > 0;
    }

    public ReadOnlyObservableCollection<OutfitDraftViewModel> Drafts { get; }
    public ReadOnlyObservableCollection<ExistingOutfitViewModel> ExistingOutfits { get; }
    public IReadOnlyList<string> PendingDeletions => _pendingDeletions;

    public bool HasDrafts
    {
        get => field;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasPendingDeletions
    {
        get => field;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool HasExistingOutfits
    {
        get => field;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public event Action<string>? StatusChanged;
    public event Action? DraftModified;

    public Func<(string Prompt, string DefaultValue), Task<string?>>? RequestNameAsync { get; set; }
    public Func<OutfitDraftViewModel, Task>? PreviewDraftAsync { get; set; }

    public bool SuppressAutoSave
    {
        get => _suppressAutoSave;
        set => _suppressAutoSave = value;
    }

    public async Task<OutfitDraftViewModel?> CreateDraftAsync(
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
            RaiseStatus("Select at least one armor to create an outfit.");
            _logger.Debug("CreateDraftAsync invoked without any valid pieces.");
            return null;
        }

        if (!isValid)
        {
            RaiseStatus(validationMessage);
            _logger.Warning("Outfit creation blocked due to slot conflict: {Message}", validationMessage);
            return null;
        }

        string outfitName;
        if (!string.IsNullOrWhiteSpace(defaultName))
        {
            outfitName = defaultName;
        }
        else
        {
            if (RequestNameAsync == null)
            {
                _logger.Warning("RequestNameAsync not configured, cannot prompt for outfit name.");
                return null;
            }

            const string namePrompt = "Enter the outfit name (also used as the EditorID):";
            var result = await RequestNameAsync((namePrompt, ""));

            if (string.IsNullOrWhiteSpace(result))
            {
                RaiseStatus("Outfit creation canceled.");
                _logger.Information("Outfit creation canceled by user.");
                return null;
            }

            outfitName = result;
        }

        var trimmedName = outfitName.Trim();
        var sanitizedName = SanitizeOutfitName(trimmedName);
        sanitizedName = EnsureUniqueOutfitName(sanitizedName, null);

        if (!string.Equals(trimmedName, sanitizedName, StringComparison.Ordinal))
        {
            _logger.Debug("Outfit name sanitized from {Original} to {Sanitized}", trimmedName, sanitizedName);
        }

        var draft = CreateDraftViewModel(sanitizedName, distinctPieces);
        _draftsSource.Add(draft);

        RaiseStatus($"Added outfit '{draft.Name}' with {distinctPieces.Count} piece(s).");
        _logger.Information("Added outfit {EditorId} with {PieceCount} pieces.", draft.EditorId, distinctPieces.Count);

        return draft;
    }

    public OutfitDraftViewModel? CreateOverrideDraft(
        IOutfitGetter outfit,
        IReadOnlyList<ArmorRecordViewModel> armorPieces,
        ModKey? winningMod)
    {
        var editorId = outfit.EditorID ?? outfit.FormKey.ToString();

        var existingDraft = _draftsSource.Items.FirstOrDefault(d =>
            d.FormKey == outfit.FormKey ||
            string.Equals(d.EditorId, editorId, StringComparison.OrdinalIgnoreCase));

        if (existingDraft != null)
        {
            RaiseStatus($"Override for {editorId} already exists.");
            return null;
        }

        var draft = CreateDraftViewModel(editorId, armorPieces, outfit.FormKey, true, winningMod);
        _draftsSource.Add(draft);

        RaiseStatus($"Added override for '{editorId}' with {armorPieces.Count} piece(s).");
        _logger.Information(
            "Added override for {EditorId} ({FormKey}) with {PieceCount} pieces.",
            editorId,
            outfit.FormKey,
            armorPieces.Count);

        return draft;
    }

    public async Task<OutfitDraftViewModel?> DuplicateDraftAsync(OutfitDraftViewModel draft)
    {
        var pieces = draft.GetPieces();
        if (pieces.Count == 0)
        {
            RaiseStatus($"Outfit '{draft.EditorId}' has no pieces to duplicate.");
            return null;
        }

        if (RequestNameAsync == null)
        {
            _logger.Warning("RequestNameAsync not configured, cannot prompt for outfit name.");
            return null;
        }

        const string namePrompt = "Enter a name for the duplicated outfit:";
        var defaultName = draft.Name + "_copy";
        var newName = await RequestNameAsync((namePrompt, defaultName));

        if (string.IsNullOrWhiteSpace(newName))
        {
            RaiseStatus("Duplicate canceled.");
            return null;
        }

        var sanitizedName = SanitizeOutfitName(newName.Trim());
        sanitizedName = EnsureUniqueOutfitName(sanitizedName, null);

        var newDraft = CreateDraftViewModel(sanitizedName, pieces);
        _draftsSource.Add(newDraft);

        RaiseStatus($"Duplicated outfit as '{sanitizedName}' with {pieces.Count} piece(s).");
        _logger.Information("Duplicated outfit draft {OriginalEditorId} to {NewEditorId}", draft.EditorId, sanitizedName);

        return newDraft;
    }

    public void RemoveDraft(OutfitDraftViewModel draft)
    {
        draft.PropertyChanged -= OnDraftPropertyChanged;

        if (!_draftsSource.Remove(draft))
        {
            return;
        }

        if (draft.FormKey.HasValue)
        {
            _pendingDeletions.Add(draft.EditorId);
            HasPendingDeletions = true;
            RaiseStatus($"Removed outfit '{draft.EditorId}'. Will be deleted from patch on save.");
            _logger.Information("Marked outfit {EditorId} for deletion.", draft.EditorId);
        }
        else
        {
            RaiseStatus($"Removed outfit '{draft.EditorId}'.");
            _logger.Information("Removed outfit draft {EditorId}.", draft.EditorId);
        }

        RaiseDraftModified();
    }

    public void RemovePiece(OutfitDraftViewModel draft, ArmorRecordViewModel piece)
    {
        draft.RemovePiece(piece);
        RaiseStatus($"Removed {piece.DisplayName} from outfit '{draft.EditorId}'.");
        _logger.Information("Removed armor {Armor} from outfit draft {EditorId}.", piece.DisplayName, draft.EditorId);
    }

    public bool TryAddPieces(OutfitDraftViewModel draft, IReadOnlyList<ArmorRecordViewModel> pieces)
    {
        var distinctPieces = DistinctArmorPieces(pieces);

        if (distinctPieces.Count == 0)
        {
            RaiseStatus($"No new armor pieces to add to outfit '{draft.EditorId}'.");
            _logger.Debug("TryAddPieces invoked with no valid pieces for outfit {EditorId}.", draft.EditorId);
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
                RaiseStatus($"Slot conflict: {piece.DisplayName} overlaps {existingConflict.DisplayName} ({slot}).");
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
                RaiseStatus($"Slot conflict: {piece.DisplayName} overlaps {stagedConflict.DisplayName} ({slot}).");
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
            RaiseStatus($"No new armor added to outfit '{draft.EditorId}'.");
            _logger.Information("Drop onto outfit {EditorId} contained only duplicate pieces.", draft.EditorId);
            return false;
        }

        RaiseStatus($"Added {added.Count} piece(s) to outfit '{draft.EditorId}'.");
        _logger.Information(
            "Added {AddedCount} armor(s) to outfit draft {EditorId}. Added: {AddedPieces}.",
            added.Count,
            draft.EditorId,
            string.Join(", ", added.Select(a => a.DisplayName)));

        return true;
    }

    public async Task<int> LoadExistingOutfitsAsync(
        string plugin,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache,
        Func<string, Task<IEnumerable<IOutfitGetter>>> loadOutfitsFunc,
        Func<string, bool> isStillSelectedFunc)
    {
        _existingOutfits.Clear();

        if (linkCache is null)
        {
            _logger.Warning("Link cache unavailable; skipping outfit discovery for {Plugin}.", plugin);
            return 0;
        }

        var outfits = (await loadOutfitsFunc(plugin)).ToList();

        if (!isStillSelectedFunc(plugin))
        {
            return 0;
        }

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

            var existing = new ExistingOutfitViewModel(editorId, editorId, outfit.FormKey, distinctPieces);
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

    public int CopyExistingOutfits(ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache)
    {
        if (_existingOutfits.Count == 0)
        {
            RaiseStatus("No existing outfits to copy.");
            _logger.Debug("CopyExistingOutfits invoked with no discovered outfits.");
            return 0;
        }

        if (linkCache is null)
        {
            RaiseStatus("Initialize Skyrim data path before copying outfits.");
            _logger.Warning("CopyExistingOutfits attempted without an active link cache.");
            return 0;
        }

        var copied = 0;

        foreach (var existing in _existingOutfits.ToList())
        {
            if (Drafts.Any(d => d.FormKey.HasValue && d.FormKey.Value == existing.FormKey))
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

            var draft = CreateDraftViewModel(uniqueName, pieces, existing.FormKey);
            _draftsSource.Add(draft);
            copied++;
        }

        _existingOutfits.Clear();

        if (copied > 0)
        {
            RaiseStatus($"Copied {copied} existing outfit(s).");
            _logger.Information("Copied {CopiedCount} existing outfit(s).", copied);
        }
        else
        {
            RaiseStatus("Existing outfits already exist or could not be copied.");
            _logger.Information("No existing outfits were copied; they may already exist or lacked valid pieces.");
        }

        return copied;
    }

    public void ClearExistingOutfits() => _existingOutfits.Clear();

    public void ClearDraftsFromOtherPlugins(ModKey targetModKey)
    {
        var draftsFromOtherPlugins = _draftsSource.Items
            .Where(d => d.FormKey.HasValue && d.FormKey.Value.ModKey != targetModKey && !d.IsOverride)
            .ToList();

        if (draftsFromOtherPlugins.Count > 0)
        {
            _logger.Information("Clearing {Count} draft(s) from previous output plugin(s).", draftsFromOtherPlugins.Count);
            foreach (var draft in draftsFromOtherPlugins)
            {
                draft.PropertyChanged -= OnDraftPropertyChanged;
                _draftsSource.Remove(draft);
            }
        }
    }

    public void AddDraftsFromOutfits(
        IEnumerable<IOutfitGetter> outfits,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ModKey targetModKey,
        Func<FormKey, ModKey?, ModKey?> getWinningMod)
    {
        var existingDraftKeys = _draftsSource.Items
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

                var result = OutfitResolver.GatherArmorPieces(outfit, linkCache);
                if (result.ArmorPieces.Count == 0)
                {
                    return null;
                }

                var editorId = outfit.EditorID ?? SanitizeOutfitName(outfit.FormKey.ToString());

                if (!ValidateOutfitPieces(result.ArmorPieces, out var validationMessage))
                {
                    _logger.Warning("Skipping outfit {EditorId} due to slot conflict: {Message}", editorId, validationMessage);
                    return null;
                }

                var isOverride = outfit.FormKey.ModKey != targetModKey;
                var overrideSourceMod = isOverride ? getWinningMod(outfit.FormKey, targetModKey) : null;

                return new
                {
                    EditorId = editorId,
                    Pieces = result.ArmorPieces,
                    FormKey = outfit.FormKey,
                    IsOverride = isOverride,
                    OverrideSourceMod = overrideSourceMod
                };
            })
            .Where(d => d != null)
            .ToList();

        var draftViewModels = new List<OutfitDraftViewModel>();
        foreach (var d in drafts)
        {
            if (d == null)
            {
                continue;
            }

            var draft = CreateDraftViewModel(d.EditorId, d.Pieces, d.FormKey, d.IsOverride, d.OverrideSourceMod);
            draftViewModels.Add(draft);

            _logger.Debug("Loaded outfit {EditorId} from output plugin.", d.EditorId);
        }

        if (draftViewModels.Count > 0)
        {
            _draftsSource.AddRange(draftViewModels);
        }
    }

    public List<OutfitCreationRequest> BuildSaveRequests()
    {
        var populatedDrafts = _draftsSource.Items.Where(d => d.HasPieces).ToList();
        var deletionsToProcess = _pendingDeletions.ToList();

        var requests = populatedDrafts
            .ConvertAll(d => new OutfitCreationRequest(
                d.Name,
                d.EditorId,
                [.. d.GetPieces().Select(p => p.Armor)],
                d.FormKey,
                d.IsOverride,
                d.OverrideSourceMod));

        requests.AddRange(deletionsToProcess.Select(editorId => new OutfitCreationRequest(editorId, editorId, [])));

        return requests;
    }

    public void ProcessSaveResults(IReadOnlyList<OutfitCreationResult> results)
    {
        foreach (var editorId in _pendingDeletions.ToList())
        {
            _pendingDeletions.Remove(editorId);
        }

        HasPendingDeletions = _pendingDeletions.Count > 0;

        foreach (var result in results)
        {
            var draft = _draftsSource.Items.FirstOrDefault(d =>
                string.Equals(d.EditorId, result.EditorId, StringComparison.OrdinalIgnoreCase));

            if (draft != null && !draft.FormKey.HasValue)
            {
                draft.FormKey = result.FormKey;
            }
        }
    }

    public bool HasUnsavedChanges() =>
        _draftsSource.Items.Any(d => d.HasPieces) || _pendingDeletions.Count > 0;

    private OutfitDraftViewModel CreateDraftViewModel(
        string name,
        IReadOnlyList<ArmorRecordViewModel> pieces,
        FormKey? formKey = null,
        bool isOverride = false,
        ModKey? overrideSourceMod = null)
    {
        var draft = new OutfitDraftViewModel(
            name,
            name,
            pieces,
            RemoveDraft,
            RemovePiece,
            d => PreviewDraftAsync?.Invoke(d) ?? Task.CompletedTask,
            d => DuplicateDraftAsync(d))
        {
            FormKey = formKey,
            IsOverride = isOverride,
            OverrideSourceMod = overrideSourceMod
        };

        draft.PropertyChanged += OnDraftPropertyChanged;
        return draft;
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not OutfitDraftViewModel draft)
        {
            return;
        }

        if (e.PropertyName == nameof(OutfitDraftViewModel.Name))
        {
            HandleDraftRename(draft);
        }

        RaiseDraftModified();
    }

    private void HandleDraftRename(OutfitDraftViewModel draft)
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

        RaiseStatus($"Renamed outfit to '{draft.Name}'.");
        _logger.Information("Renamed outfit draft to {Name}", draft.Name);
    }

    private string EnsureUniqueOutfitName(string baseName, OutfitDraftViewModel? exclude)
    {
        var sanitizedBase = string.IsNullOrEmpty(baseName) ? "Outfit" : baseName;
        var candidate = sanitizedBase;
        var suffixIndex = 0;

        while (_draftsSource.Items.Any(o =>
                   !ReferenceEquals(o, exclude) &&
                   string.Equals(o.EditorId, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            suffixIndex++;
            candidate = sanitizedBase + AlphabetSuffix(suffixIndex);
        }

        return candidate;
    }

    private void RaiseStatus(string message) => StatusChanged?.Invoke(message);

    private void RaiseDraftModified()
    {
        if (!_suppressAutoSave)
        {
            DraftModified?.Invoke();
        }
    }

    public static bool ValidateOutfitPieces(IReadOnlyList<ArmorRecordViewModel> pieces, out string validationMessage)
    {
        var slotsInUse = new Dictionary<BipedObjectFlag, ArmorRecordViewModel>();

        foreach (var piece in pieces)
        {
            var mask = piece.SlotMask;
            if (mask == 0)
            {
                continue;
            }

            foreach (var flag in BipedFlags)
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

    public static string SanitizeOutfitName(string? value) =>
        InputPatterns.Identifier.SanitizeOrDefault(value, "Outfit");

    private static List<ArmorRecordViewModel> DistinctArmorPieces(IEnumerable<ArmorRecordViewModel> pieces) =>
        pieces.GroupBy(p => p.Armor.FormKey).Select(g => g.First()).ToList();

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

    public void Dispose()
    {
        _disposables.Dispose();
        _draftsSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
