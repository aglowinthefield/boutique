using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

public static class DistributionLineParser
{
    public static List<FormKey> ExtractNpcFormKeysFromLine(
        DistributionFileViewModel file,
        DistributionLine line,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter>? npcByEditorId = null,
        Dictionary<string, INpcGetter>? npcByName = null)
    {
        return file.TypeDisplay switch
        {
            "SkyPatcher" => ExtractNpcFormKeysFromSkyPatcherLine(line.RawText, linkCache, npcByEditorId),
            "SPID" => ExtractNpcFormKeysFromSpidLine(line.RawText, linkCache, npcByEditorId, npcByName),
            _ => []
        };
    }

    private static List<FormKey> ExtractNpcFormKeysFromSkyPatcherLine(
        string rawText,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter>? npcByEditorId = null)
    {
        var results = new List<FormKey>();
        var trimmed = rawText.Trim();

        // Parse filterByNpcs= (explicit FormKeys)
        results.AddRange(ExtractFormKeysFromFilter(trimmed, "filterByNpcs="));

        // Parse filterByEditorIdContains/Or filters (partial EditorID matching)
        var editorIdFilters = ExtractEditorIdFilters(trimmed);
        if (editorIdFilters.IncludePatterns.Count > 0 || editorIdFilters.IncludeOrPatterns.Count > 0)
        {
            npcByEditorId ??= linkCache.WinningOverrides<INpcGetter>()
                .Where(n => !string.IsNullOrWhiteSpace(n.EditorID))
                .GroupBy(n => n.EditorID!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var (editorId, npc) in npcByEditorId)
            {
                if (NpcMatchesEditorIdFilters(editorId, editorIdFilters))
                {
                    results.Add(npc.FormKey);
                }
            }
        }

        return results;
    }

    private static List<FormKey> ExtractFormKeysFromFilter(string line, string filterName)
    {
        var results = new List<FormKey>();
        var filterIndex = line.IndexOf(filterName, StringComparison.OrdinalIgnoreCase);

        if (filterIndex >= 0)
        {
            var start = filterIndex + filterName.Length;
            var end = line.IndexOf(':', start);
            if (end < 0) end = line.Length;

            if (end > start)
            {
                var filterValue = line.Substring(start, end - start);
                foreach (var part in filterValue.Split(','))
                {
                    var formKey = TryParseFormKey(part.Trim());
                    if (formKey.HasValue)
                    {
                        results.Add(formKey.Value);
                    }
                }
            }
        }

        return results;
    }

    private record EditorIdFilterSet(
        List<string> IncludePatterns,
        List<string> IncludeOrPatterns,
        List<string> ExcludePatterns);

    private static EditorIdFilterSet ExtractEditorIdFilters(string line)
    {
        var includePatterns = new List<string>();
        var includeOrPatterns = new List<string>();
        var excludePatterns = new List<string>();

        // filterByEditorIdContains= (AND logic)
        includePatterns.AddRange(ExtractFilterValues(line, "filterByEditorIdContains="));

        // filterByEditorIdContainsOr= (OR logic)
        includeOrPatterns.AddRange(ExtractFilterValues(line, "filterByEditorIdContainsOr="));

        // filterByEditorIdContainsExcluded= (exclusion)
        excludePatterns.AddRange(ExtractFilterValues(line, "filterByEditorIdContainsExcluded="));

        return new EditorIdFilterSet(includePatterns, includeOrPatterns, excludePatterns);
    }

    private static List<string> ExtractFilterValues(string line, string filterName)
    {
        var results = new List<string>();
        var filterIndex = line.IndexOf(filterName, StringComparison.OrdinalIgnoreCase);

        if (filterIndex >= 0)
        {
            var start = filterIndex + filterName.Length;
            var end = line.IndexOf(':', start);
            if (end < 0) end = line.Length;

            if (end > start)
            {
                var filterValue = line.Substring(start, end - start);
                foreach (var part in filterValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmedPart = part.Trim();
                    if (!string.IsNullOrEmpty(trimmedPart))
                    {
                        results.Add(trimmedPart);
                    }
                }
            }
        }

        return results;
    }

