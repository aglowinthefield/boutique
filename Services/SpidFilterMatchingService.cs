using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;

namespace Boutique.Services;

public class SpidFilterMatchingService
{
    private static bool NpcMatchesFilter(NpcFilterData npc, SpidDistributionFilter filter) =>
        NpcMatchesFilter(npc, filter, virtualKeywords: null);

    private static bool NpcMatchesFilter(NpcFilterData npc, SpidDistributionFilter filter, IReadOnlySet<string>? virtualKeywords)
    {
        // All filter sections are multiplicative (AND logic)
        // An empty/NONE filter section means "match all"

        // 1. Check string filters (keywords, names, EditorIDs)
        if (!MatchesStringFilters(npc, filter.StringFilters, virtualKeywords))
            return false;

        // 2. Check form filters (race, class, faction, etc.)
        if (!MatchesFormFilters(npc, filter.FormFilters))
            return false;

        // 3. Check level filters
        if (!MatchesLevelFilters(npc, filter.LevelFilters))
            return false;

        // 4. Check trait filters (gender, unique, etc.)
        if (!MatchesTraitFilters(npc, filter.TraitFilters))
            return false;

        return true;
    }

    public static IReadOnlyList<NpcFilterData> GetMatchingNpcs(IReadOnlyList<NpcFilterData> allNpcs, SpidDistributionFilter filter)
    {
        // Fast path: if filter targets all NPCs with no exclusions, return all
        if (filter.TargetsAllNpcs && filter.StringFilters.IsEmpty && filter.FormFilters.IsEmpty &&
            filter.TraitFilters.IsEmpty && string.IsNullOrWhiteSpace(filter.LevelFilters))
        {
            return allNpcs.ToList();
        }

        // Use PLINQ for parallel filtering of large NPC lists
        return allNpcs.AsParallel().Where(npc => NpcMatchesFilter(npc, filter)).ToList();
    }

    public static IReadOnlyList<NpcFilterData> GetMatchingNpcsWithVirtualKeywords(
        IReadOnlyList<NpcFilterData> allNpcs,
        SpidDistributionFilter filter,
        Dictionary<FormKey, HashSet<string>> virtualKeywordsByNpc)
    {
        // Fast path: if filter targets all NPCs with no exclusions, return all
        if (filter.TargetsAllNpcs && filter.StringFilters.IsEmpty && filter.FormFilters.IsEmpty &&
            filter.TraitFilters.IsEmpty && string.IsNullOrWhiteSpace(filter.LevelFilters))
        {
            return allNpcs.ToList();
        }

        // Use PLINQ for parallel filtering of large NPC lists
        return allNpcs.AsParallel().Where(npc =>
        {
            virtualKeywordsByNpc.TryGetValue(npc.FormKey, out var virtualKeywords);
            return NpcMatchesFilter(npc, filter, virtualKeywords);
        }).ToList();
    }

    private static bool MatchesStringFilters(NpcFilterData npc, SpidFilterSection filters, IReadOnlySet<string>? virtualKeywords)
    {
        if (filters.IsEmpty)
            return true;

        // OR logic: at least one expression must match
        foreach (var expression in filters.Expressions)
        {
            if (MatchesStringExpression(npc, expression, virtualKeywords))
                return true;
        }

        return false;
    }

    private static bool MatchesStringExpression(NpcFilterData npc, SpidFilterExpression expression, IReadOnlySet<string>? virtualKeywords)
    {
        // AND logic: all parts must match
        foreach (var part in expression.Parts)
        {
            if (!MatchesStringPart(npc, part, virtualKeywords))
                return false;
        }

        return true;
    }

    private static bool MatchesStringPart(NpcFilterData npc, SpidFilterPart part, IReadOnlySet<string>? virtualKeywords)
    {
        var value = part.Value;
        var hasWildcard = part.HasWildcard;

        bool matches;

        if (hasWildcard)
        {
            // Partial match - remove * and check if any string contains the value
            var searchValue = value.Replace("*", "");
            matches = PartialMatchesNpcStrings(npc, searchValue, virtualKeywords);
        }
        else
        {
            // Exact match against name, EditorID, or keywords
            matches = ExactMatchesNpcStrings(npc, value, virtualKeywords);
        }

        // Apply negation if needed
        return part.IsNegated ? !matches : matches;
    }

