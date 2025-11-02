using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using RequiemGlamPatcher.Models;

namespace RequiemGlamPatcher.Services;

public class PatchingService : IPatchingService
{
    private readonly IMutagenService _mutagenService;
    private readonly Serilog.ILogger _logger;

    public PatchingService(IMutagenService mutagenService, ILoggingService loggingService)
    {
        _mutagenService = mutagenService;
        _logger = loggingService.ForContext<PatchingService>();
    }

    public bool ValidatePatch(IEnumerable<ArmorMatch> matches, out string validationMessage)
    {
        var matchList = matches.ToList();

        if (!matchList.Any())
        {
            validationMessage = "No armor matches to patch.";
            return false;
        }

        var validMatches = matchList.Where(m => m.TargetArmor != null).ToList();

        if (!validMatches.Any())
        {
            validationMessage = "No valid armor matches found. Please ensure target armors are selected.";
            return false;
        }

        if (!_mutagenService.IsInitialized)
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
        return await Task.Run(() =>
        {
            try
            {
                var validMatches = matches.Where(m => m.TargetArmor != null).ToList();
                var requiredMasters = new HashSet<ModKey>();

                _logger.Information("Beginning patch creation. Destination: {OutputPath}. Matches: {MatchCount}", outputPath, validMatches.Count);

                if (!validMatches.Any())
                {
                    _logger.Warning("Patch creation aborted — no valid matches were provided.");
                    return (false, "No valid matches to patch.");
                }

                // Create new patch mod
                var patchMod = new SkyrimMod(ModKey.FromFileName(Path.GetFileName(outputPath)), SkyrimRelease.SkyrimSE);

                int current = 0;
                int total = validMatches.Count;

                foreach (var match in validMatches)
                {
                    current++;
                    var sourceName = match.SourceArmor.Name?.String ?? match.SourceArmor.EditorID ?? "Unknown";
                    progress?.Report((current, total, $"Patching {sourceName}..."));

                    // Create a new armor record as override of source
                    var patchedArmor = patchMod.Armors.GetOrAddAsOverride(match.SourceArmor);

                    requiredMasters.Add(match.SourceArmor.FormKey.ModKey);
                    if (match.TargetArmor is { } targetArmor)
                    {
                        requiredMasters.Add(targetArmor.FormKey.ModKey);
                    }

                    // Copy stats from target
                    CopyArmorStats(patchedArmor, match.TargetArmor!);

                    // Copy keywords from target
                    CopyKeywords(patchedArmor, match.TargetArmor!);

                    // Copy enchantment from target
                    CopyEnchantment(patchedArmor, match.TargetArmor!);

                    // Note: Tempering recipes are separate records (COBJ) and are handled separately
                }

                //// Handle tempering recipes (temporarily disabled while we investigate freeze issues)
                ////progress?.Report((total, total, "Processing tempering recipes..."));
                ////CopyTemperingRecipes(patchMod, validMatches);

                EnsureMasters(patchMod, requiredMasters);

                // Write patch to file
                progress?.Report((total, total, "Writing patch file..."));

                patchMod.WriteToBinary(outputPath);

                _logger.Information("Patch successfully written to {OutputPath}", outputPath);

                return (true, $"Successfully created patch with {validMatches.Count} armor(s) at {outputPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating patch destined for {OutputPath}", outputPath);
                return (false, $"Error creating patch: {ex.Message}");
            }
        });
    }

    private void CopyArmorStats(Armor target, IArmorGetter source)
    {
        // Copy core stats
        target.ArmorRating = source.ArmorRating;
        target.Value = source.Value;
        target.Weight = source.Weight;
    }

    private void CopyKeywords(Armor target, IArmorGetter source)
    {
        // Clear existing keywords and copy from source
        if (source.Keywords != null)
        {
            target.Keywords = new Noggog.ExtendedList<IFormLinkGetter<IKeywordGetter>>();

            foreach (var keyword in source.Keywords)
            {
                target.Keywords.Add(keyword);
            }
        }
    }

    private void CopyEnchantment(Armor target, IArmorGetter source)
    {
        // Copy enchantment reference (ObjectEffect in Mutagen)
        if (source.ObjectEffect.FormKey != FormKey.Null)
        {
            target.ObjectEffect.SetTo(source.ObjectEffect);
        }
        else
        {
            target.ObjectEffect.Clear();
        }

        // Copy enchantment amount if present
        target.EnchantmentAmount = source.EnchantmentAmount;
    }

    private void CopyTemperingRecipes(SkyrimMod patchMod, List<ArmorMatch> matches)
    {
        if (_mutagenService.LinkCache == null)
            return;

        var linkCache = _mutagenService.LinkCache;

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

            if (!sourceRecipes.Any())
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

    private bool IsTemperingRecipe(IConstructibleObjectGetter recipe, Mutagen.Bethesda.Plugins.Cache.ILinkCache linkCache)
    {
        var editorId = recipe.EditorID?.ToLowerInvariant() ?? string.Empty;
        if (editorId.Contains("temper"))
            return true;

        return IsTemperingWorkbench(recipe, linkCache);
    }

    private bool IsTemperingWorkbench(IConstructibleObjectGetter recipe, Mutagen.Bethesda.Plugins.Cache.ILinkCache linkCache)
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
        foreach (var master in requiredMasters)
        {
            if (master == patchMod.ModKey)
                continue;

            var alreadyPresent = patchMod.ModHeader.MasterReferences.Any(m => m.Master == master);
            if (!alreadyPresent)
            {
                patchMod.ModHeader.MasterReferences.Add(new MasterReference { Master = master });
                _logger.Debug("Added master {Master} to patch header.", master);
            }
        }
    }
}