    private static bool NpcMatchesEditorIdFilters(string editorId, EditorIdFilterSet filters)
    {
        // Check exclusions first - if any match, NPC is excluded
        foreach (var pattern in filters.ExcludePatterns)
        {
            if (editorId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Check AND filters - all must match
        if (filters.IncludePatterns.Count > 0)
        {
            foreach (var pattern in filters.IncludePatterns)
            {
                if (!editorId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        // Check OR filters - at least one must match
        if (filters.IncludeOrPatterns.Count > 0)
        {
            foreach (var pattern in filters.IncludeOrPatterns)
            {
                if (editorId.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        return false;
    }

    private static List<FormKey> ExtractNpcFormKeysFromSpidLine(
        string rawText,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter>? npcByEditorId,
        Dictionary<string, INpcGetter>? npcByName)
    {
        var results = new List<FormKey>();

        // Use SpidLineParser for robust SPID parsing
        if (!SpidLineParser.TryParse(rawText, out var filter) || filter == null)
        {
            return results;
        }

        // Get specific NPC identifiers from the parsed filter
        var npcIdentifiers = SpidLineParser.GetSpecificNpcIdentifiers(filter);
        if (npcIdentifiers.Count == 0)
        {
            return results;
        }

        // Build lookup dictionaries if not provided
        if (npcByEditorId == null || npcByName == null)
        {
            var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
            npcByEditorId ??= allNpcs
                .Where(n => !string.IsNullOrWhiteSpace(n.EditorID))
                .GroupBy(n => n.EditorID!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            npcByName ??= allNpcs
                .Where(n => !string.IsNullOrWhiteSpace(n.Name?.String))
                .GroupBy(n => n.Name!.String!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        // Resolve each NPC identifier to a FormKey
        foreach (var identifier in npcIdentifiers)
        {
            INpcGetter? npc = null;
            if (npcByEditorId.TryGetValue(identifier, out var npcById))
            {
                npc = npcById;
            }
            else if (npcByName.TryGetValue(identifier, out var npcByNameMatch))
            {
                npc = npcByNameMatch;
            }

            if (npc != null)
            {
                results.Add(npc.FormKey);
            }
        }

        return results;
    }

    public static bool LineTargetsAllNpcs(DistributionFileViewModel file, DistributionLine line)
    {
        return file.TypeDisplay switch
        {
            "SPID" => SpidLineTargetsAllNpcs(line.RawText),
            "SkyPatcher" => SkyPatcherLineTargetsAllNpcs(line.RawText),
            _ => false
        };
    }

    private static bool SpidLineTargetsAllNpcs(string rawText)
    {
        if (!SpidLineParser.TryParse(rawText, out var filter) || filter == null)
            return false;

        return filter.TargetsAllNpcs;
    }

    private static bool SkyPatcherLineTargetsAllNpcs(string rawText)
    {
        var trimmed = rawText.Trim();

        // Must have an outfit assignment
        var hasOutfitDefault = trimmed.Contains("outfitDefault=", StringComparison.OrdinalIgnoreCase) ||
                               trimmed.Contains("outfitSleep=", StringComparison.OrdinalIgnoreCase);
        if (!hasOutfitDefault)
            return false;

        // Check for any NPC-specific filters - if none present, it targets all NPCs
        var hasNpcFilter = trimmed.Contains("filterByNpcs=", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.Contains("filterByNpcsExcluded=", StringComparison.OrdinalIgnoreCase);
        var hasFactionFilter = trimmed.Contains("filterByFactions", StringComparison.OrdinalIgnoreCase);
        var hasRaceFilter = trimmed.Contains("filterByRaces", StringComparison.OrdinalIgnoreCase);
        var hasKeywordFilter = trimmed.Contains("filterByKeywords", StringComparison.OrdinalIgnoreCase);
        var hasEditorIdFilter = trimmed.Contains("filterByEditorIdContains", StringComparison.OrdinalIgnoreCase);
        var hasGenderFilter = trimmed.Contains("filterByGender=", StringComparison.OrdinalIgnoreCase);
        var hasDefaultOutfitFilter = trimmed.Contains("filterByDefaultOutfits=", StringComparison.OrdinalIgnoreCase);
        var hasModNameFilter = trimmed.Contains("filterByModNames=", StringComparison.OrdinalIgnoreCase);

        // If no NPC-related filters are present, it targets all NPCs
        return !hasNpcFilter && !hasFactionFilter && !hasRaceFilter && !hasKeywordFilter &&
               !hasEditorIdFilter && !hasGenderFilter && !hasDefaultOutfitFilter && !hasModNameFilter;
    }

    public static string? ExtractOutfitNameFromLine(
        DistributionLine line,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        foreach (var formKeyString in line.OutfitFormKeys)
        {
            var formKey = TryParseFormKey(formKeyString);
            if (formKey.HasValue && linkCache.TryResolve<IOutfitGetter>(formKey.Value, out var outfit))
            {
                return outfit.EditorID ?? outfit.FormKey.ToString();
            }
        }
        return null;
    }

    private static FormKey? TryParseFormKey(string text) =>
        FormKeyHelper.TryParse(text, out var formKey) ? formKey : null;
}
