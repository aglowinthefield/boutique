using System.Globalization;
using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Utilities;

public static class SpidFilterResolver
{
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
            var classFormKeys = new List<FormKey>();
            var combatStyleFormKeys = new List<FormKey>();
            var outfitFilterFormKeys = new List<FormKey>();
            var perkFormKeys = new List<FormKey>();
            var voiceTypeFormKeys = new List<FormKey>();
            var locationFormKeys = new List<FormKey>();
            var formListFormKeys = new List<FormKey>();

            // Process StringFilters - can contain NPC names, keywords, etc.
            ProcessStringFilters(filter.StringFilters, linkCache, cachedNpcs, npcFormKeys, keywordFormKeys, logger);

            // Process FormFilters - can contain factions, races, classes, combat styles, outfits, perks, voice types, locations, formlists
            ProcessFormFilters(filter.FormFilters, linkCache, factionFormKeys, raceFormKeys, classFormKeys,
                combatStyleFormKeys, outfitFilterFormKeys, perkFormKeys, voiceTypeFormKeys, locationFormKeys, formListFormKeys, logger);

            // Must have at least one filter
            var hasAnyFilter = npcFormKeys.Count > 0 || factionFormKeys.Count > 0 || keywordFormKeys.Count > 0 ||
                               raceFormKeys.Count > 0 || classFormKeys.Count > 0 || combatStyleFormKeys.Count > 0 ||
                               outfitFilterFormKeys.Count > 0 || perkFormKeys.Count > 0 || voiceTypeFormKeys.Count > 0 ||
                               locationFormKeys.Count > 0 || formListFormKeys.Count > 0;

            if (!hasAnyFilter)
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
                RaceFormKeys = raceFormKeys,
                ClassFormKeys = classFormKeys,
                CombatStyleFormKeys = combatStyleFormKeys,
                OutfitFilterFormKeys = outfitFilterFormKeys,
                PerkFormKeys = perkFormKeys,
                VoiceTypeFormKeys = voiceTypeFormKeys,
                LocationFormKeys = locationFormKeys,
                FormListFormKeys = formListFormKeys,
                TraitFilters = filter.TraitFilters
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

            formIdString = FormKeyHelper.StripHexPrefix(formIdString);
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

            formIdString = FormKeyHelper.StripHexPrefix(formIdString);
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
                if (part.IsNegated || part.HasWildcard)
                    continue;

                // Try to resolve as keyword first (keywords can have any EditorID)
                var keyword = linkCache.WinningOverrides<IKeywordGetter>()
                    .FirstOrDefault(k => string.Equals(k.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (keyword != null)
                {
                    keywordFormKeys.Add(keyword.FormKey);
                    continue;
                }

                // If not a keyword, try to resolve as NPC by EditorID or Name
                var npc = cachedNpcs.FirstOrDefault(n =>
                    string.Equals(n.EditorID, part.Value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Name?.String, part.Value, StringComparison.OrdinalIgnoreCase));
                if (npc != null)
                {
                    npcFormKeys.Add(npc.FormKey);
                }
                else
                {
                    logger?.Debug("Could not resolve string filter as keyword or NPC: {Value}", part.Value);
                }
            }
        }
    }

    private static void ProcessFormFilters(
        SpidFilterSection formFilters,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        List<FormKey> factionFormKeys,
        List<FormKey> raceFormKeys,
        List<FormKey> classFormKeys,
        List<FormKey> combatStyleFormKeys,
        List<FormKey> outfitFilterFormKeys,
        List<FormKey> perkFormKeys,
        List<FormKey> voiceTypeFormKeys,
        List<FormKey> locationFormKeys,
        List<FormKey> formListFormKeys,
        ILogger? logger)
    {
        foreach (var expr in formFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.IsNegated)
                    continue;

                // Try faction
                var faction = linkCache.WinningOverrides<IFactionGetter>()
                    .FirstOrDefault(f => string.Equals(f.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (faction != null)
                {
                    factionFormKeys.Add(faction.FormKey);
                    continue;
                }

                // Try race
                var race = linkCache.WinningOverrides<IRaceGetter>()
                    .FirstOrDefault(r => string.Equals(r.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (race != null)
                {
                    raceFormKeys.Add(race.FormKey);
                    continue;
                }

                // Try class
                var classRecord = linkCache.WinningOverrides<IClassGetter>()
                    .FirstOrDefault(c => string.Equals(c.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (classRecord != null)
                {
                    classFormKeys.Add(classRecord.FormKey);
                    continue;
                }

                // Try combat style
                var combatStyle = linkCache.WinningOverrides<ICombatStyleGetter>()
                    .FirstOrDefault(cs => string.Equals(cs.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (combatStyle != null)
                {
                    combatStyleFormKeys.Add(combatStyle.FormKey);
                    continue;
                }

                // Try outfit (as filter, not the distributed outfit)
                var outfitFilter = linkCache.WinningOverrides<IOutfitGetter>()
                    .FirstOrDefault(o => string.Equals(o.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (outfitFilter != null)
                {
                    outfitFilterFormKeys.Add(outfitFilter.FormKey);
                    continue;
                }

                // Try perk
                var perk = linkCache.WinningOverrides<IPerkGetter>()
                    .FirstOrDefault(p => string.Equals(p.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (perk != null)
                {
                    perkFormKeys.Add(perk.FormKey);
                    continue;
                }

                // Try voice type
                var voiceType = linkCache.WinningOverrides<IVoiceTypeGetter>()
                    .FirstOrDefault(v => string.Equals(v.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (voiceType != null)
                {
                    voiceTypeFormKeys.Add(voiceType.FormKey);
                    continue;
                }

                // Try location
                var location = linkCache.WinningOverrides<ILocationGetter>()
                    .FirstOrDefault(l => string.Equals(l.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (location != null)
                {
                    locationFormKeys.Add(location.FormKey);
                    continue;
                }

                // Try formlist
                var formList = linkCache.WinningOverrides<IFormListGetter>()
                    .FirstOrDefault(fl => string.Equals(fl.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (formList != null)
                {
                    formListFormKeys.Add(formList.FormKey);
                    continue;
                }

                logger?.Debug("Could not resolve form filter: {Value}", part.Value);
            }
        }
    }
}
