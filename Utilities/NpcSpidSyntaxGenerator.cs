using System.Text;
using Boutique.Models;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

/// <summary>
///     Generates SPID and SkyPatcher distribution syntax from NPC filter criteria.
/// </summary>
public static class NpcSpidSyntaxGenerator
{
    /// <summary>
    ///     Generates both SPID and SkyPatcher syntax for the given filter criteria.
    /// </summary>
    /// <param name="filter">The filter criteria to convert to syntax.</param>
    /// <param name="linkCache">Link cache for resolving FormKeys to EditorIDs.</param>
    /// <param name="outfitPlaceholder">Placeholder text for the outfit (e.g., "YourOutfit" or actual EditorID).</param>
    /// <returns>A tuple containing SPID syntax and SkyPatcher syntax.</returns>
    public static (string SpidSyntax, string SkyPatcherSyntax) Generate(
        NpcSpidFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache,
        string outfitPlaceholder = "YourOutfit")
    {
        if (filter.IsEmpty)
        {
            return (
                $"; No filters active - this would target ALL NPCs\n; Outfit = {outfitPlaceholder}",
                $"; No filters active - this would target ALL NPCs\n; outfitDefault={outfitPlaceholder}");
        }

        var spidSyntax = GenerateSpidSyntax(filter, linkCache, outfitPlaceholder);
        var skyPatcherSyntax = GenerateSkyPatcherSyntax(filter, linkCache, outfitPlaceholder);

        return (spidSyntax, skyPatcherSyntax);
    }

    /// <summary>
    ///     Generates SPID distribution syntax for the given filter criteria.
    ///     SPID syntax: Outfit = FormOrEditorID|StringFilters|FormFilters|LevelFilters|TraitFilters|CountOrPackageIdx|Chance
    /// </summary>
    public static string GenerateSpidSyntax(
        NpcSpidFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache,
        string outfitPlaceholder = "YourOutfit")
    {
        var sb = new StringBuilder();
        sb.AppendLine("; SPID Syntax:");
        sb.Append("Outfit = ");
        sb.Append(outfitPlaceholder);

        // Position 2: StringFilters - Keywords (AND with +)
        var stringFilters = new List<string>();
        if (linkCache != null)
        {
            foreach (var keywordFormKey in filter.Keywords)
            {
                if (linkCache.TryResolve<IKeywordGetter>(keywordFormKey, out var keyword) &&
                    !string.IsNullOrWhiteSpace(keyword.EditorID))
                {
                    stringFilters.Add(keyword.EditorID);
                }
            }
        }

        var stringFiltersPart = stringFilters.Count > 0 ? string.Join("+", stringFilters) : null;

        // Position 3: FormFilters - Factions, Races, and Classes (AND with +)
        var formFilters = new List<string>();
        if (linkCache != null)
        {
            foreach (var factionFormKey in filter.Factions)
            {
                if (linkCache.TryResolve<IFactionGetter>(factionFormKey, out var faction) &&
                    !string.IsNullOrWhiteSpace(faction.EditorID))
                {
                    formFilters.Add(faction.EditorID);
                }
            }

            foreach (var raceFormKey in filter.Races)
            {
                if (linkCache.TryResolve<IRaceGetter>(raceFormKey, out var race) &&
                    !string.IsNullOrWhiteSpace(race.EditorID))
                {
                    formFilters.Add(race.EditorID);
                }
            }

            foreach (var classFormKey in filter.Classes)
            {
                if (linkCache.TryResolve<IClassGetter>(classFormKey, out var classRecord) &&
                    !string.IsNullOrWhiteSpace(classRecord.EditorID))
                {
                    formFilters.Add(classRecord.EditorID);
                }
            }
        }

        var formFiltersPart = formFilters.Count > 0 ? string.Join("+", formFilters) : null;

        // Position 4: LevelFilters
        string? levelFiltersPart = null;
        if (filter.MinLevel.HasValue || filter.MaxLevel.HasValue)
        {
            var min = filter.MinLevel ?? 1;
            var max = filter.MaxLevel ?? 0; // 0 means no max in SPID
            if (max > 0)
            {
                levelFiltersPart = $"{min}/{max}";
            }
            else
            {
                levelFiltersPart = $"{min}/";
            }
        }

        // Position 5: TraitFilters
        var traitFiltersPart = FormatTraitFilters(filter);

        // Position 6: CountOrPackageIdx - Not used
        string? countPart = null;

        // Position 7: Chance - Not used for filtering
        string? chancePart = null;

        // Build the line, preserving intermediate NONEs but trimming trailing ones
        var parts = new[]
        {
            stringFiltersPart, formFiltersPart, levelFiltersPart, traitFiltersPart, countPart, chancePart
        };

        // Find the last non-null position
        var lastNonNullIndex = -1;
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (parts[i] != null)
            {
                lastNonNullIndex = i;
                break;
            }
        }

