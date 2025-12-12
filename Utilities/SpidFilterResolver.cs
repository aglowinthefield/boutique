using System.Globalization;
using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Utilities;

/// <summary>
/// Resolves parsed SPID filters to DistributionEntry objects using the game's LinkCache.
/// Converts EditorIDs and FormKey strings to actual game records.
/// </summary>
public static class SpidFilterResolver
{
    /// <summary>
    /// Resolves a parsed SpidDistributionFilter to a DistributionEntry.
    /// </summary>
    /// <param name="filter">The parsed SPID filter.</param>
    /// <param name="linkCache">The game's LinkCache for resolving records.</param>
    /// <param name="cachedNpcs">Pre-cached list of NPCs for efficient lookups.</param>
    /// <param name="cachedOutfits">Pre-cached list of outfits for efficient lookups.</param>
    /// <param name="logger">Optional logger for debug output.</param>
    /// <returns>A DistributionEntry if resolution succeeded, null otherwise.</returns>
    public static DistributionEntry? Resolve(
        SpidDistributionFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyList<INpcGetter> cachedNpcs,
        IReadOnlyList<IOutfitGetter> cachedOutfits,
        ILogger? logger = null)
    {
        try
        {
            var outfit = ResolveOutfit(filter.OutfitIdentifier, linkCache, cachedOutfits, logger);
            if (outfit == null)
            {
                logger?.Debug("Could not resolve outfit from identifier: {Identifier}", filter.OutfitIdentifier);
                return null;
            }

            var npcFormKeys = new List<FormKey>();
            var factionFormKeys = new List<FormKey>();
            var keywordFormKeys = new List<FormKey>();
            var raceFormKeys = new List<FormKey>();

            // Process StringFilters - can contain NPC names, keywords, etc.
            ProcessStringFilters(filter.StringFilters, linkCache, cachedNpcs, npcFormKeys, keywordFormKeys, logger);

            // Process FormFilters - can contain factions, races, etc.
            ProcessFormFilters(filter.FormFilters, linkCache, factionFormKeys, raceFormKeys, logger);

            // Must have at least one filter
            if (npcFormKeys.Count == 0 && factionFormKeys.Count == 0 &&
                keywordFormKeys.Count == 0 && raceFormKeys.Count == 0)
            {
                logger?.Debug("No filters could be resolved for SPID line: {Line}", filter.RawLine);
                return null;
            }

            var entry = new DistributionEntry
            {
                Outfit = outfit,
                NpcFormKeys = npcFormKeys,
                FactionFormKeys = factionFormKeys,
                KeywordFormKeys = keywordFormKeys,
                RaceFormKeys = raceFormKeys
            };

            // Set chance if not 100%
            if (filter.Chance != 100)
            {
                entry.Chance = filter.Chance;
            }

            return entry;
        }
        catch (Exception ex)
        {
            logger?.Debug(ex, "Failed to resolve SPID filter: {Line}", filter.RawLine);
            return null;
        }
    }

