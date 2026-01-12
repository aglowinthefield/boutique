using System.IO;
using Boutique.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Serilog;

namespace Boutique.Services;

public class PatchingService(MutagenService mutagenService, ILoggingService loggingService)
{
    private readonly ILogger _logger = loggingService.ForContext<PatchingService>();

    public bool ValidatePatch(IEnumerable<ArmorMatch> matches, out string validationMessage)
    {
        var matchList = matches.ToList();

        if (matchList.Count == 0)
        {
            validationMessage = "No armor matches to patch.";
            return false;
        }

        var validMatches = matchList.Where(m => m.TargetArmor != null).ToList();

        if (validMatches.Count == 0)
        {
            validationMessage = "No valid armor matches found. Please ensure target armors are selected.";
            return false;
        }

        if (!mutagenService.IsInitialized)
        {
            validationMessage = "Mutagen service is not initialized. Please set the Skyrim data path first.";
            return false;
        }

        validationMessage = $"Ready to patch {validMatches.Count} armor(s).";
        return true;
    }

    public async Task<(bool success, string message)> CreatePatchAsync(
        IEnumerable<ArmorMatch> matches,
        string outputPath,
        IProgress<(int current, int total, string message)>? progress = null)
    {
        var result = await Task.Run(() =>
        {
            try
            {
                var validMatches = matches.Where(m => m.TargetArmor != null || m.IsGlamOnly).ToList();
                var requiredMasters = new HashSet<ModKey>();

                _logger.Information("Beginning patch creation. Destination: {OutputPath}. Matches: {MatchCount}",
                    outputPath, validMatches.Count);

                if (validMatches.Count == 0)
                {
                    _logger.Warning("Patch creation aborted — no valid matches were provided.");
                    return (false, "No valid matches to patch.");
                }

                var modKey = ModKey.FromFileName(Path.GetFileName(outputPath));
                SkyrimMod patchMod;

                if (File.Exists(outputPath))
                    try
                    {
                        _logger.Information("Existing patch detected at {OutputPath}; loading for append.", outputPath);
                        patchMod = SkyrimMod.CreateFromBinary(outputPath, mutagenService.SkyrimRelease);
                        _logger.Information("Loaded existing patch containing {ArmorCount} armor overrides.",
                            patchMod.Armors.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to load existing patch at {OutputPath}.", outputPath);
                        return (false, $"Unable to load existing patch: {ex.Message}");
                    }
                else
                    patchMod = new SkyrimMod(modKey, mutagenService.SkyrimRelease);

                var existingMasters = patchMod.ModHeader.MasterReferences?
                    .Select(m => m.Master) ?? [];
                requiredMasters.UnionWith(existingMasters);

                var current = 0;
                var total = validMatches.Count;

                foreach (var match in validMatches)
                {
                    current++;
                    var sourceName = match.SourceArmor.Name?.String ?? match.SourceArmor.EditorID ?? "Unknown";
                    progress?.Report((current, total, $"Patching {sourceName}..."));

                    var patchedArmor = patchMod.Armors.GetOrAddAsOverride(match.SourceArmor);

                    requiredMasters.Add(match.SourceArmor.FormKey.ModKey);
                    if (match.IsGlamOnly)
                    {
                        ApplyGlamOnlyAdjustments(patchedArmor);
                        continue;
                    }

                    var targetArmor = match.TargetArmor!;
                    requiredMasters.Add(targetArmor.FormKey.ModKey);

                    CopyArmorStats(patchedArmor, targetArmor);
                    CopyKeywords(patchedArmor, targetArmor);
                    CopyEnchantment(patchedArmor, targetArmor);
                }

                //// Handle tempering recipes (temporarily disabled while we investigate freeze issues)
                ////progress?.Report((total, total, "Processing tempering recipes..."));
                ////CopyTemperingRecipes(patchMod, validMatches);

                EnsureMasters(patchMod, requiredMasters);

                TryApplyEslFlag(patchMod);

                progress?.Report((total, total, "Writing patch file..."));
                mutagenService.ReleaseLinkCache();

                var writeParameters = new BinaryWriteParameters
                {
                    LowerRangeDisallowedHandler = new NoCheckIfLowerRangeDisallowed()
                };

                WritePatchWithRetry(patchMod, outputPath, writeParameters);

                _logger.Information("Patch successfully written to {OutputPath}", outputPath);

                return (true, $"Successfully created patch with {validMatches.Count} armor(s) at {outputPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating patch destined for {OutputPath}", outputPath);
                return (false, $"Error creating patch: {ex.Message}");
            }
        });

        // Refresh the link cache so subsequent operations can read the newly written patch
        if (result.Item1)
        {
            progress?.Report((1, 1, "Refreshing load order..."));
            var pluginName = Path.GetFileName(outputPath);
            await mutagenService.RefreshLinkCacheAsync(pluginName);
        }

        return result;
    }

    public async Task<(bool success, string message, IReadOnlyList<OutfitCreationResult> results)>
        CreateOrUpdateOutfitsAsync(
            IEnumerable<OutfitCreationRequest> outfits,
            string outputPath,
            IProgress<(int current, int total, string message)>? progress = null)
    {
        var result = await Task.Run(() =>
        {
            try
            {
                var outfitList = outfits.ToList();
                if (outfitList.Count == 0)
                    return (false, "No outfits to create.", (IReadOnlyList<OutfitCreationResult>)[]);

                if (!mutagenService.IsInitialized)
                    return (false, "Mutagen service is not initialized. Please set the Skyrim data path first.", (IReadOnlyList<OutfitCreationResult>)[]);

                _logger.Information("Beginning outfit creation. Destination: {OutputPath}. OutfitCount={Count}",
                    outputPath, outfitList.Count);

                var requiredMasters = new HashSet<ModKey>();
                var modKey = ModKey.FromFileName(Path.GetFileName(outputPath));

                SkyrimMod patchMod;
                if (File.Exists(outputPath))
                    try
                    {
                        _logger.Information("Loading existing patch at {OutputPath} for outfit append.", outputPath);
                        patchMod = SkyrimMod.CreateFromBinary(outputPath, mutagenService.SkyrimRelease);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to load existing patch for outfit creation at {OutputPath}.",
                            outputPath);
                        return (false, $"Unable to load existing patch: {ex.Message}", (IReadOnlyList<OutfitCreationResult>)[]);
                    }
                else
                    patchMod = new SkyrimMod(modKey, mutagenService.SkyrimRelease);

                var existingOutfitMasters = patchMod.ModHeader.MasterReferences.Select(m => m.Master);
                requiredMasters.UnionWith(existingOutfitMasters);

                var results = new List<OutfitCreationResult>();
                var total = outfitList.Count;
                var current = 0;

                foreach (var (name, editorId, pieces) in outfitList)
                {
                    current++;
                    progress?.Report((current, total, $"Writing outfit {name}..."));

                    var existing = patchMod.Outfits
                        .FirstOrDefault(o =>
                            string.Equals(o.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

                    if (pieces.Count == 0)
                    {
                        if (existing != null)
                        {
                            patchMod.Outfits.Remove(existing);
                            _logger.Information("Deleted outfit {EditorId}.", editorId);
                            results.Add(new OutfitCreationResult(editorId, existing.FormKey));
                        }
                        else
                        {
                            _logger.Debug("Skipping deletion of {EditorId} — not in patch.", editorId);
                        }
                        continue;
                    }

                    Outfit outfit;
                    if (existing != null)
                    {
                        outfit = existing;
                        _logger.Information("Updating existing outfit {EditorId} with {PieceCount} piece(s).",
                            editorId, pieces.Count);
                    }
                    else
                    {
                        outfit = patchMod.Outfits.AddNew();
                        outfit.EditorID = editorId;
                        _logger.Information("Creating new outfit {EditorId} with {PieceCount} piece(s).",
                            editorId, pieces.Count);
                    }

                    var items = outfit.Items ??= [];
                    items.Clear();
                    foreach (var armor in pieces)
                    {
                        items.Add(armor.ToLink());
                        requiredMasters.Add(armor.FormKey.ModKey);
                    }

                    results.Add(new OutfitCreationResult(editorId, outfit.FormKey));
                }

                EnsureMasters(patchMod, requiredMasters);
                TryApplyEslFlag(patchMod);

                progress?.Report((total, total, "Writing patch file..."));
                mutagenService.ReleaseLinkCache();

                var writeParameters = new BinaryWriteParameters
                {
                    LowerRangeDisallowedHandler = new NoCheckIfLowerRangeDisallowed()
                };

                WritePatchWithRetry(patchMod, outputPath, writeParameters);

                _logger.Information("Outfit creation completed successfully. File: {OutputPath}", outputPath);

                return (true, $"Saved {results.Count} outfit(s) to {outputPath}", (IReadOnlyList<OutfitCreationResult>)results);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating outfits destined for {OutputPath}", outputPath);
                return (false, $"Error creating outfits: {ex.Message}",
                    (IReadOnlyList<OutfitCreationResult>)[]);
            }
        });

        // Refresh the link cache so subsequent operations can read the newly written patch
        if (result.Item1)
        {
            progress?.Report((1, 1, "Refreshing load order..."));
            var pluginName = Path.GetFileName(outputPath);
            await mutagenService.RefreshLinkCacheAsync(pluginName);
        }

        return result;
    }

    private static void ApplyGlamOnlyAdjustments(Armor target) => target.ArmorRating = 0;

    private static void CopyArmorStats(Armor target, IArmorGetter source)
    {
        target.ArmorRating = source.ArmorRating;
        target.Value = source.Value;
        target.Weight = source.Weight;
    }

    private static void CopyKeywords(Armor target, IArmorGetter source)
    {
        if (source.Keywords == null)
            return;
        target.Keywords = [];

        foreach (var keyword in source.Keywords)
            target.Keywords.Add(keyword);
    }

    private static void CopyEnchantment(Armor target, IArmorGetter source)
    {
        if (source.ObjectEffect.FormKey != FormKey.Null)
            target.ObjectEffect.SetTo(source.ObjectEffect);
        else
            target.ObjectEffect.Clear();

        target.EnchantmentAmount = source.EnchantmentAmount;
    }

    private void EnsureMasters(SkyrimMod patchMod, HashSet<ModKey> requiredMasters)
    {
        var masterList = patchMod.ModHeader.MasterReferences;

        var existing = masterList.Select(m => m.Master).ToHashSet();

        foreach (var master in requiredMasters)
        {
            if (master == patchMod.ModKey || master.IsNull)
                continue;

            if (!existing.Add(master))
                continue;
            masterList.Add(new MasterReference { Master = master });
            _logger.Debug("Added master {Master} to patch header.", master);
        }

        _logger.Information("Patch master list: {Masters}",
            string.Join(", ", masterList.Select(m => m.Master.FileName)));
    }

    private void WritePatchWithRetry(SkyrimMod patchMod, string outputPath, BinaryWriteParameters writeParameters)
    {
        var tempPath = outputPath + ".tmp";

        var tempWriteParameters = writeParameters with
        {
            ModKey = ModKeyOption.NoCheck
        };
        patchMod.WriteToBinary(tempPath, tempWriteParameters);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        const int maxRetries = 10;
        const int initialDelayMs = 50;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                File.Move(tempPath, outputPath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                var delay = initialDelayMs * attempt;
                _logger.Debug("File replace attempt {Attempt}/{Max} failed, retrying in {Delay}ms...",
                    attempt, maxRetries, delay);
                Thread.Sleep(delay);
            }
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private void TryApplyEslFlag(SkyrimMod patchMod)
    {
        // ESL plugins can have at most 2048 new records (FormIDs 0x000-0x7FF in light master range)
        // Override records don't count - they reuse the original FormID
        const int eslRecordLimit = 2048;

        // Count only NEW records (those with FormKeys belonging to this mod)
        var newRecordCount = patchMod.EnumerateMajorRecords()
            .Count(r => r.FormKey.ModKey == patchMod.ModKey);

        if (newRecordCount < eslRecordLimit)
        {
            patchMod.ModHeader.Flags |= SkyrimModHeader.HeaderFlag.Small;
            _logger.Information("ESL flag applied. New record count: {Count} (limit: {Limit}).",
                newRecordCount, eslRecordLimit);
        }
        else
        {
            _logger.Warning("ESL flag NOT applied. New record count {Count} exceeds limit of {Limit}.",
                newRecordCount, eslRecordLimit);
        }
    }

    public async Task<MissingMastersResult> CheckMissingMastersAsync(string patchPath)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(patchPath))
            {
                _logger.Debug("Patch file does not exist at {Path}, no missing masters check needed.", patchPath);
                return new MissingMastersResult(false, [], [], []);
            }

            try
            {
                using var patchMod = SkyrimMod.CreateFromBinaryOverlay(patchPath, SkyrimRelease.SkyrimSE);
                var dataFolder = mutagenService.DataFolderPath ?? string.Empty;
                var loadOrderModKeys = mutagenService.GetLoadOrderModKeys();
                var masterRefs = patchMod.ModHeader.MasterReferences.Select(m => m.Master).ToList();
                var missingMasters = new List<ModKey>();
                var patchModKey = patchMod.ModKey;

                foreach (var master in masterRefs)
                {
                    if (master == patchModKey)
                        continue;

                    if (!loadOrderModKeys.Contains(master))
                    {
                        missingMasters.Add(master);
                        _logger.Warning("Missing master detected: {Master}", master.FileName);
                    }
                }

                if (missingMasters.Count == 0)
                {
                    _logger.Debug("All masters present for patch {Patch}.", patchPath);
                    var validOutfits = patchMod.Outfits.ToList();
                    return new MissingMastersResult(false, [], [], validOutfits);
                }

                var missingMasterSet = missingMasters.ToHashSet();
                var affectedOutfitsByMaster = new Dictionary<ModKey, List<AffectedOutfitInfo>>();
                var validOutfitsList = new List<IOutfitGetter>();

                foreach (var outfit in patchMod.Outfits)
                {
                    var orphanedFormKeys = new List<FormKey>();
                    var affectingMasters = new HashSet<ModKey>();

                    if (outfit.Items != null)
                    {
                        foreach (var itemLink in outfit.Items)
                        {
                            var formKeyNullable = itemLink?.FormKeyNullable;
                            if (!formKeyNullable.HasValue || formKeyNullable.Value == FormKey.Null)
                                continue;

                            var itemModKey = formKeyNullable.Value.ModKey;
                            if (missingMasterSet.Contains(itemModKey))
                            {
                                orphanedFormKeys.Add(formKeyNullable.Value);
                                affectingMasters.Add(itemModKey);
                            }
                        }
                    }

                    if (orphanedFormKeys.Count > 0)
                    {
                        var affectedInfo = new AffectedOutfitInfo(
                            outfit.FormKey,
                            outfit.EditorID,
                            orphanedFormKeys);

                        foreach (var master in affectingMasters)
                        {
                            if (!affectedOutfitsByMaster.TryGetValue(master, out var list))
                            {
                                list = [];
                                affectedOutfitsByMaster[master] = list;
                            }
                            list.Add(affectedInfo);
                        }
                    }
                    else
                    {
                        validOutfitsList.Add(outfit);
                    }
                }

                var missingMasterInfos = missingMasters
                    .Select(m => new MissingMasterInfo(
                        m,
                        affectedOutfitsByMaster.TryGetValue(m, out var list) ? list : []))
                    .ToList();

                var allAffectedOutfits = affectedOutfitsByMaster
                    .SelectMany(kvp => kvp.Value)
                    .DistinctBy(a => a.FormKey)
                    .ToList();

                _logger.Information(
                    "Missing masters check complete: {MissingCount} missing master(s), {AffectedCount} affected outfit(s).",
                    missingMasters.Count, allAffectedOutfits.Count);

                return new MissingMastersResult(true, missingMasterInfos, allAffectedOutfits, validOutfitsList);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking missing masters for patch {Path}.", patchPath);
                return new MissingMastersResult(false, [], [], []);
            }
        });
    }

    public async Task<(bool success, string message)> CleanPatchMissingMastersAsync(
        string patchPath,
        IReadOnlyList<AffectedOutfitInfo> outfitsToRemove)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(patchPath))
                    return (false, "Patch file does not exist.");

                _logger.Information("Cleaning patch {Path}: removing {Count} outfit(s) with missing masters.",
                    patchPath, outfitsToRemove.Count);

                var patchMod = SkyrimMod.CreateFromBinary(patchPath, mutagenService.SkyrimRelease);
                var outfitsToRemoveSet = outfitsToRemove.Select(o => o.FormKey).ToHashSet();

                var removedCount = 0;
                var outfitsToKeep = patchMod.Outfits
                    .Where(o =>
                    {
                        if (outfitsToRemoveSet.Contains(o.FormKey))
                        {
                            _logger.Debug("Removing outfit {EditorId} ({FormKey}) due to missing masters.",
                                o.EditorID, o.FormKey);
                            removedCount++;
                            return false;
                        }
                        return true;
                    })
                    .ToList();

                patchMod.Outfits.Clear();
                foreach (var outfit in outfitsToKeep)
                    patchMod.Outfits.Add(outfit);

                var remainingMasters = CollectRequiredMasters(patchMod, outfitsToRemoveSet);
                CleanupMasterReferences(patchMod, remainingMasters);

                TryApplyEslFlag(patchMod);

                mutagenService.ReleaseLinkCache();

                var writeParameters = new BinaryWriteParameters
                {
                    LowerRangeDisallowedHandler = new NoCheckIfLowerRangeDisallowed()
                };

                WritePatchWithRetry(patchMod, patchPath, writeParameters);

                _logger.Information("Patch cleaned successfully. Removed {Count} outfit(s).", removedCount);
                return (true, $"Successfully removed {removedCount} outfit(s) with missing masters.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cleaning patch {Path}.", patchPath);
                return (false, $"Error cleaning patch: {ex.Message}");
            }
        });
    }

    private static HashSet<ModKey> CollectRequiredMasters(SkyrimMod patchMod, HashSet<FormKey> excludedOutfits)
    {
        var requiredMasters = new HashSet<ModKey>();

        foreach (var record in patchMod.EnumerateMajorRecords())
        {
            if (record is IOutfitGetter outfit && excludedOutfits.Contains(outfit.FormKey))
                continue;

            requiredMasters.Add(record.FormKey.ModKey);

            if (record is IOutfitGetter outfitRecord && outfitRecord.Items != null)
            {
                foreach (var item in outfitRecord.Items)
                {
                    var formKey = item?.FormKeyNullable;
                    if (formKey.HasValue && formKey.Value != FormKey.Null)
                        requiredMasters.Add(formKey.Value.ModKey);
                }
            }

            if (record is IArmorGetter armor)
            {
                if (armor.ObjectEffect.FormKeyNullable is { } enchantKey && enchantKey != FormKey.Null)
                    requiredMasters.Add(enchantKey.ModKey);

                if (armor.Keywords != null)
                {
                    foreach (var keyword in armor.Keywords)
                    {
                        if (keyword.FormKeyNullable is { } keywordKey && keywordKey != FormKey.Null)
                            requiredMasters.Add(keywordKey.ModKey);
                    }
                }
            }
        }

        return requiredMasters;
    }

    private void CleanupMasterReferences(SkyrimMod patchMod, HashSet<ModKey> requiredMasters)
    {
        var masterList = patchMod.ModHeader.MasterReferences;
        var mastersToRemove = masterList
            .Where(m => !requiredMasters.Contains(m.Master) && m.Master != patchMod.ModKey)
            .ToList();

        foreach (var master in mastersToRemove)
        {
            masterList.Remove(master);
            _logger.Debug("Removed unused master {Master} from patch header.", master.Master.FileName);
        }

        _logger.Information("Cleaned master list: {Masters}",
            string.Join(", ", masterList.Select(m => m.Master.FileName)));
    }
}
