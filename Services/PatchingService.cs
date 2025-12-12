using System.IO;
using Boutique.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
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
                        patchMod = SkyrimMod.CreateFromBinary(outputPath, SkyrimRelease.SkyrimSE);
                        _logger.Information("Loaded existing patch containing {ArmorCount} armor overrides.",
                            patchMod.Armors.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to load existing patch at {OutputPath}.", outputPath);
                        return (false, $"Unable to load existing patch: {ex.Message}");
                    }
                else
                    patchMod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

                var existingMasters = patchMod.ModHeader.MasterReferences?
                    .Select(m => m.Master) ?? Enumerable.Empty<ModKey>();
                requiredMasters.UnionWith(existingMasters);

                var current = 0;
                var total = validMatches.Count;

                foreach (var match in validMatches)
                {
                    current++;
                    var sourceName = match.SourceArmor.Name?.String ?? match.SourceArmor.EditorID ?? "Unknown";
                    progress?.Report((current, total, $"Patching {sourceName}..."));

                    // Create a new armor record as override of source
                    var patchedArmor = patchMod.Armors.GetOrAddAsOverride(match.SourceArmor);

                    requiredMasters.Add(match.SourceArmor.FormKey.ModKey);
                    if (match.IsGlamOnly)
                    {
                        ApplyGlamOnlyAdjustments(patchedArmor);
                        continue;
                    }

                    var targetArmor = match.TargetArmor!;
                    requiredMasters.Add(targetArmor.FormKey.ModKey);

                    // Copy stats from target
                    CopyArmorStats(patchedArmor, targetArmor);

                    // Copy keywords from target
                    CopyKeywords(patchedArmor, targetArmor);

                    // Copy enchantment from target
                    CopyEnchantment(patchedArmor, targetArmor);

                    // Note: Tempering recipes are separate records (COBJ) and are handled separately
                }

                //// Handle tempering recipes (temporarily disabled while we investigate freeze issues)
                ////progress?.Report((total, total, "Processing tempering recipes..."));
                ////CopyTemperingRecipes(patchMod, validMatches);

                EnsureMasters(patchMod, requiredMasters);

                // Auto-ESL if under record limit
                TryApplyEslFlag(patchMod);

                // Write patch to file
                progress?.Report((total, total, "Writing patch file..."));

                // Release any file handles held by the environment before writing
                mutagenService.ReleaseLinkCache();

                var writeParameters = new BinaryWriteParameters
                {
                    LowerRangeDisallowedHandler = new NoCheckIfLowerRangeDisallowed()
                };

                patchMod.WriteToBinary(outputPath, writeParameters);

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
                if (outfitList.Count == 0) return (false, "No outfits to create.", (IReadOnlyList<OutfitCreationResult>)[]);

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
                        patchMod = SkyrimMod.CreateFromBinary(outputPath, SkyrimRelease.SkyrimSE);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to load existing patch for outfit creation at {OutputPath}.",
                            outputPath);
                        return (false, $"Unable to load existing patch: {ex.Message}", (IReadOnlyList<OutfitCreationResult>)[]);
                    }
                else
                    patchMod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

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

                    if (pieces.Count == 0)
                    {
                        _logger.Warning("Skipping outfit {EditorId} because it has no armor pieces.", editorId);
                        continue;
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

                // Auto-ESL if under record limit
                TryApplyEslFlag(patchMod);

                progress?.Report((total, total, "Writing patch file..."));

                // Release any file handles held by the environment before writing
                mutagenService.ReleaseLinkCache();

                var writeParameters = new BinaryWriteParameters
                {
                    LowerRangeDisallowedHandler = new NoCheckIfLowerRangeDisallowed()
                };

                try
                {
                    patchMod.WriteToBinary(outputPath, writeParameters);
                }
                catch (IOException ioEx)
                {
                    var lockedMessage =
                        $"Unable to write to {outputPath}. It appears to be locked by another application (Mod Organizer, xEdit, or the Skyrim launcher). Close the application that has the file open, or pick a different output path, then try again.";
                    _logger.Error(ioEx, lockedMessage);
                    return (false, lockedMessage, (IReadOnlyList<OutfitCreationResult>)[]);
                }

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

    private static void ApplyGlamOnlyAdjustments(Armor target)
    {
        target.ArmorRating = 0;
    }

    private static void CopyArmorStats(Armor target, IArmorGetter source)
    {
        // Copy core stats
        target.ArmorRating = source.ArmorRating;
        target.Value = source.Value;
        target.Weight = source.Weight;
    }

    private static void CopyKeywords(Armor target, IArmorGetter source)
    {
        // Clear existing keywords and copy from source
        if (source.Keywords == null) return;
        target.Keywords = [];

        foreach (var keyword in source.Keywords) target.Keywords.Add(keyword);
    }

    private static void CopyEnchantment(Armor target, IArmorGetter source)
    {
        // Copy enchantment reference (ObjectEffect in Mutagen)
        if (source.ObjectEffect.FormKey != FormKey.Null)
            target.ObjectEffect.SetTo(source.ObjectEffect);
        else
            target.ObjectEffect.Clear();

        // Copy enchantment amount if present
        target.EnchantmentAmount = source.EnchantmentAmount;
    }

    private void CopyTemperingRecipes(SkyrimMod patchMod, List<ArmorMatch> matches)
    {
        if (mutagenService.LinkCache == null)
            return;

        var linkCache = mutagenService.LinkCache;

        // Cache all constructible objects once so we can query both source and target recipes efficiently
        var allRecipes = linkCache.PriorityOrder.WinningOverrides<IConstructibleObjectGetter>().ToList();

        foreach (var match in matches)
        {
            if (match.TargetArmor == null)
                continue;

            var targetRecipe = allRecipes.FirstOrDefault(r =>
                r.CreatedObject.FormKey == match.TargetArmor.FormKey &&
                IsTemperingRecipe(r, linkCache));

            if (targetRecipe == null)
                continue;

            var sourceRecipes = allRecipes.Where(r =>
                    r.CreatedObject.FormKey == match.SourceArmor.FormKey &&
                    IsTemperingRecipe(r, linkCache))
                .ToList();

            if (sourceRecipes.Count == 0)
                continue;

            foreach (var sourceRecipe in sourceRecipes)
            {
                var patchedRecipe = patchMod.ConstructibleObjects.GetOrAddAsOverride(sourceRecipe);
                var originalEditorId = patchedRecipe.EditorID;

                patchedRecipe.DeepCopyIn(targetRecipe);

                // Restore identifying data so the recipe still produces the source armor record
                patchedRecipe.EditorID = originalEditorId;
                patchedRecipe.CreatedObject.SetTo(match.SourceArmor.ToLink());
                patchedRecipe.CreatedObjectCount = targetRecipe.CreatedObjectCount;
            }
        }
    }

    private bool IsTemperingRecipe(IConstructibleObjectGetter recipe, ILinkCache linkCache)
    {
        var editorId = recipe.EditorID?.ToLowerInvariant() ?? string.Empty;
        return editorId.Contains("temper") || IsTemperingWorkbench(recipe, linkCache);
    }

    private static bool IsTemperingWorkbench(IConstructibleObjectGetter recipe, ILinkCache linkCache)
    {
        // Check if the workbench keyword indicates tempering
        if (recipe.WorkbenchKeyword.FormKey == FormKey.Null)
            return false;

        if (linkCache.TryResolve<IKeywordGetter>(recipe.WorkbenchKeyword.FormKey, out var keyword))
        {
            var editorId = keyword.EditorID?.ToLowerInvariant() ?? "";
            return editorId.Contains("sharpen") || editorId.Contains("armortable") || editorId.Contains("temper");
        }

        return false;
    }

    private void EnsureMasters(SkyrimMod patchMod, HashSet<ModKey> requiredMasters)
    {
        var masterList = patchMod.ModHeader.MasterReferences;

        var existing = masterList.Select(m => m.Master).ToHashSet();

        foreach (var master in requiredMasters)
        {
            if (master == patchMod.ModKey || master.IsNull)
                continue;

            if (!existing.Add(master)) continue;
            masterList.Add(new MasterReference { Master = master });
            _logger.Debug("Added master {Master} to patch header.", master);
        }

        _logger.Information("Patch master list: {Masters}",
            string.Join(", ", masterList.Select(m => m.Master.FileName)));
    }

    /// <summary>
    /// Attempts to ESL-flag the patch if it's safe to do so (under 2048 new records).
    /// Override records don't count against this limit since they reuse existing FormIDs.
    /// </summary>
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
}