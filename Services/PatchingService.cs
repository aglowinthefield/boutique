using System.IO;
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

    public PatchingService(IMutagenService mutagenService)
    {
        _mutagenService = mutagenService;
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

                if (!validMatches.Any())
                    return (false, "No valid matches to patch.");

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

                    // Copy stats from target
                    CopyArmorStats(patchedArmor, match.TargetArmor!);

                    // Copy keywords from target
                    CopyKeywords(patchedArmor, match.TargetArmor!);

                    // Copy enchantment from target
                    CopyEnchantment(patchedArmor, match.TargetArmor!);

                    // Note: Tempering recipes are separate records (COBJ) and are handled separately
                }

                // Handle tempering recipes
                progress?.Report((total, total, "Processing tempering recipes..."));
                CopyTemperingRecipes(patchMod, validMatches);

                // Write patch to file
                progress?.Report((total, total, "Writing patch file..."));

                patchMod.WriteToBinary(outputPath);

                return (true, $"Successfully created patch with {validMatches.Count} armor(s) at {outputPath}");
            }
            catch (Exception ex)
            {
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

        // For each matched armor, try to find and copy its tempering recipe
        foreach (var match in matches)
        {
            if (match.TargetArmor == null)
                continue;

            // Search for constructible objects (recipes) that create this armor
            var targetFormKey = match.TargetArmor.FormKey;

            // Get all constructible objects from the load order
            var recipes = linkCache.PriorityOrder.WinningOverrides<IConstructibleObjectGetter>();

            foreach (var recipe in recipes)
            {
                // Check if this recipe creates our target armor
                if (recipe.CreatedObject.FormKey == targetFormKey)
                {
                    // Check if it's a tempering recipe (usually has specific workbench keywords)
                    var editorId = recipe.EditorID?.ToLowerInvariant() ?? "";
                    if (editorId.Contains("temper") || IsTemperingWorkbench(recipe, linkCache))
                    {
                        // Create override that points to our source armor instead
                        var patchedRecipe = patchMod.ConstructibleObjects.GetOrAddAsOverride(recipe);

                        // Update to create the source armor (keeping all other properties)
                        patchedRecipe.CreatedObject.SetTo(match.SourceArmor.ToLink());
                    }
                }
            }
        }
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
}