    private static bool ExactMatchesNpcStrings(NpcFilterData npc, string value, IReadOnlySet<string>? virtualKeywords)
    {
        // Check NPC name
        if (!string.IsNullOrWhiteSpace(npc.Name) &&
            npc.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check EditorID
        if (!string.IsNullOrWhiteSpace(npc.EditorId) &&
            npc.EditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check keywords (NPC + race keywords are already combined)
        if (npc.Keywords.Contains(value))
            return true;

        // Check virtual keywords (SPID-distributed keywords)
        if (virtualKeywords != null && virtualKeywords.Contains(value))
            return true;

        // Check template EditorID
        if (!string.IsNullOrWhiteSpace(npc.TemplateEditorId) &&
            npc.TemplateEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool PartialMatchesNpcStrings(NpcFilterData npc, string value, IReadOnlySet<string>? virtualKeywords)
    {
        // Check NPC name
        if (!string.IsNullOrWhiteSpace(npc.Name) &&
            npc.Name.Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check EditorID
        if (!string.IsNullOrWhiteSpace(npc.EditorId) &&
            npc.EditorId.Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check keywords
        foreach (var keyword in npc.Keywords)
        {
            if (keyword.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check virtual keywords (SPID-distributed keywords)
        if (virtualKeywords != null)
        {
            foreach (var keyword in virtualKeywords)
            {
                if (keyword.Contains(value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Check template EditorID
        if (!string.IsNullOrWhiteSpace(npc.TemplateEditorId) &&
            npc.TemplateEditorId.Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool MatchesFormFilters(NpcFilterData npc, SpidFilterSection filters)
    {
        if (filters.IsEmpty)
            return true;

        // OR logic: at least one expression must match
        foreach (var expression in filters.Expressions)
        {
            if (MatchesFormExpression(npc, expression))
                return true;
        }

        return false;
    }

    private static bool MatchesFormExpression(NpcFilterData npc, SpidFilterExpression expression)
    {
        // AND logic: all parts must match
        foreach (var part in expression.Parts)
        {
            if (!MatchesFormPart(npc, part))
                return false;
        }

        return true;
    }

    private static bool MatchesFormPart(NpcFilterData npc, SpidFilterPart part)
    {
        var value = part.Value;

        bool matches = MatchesFormValue(npc, value);

        // Apply negation if needed
        return part.IsNegated ? !matches : matches;
    }

    private static bool MatchesFormValue(NpcFilterData npc, string value)
    {
        // Check if it's a plugin filter (ends with .esp/.esm/.esl)
        if (FormKeyHelper.IsModKeyFileName(value))
        {
            return string.Equals(npc.SourceMod.FileName, value, StringComparison.OrdinalIgnoreCase);
        }

        // Try to match as FormKey (0x12345 or ModKey|FormID)
        if (TryParseAsFormKey(value, out var formKey))
        {
            // Check specific NPC
            if (npc.FormKey == formKey)
                return true;

            // Check race
            if (npc.RaceFormKey == formKey)
                return true;

            // Check class
            if (npc.ClassFormKey == formKey)
                return true;

            // Check faction
            if (npc.Factions.Any(f => f.FactionFormKey == formKey))
                return true;

            // Check combat style
            if (npc.CombatStyleFormKey == formKey)
                return true;

            // Check voice type
            if (npc.VoiceTypeFormKey == formKey)
                return true;

            // Check outfit
            if (npc.DefaultOutfitFormKey == formKey)
                return true;

            // Check template
            if (npc.TemplateFormKey == formKey)
                return true;

            return false;
        }

        // Otherwise, match by EditorID

        // Check specific NPC by EditorID
        if (!string.IsNullOrWhiteSpace(npc.EditorId) &&
            npc.EditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check race by EditorID
        if (!string.IsNullOrWhiteSpace(npc.RaceEditorId) &&
            npc.RaceEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check class by EditorID
        if (!string.IsNullOrWhiteSpace(npc.ClassEditorId) &&
            npc.ClassEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check faction by EditorID
        if (npc.Factions.Any(f => !string.IsNullOrWhiteSpace(f.FactionEditorId) &&
            f.FactionEditorId.Equals(value, StringComparison.OrdinalIgnoreCase)))
            return true;

        // Check combat style by EditorID
        if (!string.IsNullOrWhiteSpace(npc.CombatStyleEditorId) &&
            npc.CombatStyleEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check voice type by EditorID
        if (!string.IsNullOrWhiteSpace(npc.VoiceTypeEditorId) &&
            npc.VoiceTypeEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check outfit by EditorID
        if (!string.IsNullOrWhiteSpace(npc.DefaultOutfitEditorId) &&
            npc.DefaultOutfitEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check template by EditorID
        if (!string.IsNullOrWhiteSpace(npc.TemplateEditorId) &&
            npc.TemplateEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool TryParseAsFormKey(string value, out FormKey formKey)
    {
        formKey = FormKey.Null;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Try Mutagen's built-in parser first
        if (FormKey.TryFactory(value, out formKey))
            return true;

        // Fall back to our helper which handles pipe and tilde formats
        return FormKeyHelper.TryParse(value, out formKey);
    }

    private static bool MatchesLevelFilters(NpcFilterData npc, string? levelFilters)
    {
        if (string.IsNullOrWhiteSpace(levelFilters) ||
            levelFilters.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return true;

        // Parse level filter: can be "min", "min/max", or "min/" (open-ended)
        // Can also have skill filters like "14(50/50)" meaning skill index 14 at level 50-50
        var parts = levelFilters.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            // Check for skill filter format: skillIndex(min/max)
            if (TryParseSkillFilter(trimmed, out var skillIndex, out var minSkill, out var maxSkill))
            {
                if (!MatchesSkillFilter(npc, skillIndex, minSkill, maxSkill))
                    return false;
                continue;
            }

            // Basic level range
            if (!ParseLevelRange(trimmed, out var minLevel, out var maxLevel))
                continue;

            // Check if NPC level is in range
            if (npc.Level < minLevel)
                return false;

            if (maxLevel.HasValue && npc.Level > maxLevel.Value)
                return false;
        }

        return true;
    }

    private static bool TryParseSkillFilter(string value, out int skillIndex, out int minSkill, out int? maxSkill)
    {
        skillIndex = 0;
        minSkill = 0;
        maxSkill = null;

        var openParen = value.IndexOf('(');
        var closeParen = value.IndexOf(')');

        if (openParen < 0 || closeParen < openParen)
            return false;

        var indexPart = value[..openParen].Trim();
        var rangePart = value[(openParen + 1)..closeParen].Trim();

        if (!int.TryParse(indexPart, out skillIndex))
            return false;

        // Validate skill index is in valid range (6-23 for SPID)
        if (skillIndex < 6 || skillIndex > 23)
            return false;

        return ParseLevelRange(rangePart, out minSkill, out maxSkill);
    }

    private static bool MatchesSkillFilter(NpcFilterData npc, int skillIndex, int minSkill, int? maxSkill)
    {
        if (skillIndex < 0 || skillIndex >= npc.SkillValues.Length)
            return true;

        var skillValue = npc.SkillValues[skillIndex];

        if (skillValue < minSkill)
            return false;

        if (maxSkill.HasValue && skillValue > maxSkill.Value)
            return false;

        return true;
    }

    private static bool ParseLevelRange(string value, out int minLevel, out int? maxLevel)
    {
        maxLevel = null;

        var slashIndex = value.IndexOf('/');

        if (slashIndex < 0)
        {
            // Just a minimum level
            return int.TryParse(value, out minLevel);
        }

        var minPart = value[..slashIndex];
        var maxPart = slashIndex < value.Length - 1 ? value[(slashIndex + 1)..] : null;

        if (!int.TryParse(minPart, out minLevel))
            return false;

        if (!string.IsNullOrWhiteSpace(maxPart) && int.TryParse(maxPart, out var max))
        {
            maxLevel = max;
        }

        return true;
    }

    private static bool MatchesTraitFilters(NpcFilterData npc, SpidTraitFilters traits)
    {
        if (traits.IsEmpty)
            return true;

        // Check gender
        if (traits.IsFemale.HasValue && npc.IsFemale != traits.IsFemale.Value)
            return false;

        // Check unique
        if (traits.IsUnique.HasValue && npc.IsUnique != traits.IsUnique.Value)
            return false;

        // Check summonable
        if (traits.IsSummonable.HasValue && npc.IsSummonable != traits.IsSummonable.Value)
            return false;

        // Check child
        if (traits.IsChild.HasValue && npc.IsChild != traits.IsChild.Value)
            return false;

        // Check leveled
        if (traits.IsLeveled.HasValue && npc.IsLeveled != traits.IsLeveled.Value)
            return false;

        // Note: Teammate and Dead are runtime states, can't be checked statically
        // We'll skip these for now

        return true;
    }
}
