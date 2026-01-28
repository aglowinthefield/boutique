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
        ILogger? logger = null) =>
        Resolve(filter, linkCache, cachedNpcs, cachedOutfits, null, logger);

    public static DistributionEntry? Resolve(
        SpidDistributionFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyList<INpcGetter> cachedNpcs,
        IReadOnlyList<IOutfitGetter> cachedOutfits,
        IReadOnlySet<string>? knownVirtualKeywords,
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

            var npcFilters = new List<FormKeyFilter>();
            var keywordFilters = new List<KeywordFilter>();
            var factionFilters = new List<FormKeyFilter>();
            var raceFilters = new List<FormKeyFilter>();
            var classFormKeys = new List<FormKey>();
            var combatStyleFormKeys = new List<FormKey>();
            var outfitFilterFormKeys = new List<FormKey>();
            var perkFormKeys = new List<FormKey>();
            var voiceTypeFormKeys = new List<FormKey>();
            var locationFormKeys = new List<FormKey>();
            var formListFormKeys = new List<FormKey>();

            ProcessStringFilters(
                filter.StringFilters,
                linkCache,
                cachedNpcs,
                npcFilters,
                keywordFilters,
                knownVirtualKeywords,
                logger);

            var resolvedFormEditorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ProcessFormFilters(
                filter.FormFilters,
                linkCache,
                npcFilters,
                factionFilters,
                raceFilters,
                classFormKeys,
                combatStyleFormKeys,
                outfitFilterFormKeys,
                perkFormKeys,
                voiceTypeFormKeys,
                locationFormKeys,
                formListFormKeys,
                resolvedFormEditorIds,
                logger);

            var rawStringFilters = ExtractUnresolvableStringFilters(filter.StringFilters, keywordFilters);
            var rawFormFilters = ExtractUnresolvableFormFilters(filter.FormFilters, resolvedFormEditorIds);

            var hasAnyFilter = npcFilters.Count > 0 || factionFilters.Count > 0 ||
                               keywordFilters.Count > 0 || raceFilters.Count > 0 ||
                               classFormKeys.Count > 0 || combatStyleFormKeys.Count > 0 ||
                               outfitFilterFormKeys.Count > 0 || perkFormKeys.Count > 0 ||
                               voiceTypeFormKeys.Count > 0 ||
                               locationFormKeys.Count > 0 || formListFormKeys.Count > 0 ||
                               !string.IsNullOrEmpty(rawStringFilters) || !string.IsNullOrEmpty(rawFormFilters);

            if (!hasAnyFilter && !filter.TargetsAllNpcs)
            {
                logger?.Debug("No filters could be resolved for SPID line: {Line}", filter.RawLine);
                return null;
            }

            var entry = new DistributionEntry
            {
                Outfit = outfit,
                NpcFilters = npcFilters,
                KeywordFilters = keywordFilters,
                FactionFilters = factionFilters,
                RaceFilters = raceFilters,
                ClassFormKeys = classFormKeys,
                CombatStyleFormKeys = combatStyleFormKeys,
                OutfitFilterFormKeys = outfitFilterFormKeys,
                PerkFormKeys = perkFormKeys,
                VoiceTypeFormKeys = voiceTypeFormKeys,
                LocationFormKeys = locationFormKeys,
                FormListFormKeys = formListFormKeys,
                TraitFilters = filter.TraitFilters,
                LevelFilters = filter.LevelFilters,
                RawStringFilters = rawStringFilters,
                RawFormFilters = rawFormFilters
            };

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

    public static DistributionEntry? ResolveKeyword(
        SpidDistributionFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyList<INpcGetter> cachedNpcs,
        IReadOnlySet<string>? knownVirtualKeywords = null,
        ILogger? logger = null)
    {
        try
        {
            var keywordToDistribute = filter.FormIdentifier;
            if (string.IsNullOrWhiteSpace(keywordToDistribute))
            {
                logger?.Debug("Empty keyword identifier in Keyword distribution line: {Line}", filter.RawLine);
                return null;
            }

            var npcFilters = new List<FormKeyFilter>();
            var keywordFilters = new List<KeywordFilter>();
            var factionFilters = new List<FormKeyFilter>();
            var raceFilters = new List<FormKeyFilter>();
            var classFormKeys = new List<FormKey>();
            var combatStyleFormKeys = new List<FormKey>();
            var outfitFilterFormKeys = new List<FormKey>();
            var perkFormKeys = new List<FormKey>();
            var voiceTypeFormKeys = new List<FormKey>();
            var locationFormKeys = new List<FormKey>();
            var formListFormKeys = new List<FormKey>();

            ProcessStringFilters(
                filter.StringFilters,
                linkCache,
                cachedNpcs,
                npcFilters,
                keywordFilters,
                knownVirtualKeywords,
                logger);

            var resolvedFormEditorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ProcessFormFilters(
                filter.FormFilters,
                linkCache,
                npcFilters,
                factionFilters,
                raceFilters,
                classFormKeys,
                combatStyleFormKeys,
                outfitFilterFormKeys,
                perkFormKeys,
                voiceTypeFormKeys,
                locationFormKeys,
                formListFormKeys,
                resolvedFormEditorIds,
                logger);

            var rawStringFilters = ExtractUnresolvableStringFilters(filter.StringFilters, keywordFilters);
            var rawFormFilters = ExtractUnresolvableFormFilters(filter.FormFilters, resolvedFormEditorIds);

            var entry = new DistributionEntry
            {
                Type = DistributionType.Keyword,
                KeywordToDistribute = keywordToDistribute,
                NpcFilters = npcFilters,
                KeywordFilters = keywordFilters,
                FactionFilters = factionFilters,
                RaceFilters = raceFilters,
                ClassFormKeys = classFormKeys,
                CombatStyleFormKeys = combatStyleFormKeys,
                OutfitFilterFormKeys = outfitFilterFormKeys,
                PerkFormKeys = perkFormKeys,
                VoiceTypeFormKeys = voiceTypeFormKeys,
                LocationFormKeys = locationFormKeys,
                FormListFormKeys = formListFormKeys,
                TraitFilters = filter.TraitFilters,
                LevelFilters = filter.LevelFilters,
                RawStringFilters = rawStringFilters,
                RawFormFilters = rawFormFilters
            };

            if (filter.Chance != 100)
            {
                entry.Chance = filter.Chance;
            }

            return entry;
        }
        catch (Exception ex)
        {
            logger?.Debug(ex, "Failed to resolve keyword SPID filter: {Line}", filter.RawLine);
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
        List<FormKeyFilter> npcFilters,
        List<KeywordFilter> keywordFilters,
        IReadOnlySet<string>? knownVirtualKeywords,
        ILogger? logger)
    {
        foreach (var expr in stringFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.HasWildcard)
                {
                    continue;
                }

                var keyword = linkCache.WinningOverrides<IKeywordGetter>()
                    .FirstOrDefault(k => string.Equals(k.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (keyword != null)
                {
                    keywordFilters.Add(new KeywordFilter(keyword.EditorID ?? part.Value, part.IsNegated));
                    continue;
                }

                if (knownVirtualKeywords != null && knownVirtualKeywords.Contains(part.Value))
                {
                    keywordFilters.Add(new KeywordFilter(part.Value, part.IsNegated));
                    continue;
                }

                var npc = cachedNpcs.FirstOrDefault(n =>
                    string.Equals(n.EditorID, part.Value, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Name?.String, part.Value, StringComparison.OrdinalIgnoreCase));
                if (npc != null)
                {
                    npcFilters.Add(new FormKeyFilter(npc.FormKey, part.IsNegated));
                    logger?.Debug("Resolved NPC string filter '{Value}' to {FormKey}", part.Value, npc.FormKey);
                    continue;
                }

                keywordFilters.Add(new KeywordFilter(part.Value, part.IsNegated));
                logger?.Verbose("Treating unresolved string filter as keyword: {Value}", part.Value);
            }
        }

        foreach (var exclusion in stringFilters.GlobalExclusions)
        {
            if (exclusion.HasWildcard)
            {
                continue;
            }

            var keyword = linkCache.WinningOverrides<IKeywordGetter>()
                .FirstOrDefault(k => string.Equals(k.EditorID, exclusion.Value, StringComparison.OrdinalIgnoreCase));
            if (keyword != null)
            {
                keywordFilters.Add(new KeywordFilter(keyword.EditorID ?? exclusion.Value, true));
                continue;
            }

            if (knownVirtualKeywords != null && knownVirtualKeywords.Contains(exclusion.Value))
            {
                keywordFilters.Add(new KeywordFilter(exclusion.Value, true));
                continue;
            }

            keywordFilters.Add(new KeywordFilter(exclusion.Value, true));
            logger?.Verbose("Treating global exclusion as excluded keyword: {Value}", exclusion.Value);
        }
    }

    private static void ProcessFormFilters(
        SpidFilterSection formFilters,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        List<FormKeyFilter> npcFilters,
        List<FormKeyFilter> factionFilters,
        List<FormKeyFilter> raceFilters,
        List<FormKey> classFormKeys,
        List<FormKey> combatStyleFormKeys,
        List<FormKey> outfitFilterFormKeys,
        List<FormKey> perkFormKeys,
        List<FormKey> voiceTypeFormKeys,
        List<FormKey> locationFormKeys,
        List<FormKey> formListFormKeys,
        HashSet<string>? resolvedEditorIds,
        ILogger? logger)
    {
        foreach (var expr in formFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (TryResolveFormFilterByFormKey(part, linkCache, npcFilters, factionFilters, raceFilters,
                        resolvedEditorIds, logger))
                {
                    continue;
                }

                var faction = linkCache.WinningOverrides<IFactionGetter>()
                    .FirstOrDefault(f => string.Equals(f.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (faction != null)
                {
                    factionFilters.Add(new FormKeyFilter(faction.FormKey, part.IsNegated));
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                var race = linkCache.WinningOverrides<IRaceGetter>()
                    .FirstOrDefault(r => string.Equals(r.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (race != null)
                {
                    raceFilters.Add(new FormKeyFilter(race.FormKey, part.IsNegated));
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                if (part.IsNegated)
                {
                    continue;
                }

                var classRecord = linkCache.WinningOverrides<IClassGetter>()
                    .FirstOrDefault(c => string.Equals(c.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (classRecord != null)
                {
                    classFormKeys.Add(classRecord.FormKey);
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                var combatStyle = linkCache.WinningOverrides<ICombatStyleGetter>()
                    .FirstOrDefault(cs => string.Equals(cs.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (combatStyle != null)
                {
                    combatStyleFormKeys.Add(combatStyle.FormKey);
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                var outfitFilter = linkCache.WinningOverrides<IOutfitGetter>()
                    .FirstOrDefault(o => string.Equals(o.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (outfitFilter != null)
                {
                    outfitFilterFormKeys.Add(outfitFilter.FormKey);
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                var perk = linkCache.WinningOverrides<IPerkGetter>()
                    .FirstOrDefault(p => string.Equals(p.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (perk != null)
                {
                    perkFormKeys.Add(perk.FormKey);
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                var voiceType = linkCache.WinningOverrides<IVoiceTypeGetter>()
                    .FirstOrDefault(v => string.Equals(v.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (voiceType != null)
                {
                    voiceTypeFormKeys.Add(voiceType.FormKey);
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                var location = linkCache.WinningOverrides<ILocationGetter>()
                    .FirstOrDefault(l => string.Equals(l.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (location != null)
                {
                    locationFormKeys.Add(location.FormKey);
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                var formList = linkCache.WinningOverrides<IFormListGetter>()
                    .FirstOrDefault(fl => string.Equals(fl.EditorID, part.Value, StringComparison.OrdinalIgnoreCase));
                if (formList != null)
                {
                    formListFormKeys.Add(formList.FormKey);
                    resolvedEditorIds?.Add(part.Value);
                    continue;
                }

                logger?.Debug("Could not resolve form filter: {Value}", part.Value);
            }
        }

        foreach (var exclusion in formFilters.GlobalExclusions)
        {
            var exclusionPart = new SpidFilterPart { Value = exclusion.Value, IsNegated = true };
            if (TryResolveFormFilterByFormKey(exclusionPart, linkCache, npcFilters, factionFilters, raceFilters,
                    resolvedEditorIds, logger))
            {
                continue;
            }

            var faction = linkCache.WinningOverrides<IFactionGetter>()
                .FirstOrDefault(f => string.Equals(f.EditorID, exclusion.Value, StringComparison.OrdinalIgnoreCase));
            if (faction != null)
            {
                factionFilters.Add(new FormKeyFilter(faction.FormKey, true));
                resolvedEditorIds?.Add(exclusion.Value);
                continue;
            }

            var race = linkCache.WinningOverrides<IRaceGetter>()
                .FirstOrDefault(r => string.Equals(r.EditorID, exclusion.Value, StringComparison.OrdinalIgnoreCase));
            if (race != null)
            {
                raceFilters.Add(new FormKeyFilter(race.FormKey, true));
                resolvedEditorIds?.Add(exclusion.Value);
            }
        }
    }

    private static bool TryResolveFormFilterByFormKey(
        SpidFilterPart part,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        List<FormKeyFilter> npcFilters,
        List<FormKeyFilter> factionFilters,
        List<FormKeyFilter> raceFilters,
        HashSet<string>? resolvedEditorIds,
        ILogger? logger)
    {
        if (!TryParseAsFormKey(part.Value, out var formKey))
        {
            return false;
        }

        if (linkCache.TryResolve<INpcGetter>(formKey, out var npc))
        {
            npcFilters.Add(new FormKeyFilter(formKey, part.IsNegated));
            resolvedEditorIds?.Add(part.Value);
            logger?.Debug("Resolved FormID {Value} as NPC: {EditorId}", part.Value, npc.EditorID);
            return true;
        }

        if (linkCache.TryResolve<IFactionGetter>(formKey, out var faction))
        {
            factionFilters.Add(new FormKeyFilter(formKey, part.IsNegated));
            resolvedEditorIds?.Add(part.Value);
            logger?.Debug("Resolved FormID {Value} as Faction: {EditorId}", part.Value, faction.EditorID);
            return true;
        }

        if (linkCache.TryResolve<IRaceGetter>(formKey, out var race))
        {
            raceFilters.Add(new FormKeyFilter(formKey, part.IsNegated));
            resolvedEditorIds?.Add(part.Value);
            logger?.Debug("Resolved FormID {Value} as Race: {EditorId}", part.Value, race.EditorID);
            return true;
        }

        return false;
    }

    private static bool TryParseAsFormKey(string value, out FormKey formKey)
    {
        formKey = FormKey.Null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (FormKey.TryFactory(value, out formKey) || FormKeyHelper.TryParse(value, out formKey))
        {
            return true;
        }

        if (FormKeyHelper.TryParseFormId(value, out var id))
        {
            formKey = new FormKey(FormKeyHelper.SkyrimModKey, id);
            return true;
        }

        return false;
    }

    private static string? ExtractUnresolvableStringFilters(
        SpidFilterSection stringFilters,
        List<KeywordFilter> resolvedKeywordFilters)
    {
        var resolvedSet = new HashSet<string>(
            resolvedKeywordFilters.Where(k => k.IsExcluded).Select(k => k.EditorId),
            StringComparer.OrdinalIgnoreCase);
        var unresolvableParts = new List<string>();

        foreach (var expr in stringFilters.Expressions)
        {
            var exprParts = new List<string>();
            foreach (var part in expr.Parts)
            {
                if (part.HasWildcard)
                {
                    exprParts.Add(part.Value);
                }
                else if (part.IsNegated && !resolvedSet.Contains(part.Value))
                {
                    exprParts.Add($"-{part.Value}");
                }
            }

            if (exprParts.Count > 0)
            {
                unresolvableParts.Add(string.Join("+", exprParts));
            }
        }

        foreach (var exclusion in stringFilters.GlobalExclusions)
        {
            if (!resolvedSet.Contains(exclusion.Value))
            {
                unresolvableParts.Add($"-{exclusion.Value}");
            }
        }

        return unresolvableParts.Count > 0 ? string.Join(",", unresolvableParts) : null;
    }

    private static string? ExtractUnresolvableFormFilters(
        SpidFilterSection formFilters,
        HashSet<string> resolvedEditorIds)
    {
        var unresolvableParts = new List<string>();

        foreach (var expr in formFilters.Expressions)
        {
            var exprParts = new List<string>();
            foreach (var part in expr.Parts)
            {
                if (resolvedEditorIds.Contains(part.Value))
                {
                    continue;
                }

                var prefix = part.IsNegated ? "-" : string.Empty;
                exprParts.Add($"{prefix}{part.Value}");
            }

            if (exprParts.Count > 0)
            {
                unresolvableParts.Add(string.Join("+", exprParts));
            }
        }

        foreach (var exclusion in formFilters.GlobalExclusions)
        {
            if (!resolvedEditorIds.Contains(exclusion.Value))
            {
                unresolvableParts.Add($"-{exclusion.Value}");
            }
        }

        return unresolvableParts.Count > 0 ? string.Join(",", unresolvableParts) : null;
    }
}