        // Add all parts up to and including the last non-null one
        for (var i = 0; i <= lastNonNullIndex; i++)
        {
            sb.Append('|');
            sb.Append(parts[i] ?? "NONE");
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Generates SkyPatcher distribution syntax for the given filter criteria.
    ///     SkyPatcher syntax: filterByFactions=...:filterByKeywords=...:filterByRaces=...:filterByGender=...:outfitDefault=...
    /// </summary>
    public static string GenerateSkyPatcherSyntax(
        NpcSpidFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache = null,
        string outfitPlaceholder = "YourOutfit")
    {
        var sb = new StringBuilder();
        sb.AppendLine("; SkyPatcher Syntax:");

        var filterParts = new List<string>();

        // Faction filter
        if (filter.Factions.Count > 0)
        {
            var factionFormKeys = filter.Factions
                .Select(FormKeyHelper.Format)
                .ToList();
            filterParts.Add($"filterByFactions={string.Join(",", factionFormKeys)}");
        }

        // Keyword filter
        if (filter.Keywords.Count > 0)
        {
            var keywordFormKeys = filter.Keywords
                .Select(FormKeyHelper.Format)
                .ToList();
            filterParts.Add($"filterByKeywords={string.Join(",", keywordFormKeys)}");
        }

        // Race filter
        if (filter.Races.Count > 0)
        {
            var raceFormKeys = filter.Races
                .Select(FormKeyHelper.Format)
                .ToList();
            filterParts.Add($"filterByRaces={string.Join(",", raceFormKeys)}");
        }

        // Class filter
        if (filter.Classes.Count > 0)
        {
            var classFormKeys = filter.Classes
                .Select(FormKeyHelper.Format)
                .ToList();
            filterParts.Add($"filterByClass={string.Join(",", classFormKeys)}");
        }

        // Gender filter
        if (filter.IsFemale.HasValue)
        {
            filterParts.Add($"filterByGender={(filter.IsFemale.Value ? "female" : "male")}");
        }

        // Note: SkyPatcher doesn't have direct filters for unique, templated, child, summonable, leveled
        // These are SPID-specific traits. We'll add a comment about this.
        var unsupportedFilters = new List<string>();
        if (filter.IsUnique.HasValue)
        {
            unsupportedFilters.Add($"Unique={(filter.IsUnique.Value ? "Yes" : "No")}");
        }

        if (filter.IsTemplated.HasValue)
        {
            unsupportedFilters.Add($"Templated={(filter.IsTemplated.Value ? "Yes" : "No")}");
        }

        if (filter.IsChild.HasValue)
        {
            unsupportedFilters.Add($"Child={(filter.IsChild.Value ? "Yes" : "No")}");
        }

        if (filter.IsSummonable.HasValue)
        {
            unsupportedFilters.Add($"Summonable={(filter.IsSummonable.Value ? "Yes" : "No")}");
        }

        if (filter.IsLeveled.HasValue)
        {
            unsupportedFilters.Add($"Leveled={(filter.IsLeveled.Value ? "Yes" : "No")}");
        }

        // Add outfit
        filterParts.Add($"outfitDefault={outfitPlaceholder}");

        sb.AppendJoin(":", filterParts);

        // Add comment about unsupported filters if any
        if (unsupportedFilters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("; Note: SkyPatcher doesn't support these SPID trait filters:");
            sb.Append("; ").AppendJoin(", ", unsupportedFilters);
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Formats trait filters for SPID output.
    /// </summary>
    private static string? FormatTraitFilters(NpcSpidFilter filter)
    {
        if (!filter.HasTraitFilters)
        {
            return null;
        }

        var parts = new List<string>();

        if (filter.IsFemale == true)
        {
            parts.Add("F");
        }
        else if (filter.IsFemale == false)
        {
            parts.Add("M");
        }

        if (filter.IsUnique == true)
        {
            parts.Add("U");
        }
        else if (filter.IsUnique == false)
        {
            parts.Add("-U");
        }

        if (filter.IsSummonable == true)
        {
            parts.Add("S");
        }
        else if (filter.IsSummonable == false)
        {
            parts.Add("-S");
        }

        if (filter.IsChild == true)
        {
            parts.Add("C");
        }
        else if (filter.IsChild == false)
        {
            parts.Add("-C");
        }

        if (filter.IsLeveled == true)
        {
            parts.Add("L");
        }
        else if (filter.IsLeveled == false)
        {
            parts.Add("-L");
        }

        // Note: Templated is not a standard SPID trait filter
        // We'll add it as a comment instead
        // The -L (non-leveled) filter is close but not the same as non-templated
        return parts.Count > 0 ? string.Join("/", parts) : null;
    }

    /// <summary>
    ///     Gets a human-readable description of the active filters.
    /// </summary>
    public static string GetFilterDescription(NpcSpidFilter filter)
    {
        if (filter.IsEmpty)
        {
            return "No filters active";
        }

        var parts = new List<string>();

        if (filter.IsFemale == true)
        {
            parts.Add("Female");
        }
        else if (filter.IsFemale == false)
        {
            parts.Add("Male");
        }

        if (filter.IsUnique == true)
        {
            parts.Add("Unique");
        }
        else if (filter.IsUnique == false)
        {
            parts.Add("Non-Unique");
        }

        if (filter.IsTemplated == true)
        {
            parts.Add("Templated");
        }
        else if (filter.IsTemplated == false)
        {
            parts.Add("Non-Templated");
        }

        if (filter.IsChild == true)
        {
            parts.Add("Child");
        }
        else if (filter.IsChild == false)
        {
            parts.Add("Adult");
        }

        if (filter.IsSummonable == true)
        {
            parts.Add("Summonable");
        }
        else if (filter.IsSummonable == false)
        {
            parts.Add("Non-Summonable");
        }

        if (filter.IsLeveled == true)
        {
            parts.Add("Leveled");
        }
        else if (filter.IsLeveled == false)
        {
            parts.Add("Non-Leveled");
        }

        if (filter.Factions.Count > 0)
        {
            parts.Add($"{filter.Factions.Count} faction(s)");
        }

        if (filter.Races.Count > 0)
        {
            parts.Add($"{filter.Races.Count} race(s)");
        }

        if (filter.Keywords.Count > 0)
        {
            parts.Add($"{filter.Keywords.Count} keyword(s)");
        }

        if (filter.Classes.Count > 0)
        {
            parts.Add($"{filter.Classes.Count} class(es)");
        }

        if (filter.MinLevel.HasValue || filter.MaxLevel.HasValue)
        {
            var min = filter.MinLevel ?? 1;
            var max = filter.MaxLevel;
            if (max.HasValue)
            {
                parts.Add($"Level {min}-{max}");
            }
            else
            {
                parts.Add($"Level {min}+");
            }
        }

        return string.Join(", ", parts);
    }
}