    /// <summary>
    /// Resolves an outfit identifier to an IOutfitGetter.
    /// Supports tilde format (0x800~Plugin.esp), pipe format (Plugin.esp|0x800), and EditorID.
    /// </summary>
    public static IOutfitGetter? ResolveOutfit(
        string outfitIdentifier,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyList<IOutfitGetter> cachedOutfits,
        ILogger? logger = null)
    {
        // Check for tilde format: 0x800~Plugin.esp
        var tildeIndex = outfitIdentifier.IndexOf('~');
        if (tildeIndex >= 0)
        {
            var formIdString = outfitIdentifier[..tildeIndex].Trim();
            var modKeyString = outfitIdentifier[(tildeIndex + 1)..].Trim();

            formIdString = formIdString.Replace("0x", "").Replace("0X", "");
            if (uint.TryParse(formIdString, NumberStyles.HexNumber, null, out var formId) &&
                ModKey.TryFromNameAndExtension(modKeyString, out var modKey))
            {
                var outfitFormKey = new FormKey(modKey, formId);
                if (linkCache.TryResolve<IOutfitGetter>(outfitFormKey, out var outfit))
                {
                    return outfit;
                }
            }
            logger?.Debug("Failed to resolve tilde-format outfit: {Identifier}", outfitIdentifier);
            return null;
        }

        // Check for pipe format: Plugin.esp|0x800
        if (outfitIdentifier.Contains('|'))
        {
            var pipeIndex = outfitIdentifier.IndexOf('|');
            var modKeyString = outfitIdentifier[..pipeIndex].Trim();
            var formIdString = outfitIdentifier[(pipeIndex + 1)..].Trim();

            formIdString = formIdString.Replace("0x", "").Replace("0X", "");
            if (uint.TryParse(formIdString, NumberStyles.HexNumber, null, out var formId) &&
                ModKey.TryFromNameAndExtension(modKeyString, out var modKey))
            {
                var outfitFormKey = new FormKey(modKey, formId);
                if (linkCache.TryResolve<IOutfitGetter>(outfitFormKey, out var outfit))
                {
                    return outfit;
                }
            }
            logger?.Debug("Failed to resolve pipe-format outfit: {Identifier}", outfitIdentifier);
            return null;
        }

        // Otherwise, treat as EditorID
        var resolvedOutfit = cachedOutfits.FirstOrDefault(o =>
            string.Equals(o.EditorID, outfitIdentifier, StringComparison.OrdinalIgnoreCase));

        if (resolvedOutfit == null)
        {
            logger?.Debug("Failed to resolve EditorID outfit: {Identifier}", outfitIdentifier);
        }

        return resolvedOutfit;
    }

    private static void ProcessStringFilters(
        SpidFilterSection stringFilters,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyList<INpcGetter> cachedNpcs,
        List<FormKey> npcFormKeys,
        List<FormKey> keywordFormKeys,
        ILogger? logger)
    {
        foreach (var expr in stringFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.IsNegated)
                    continue; // Skip negated filters for now

                // Check if it looks like a keyword
                if (part.LooksLikeKeyword)
                {
                    // Try to resolve as keyword
                    var keyword = linkCache.WinningOverrides<IKeywordGetter>()
                        .FirstOrDefault(k => string.Equals(k.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                    if (keyword != null)
                    {
                        keywordFormKeys.Add(keyword.FormKey);
                    }
                    else
                    {
                        logger?.Debug("Could not resolve keyword: {Value}", part.Value);
                    }
                }
                else if (!part.HasWildcard)
                {
                    // Try to resolve as NPC by EditorID or Name
                    var npc = cachedNpcs.FirstOrDefault(n =>
                        string.Equals(n.EditorID, part.Value, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(n.Name?.String, part.Value, StringComparison.OrdinalIgnoreCase));
                    if (npc != null)
                    {
                        npcFormKeys.Add(npc.FormKey);
                    }
                    else
                    {
                        logger?.Debug("Could not resolve NPC: {Value}", part.Value);
                    }
                }
            }
        }
    }

    private static void ProcessFormFilters(
        SpidFilterSection formFilters,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        List<FormKey> factionFormKeys,
        List<FormKey> raceFormKeys,
        ILogger? logger)
    {
        foreach (var expr in formFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.IsNegated)
                    continue; // Skip negated filters for now

                // Try to resolve as faction
                if (part.LooksLikeFaction)
                {
                    var faction = linkCache.WinningOverrides<IFactionGetter>()
                        .FirstOrDefault(f => string.Equals(f.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                    if (faction != null)
                    {
                        factionFormKeys.Add(faction.FormKey);
                        continue;
                    }
                    logger?.Debug("Could not resolve faction: {Value}", part.Value);
                }

                // Try to resolve as race
                if (part.LooksLikeRace)
                {
                    var race = linkCache.WinningOverrides<IRaceGetter>()
                        .FirstOrDefault(r => string.Equals(r.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                    if (race != null)
                    {
                        raceFormKeys.Add(race.FormKey);
                    }
                    else
                    {
                        logger?.Debug("Could not resolve race: {Value}", part.Value);
                    }
                }
            }
        }
    }
}
