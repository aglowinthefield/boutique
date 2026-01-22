using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;

namespace Boutique.Services;

public class SpidFilterMatchingService
{
    private static bool NpcMatchesFilter(NpcFilterData npc, SpidDistributionFilter filter) =>
        NpcMatchesFilter(npc, filter, null);

    private static bool NpcMatchesFilter(
        NpcFilterData npc,
        SpidDistributionFilter filter,
        IReadOnlySet<string>? virtualKeywords) =>
        MatchesStringFilters(npc, filter.StringFilters, virtualKeywords) &&
        MatchesFormFilters(npc, filter.FormFilters) &&
        MatchesLevelFilters(npc, filter.LevelFilters) &&
        MatchesTraitFilters(npc, filter.TraitFilters);

    public static IReadOnlyList<NpcFilterData> GetMatchingNpcs(IReadOnlyList<NpcFilterData> allNpcs,
        SpidDistributionFilter filter)
    {
        if (filter.TargetsAllNpcs && filter.StringFilters.IsEmpty && filter.FormFilters.IsEmpty &&
            filter.TraitFilters.IsEmpty && string.IsNullOrWhiteSpace(filter.LevelFilters))
        {
            return allNpcs.ToList();
        }

        return allNpcs.AsParallel().Where(npc => NpcMatchesFilter(npc, filter)).ToList();
    }

    public static IReadOnlyList<NpcFilterData> GetMatchingNpcsWithVirtualKeywords(
        IReadOnlyList<NpcFilterData> allNpcs,
        SpidDistributionFilter filter,
        Dictionary<FormKey, HashSet<string>> virtualKeywordsByNpc)
    {
        if (filter.TargetsAllNpcs && filter.StringFilters.IsEmpty && filter.FormFilters.IsEmpty &&
            filter.TraitFilters.IsEmpty && string.IsNullOrWhiteSpace(filter.LevelFilters))
        {
            return allNpcs.ToList();
        }

        return allNpcs.AsParallel().Where(npc =>
        {
            virtualKeywordsByNpc.TryGetValue(npc.FormKey, out var virtualKeywords);
            return NpcMatchesFilter(npc, filter, virtualKeywords);
        }).ToList();
    }

    private static bool MatchesStringFilters(
        NpcFilterData npc,
        SpidFilterSection filters,
        IReadOnlySet<string>? virtualKeywords)
    {
        if (filters.IsEmpty)
        {
            return true;
        }

        foreach (var exclusion in filters.GlobalExclusions)
        {
            if (MatchesStringPart(
                    npc,
                    new SpidFilterPart { Value = exclusion.Value, IsNegated = false },
                    virtualKeywords))
            {
                return false;
            }
        }

        if (filters.Expressions.Count == 0)
        {
            return true;
        }

        return filters.Expressions.Any(e => MatchesStringExpression(npc, e, virtualKeywords));
    }

    private static bool MatchesStringExpression(
        NpcFilterData npc,
        SpidFilterExpression expression,
        IReadOnlySet<string>? virtualKeywords) =>
        expression.Parts.All(part => MatchesStringPart(npc, part, virtualKeywords));

    private static bool MatchesStringPart(NpcFilterData npc, SpidFilterPart part, IReadOnlySet<string>? virtualKeywords)
    {
        var matches = part.HasWildcard
            ? PartialMatchesNpcStrings(npc, part.Value.Replace("*", string.Empty), virtualKeywords)
            : ExactMatchesNpcStrings(npc, part.Value, virtualKeywords);

        return part.IsNegated ? !matches : matches;
    }

    private static bool
        ExactMatchesNpcStrings(NpcFilterData npc, string value, IReadOnlySet<string>? virtualKeywords) =>
        (!string.IsNullOrWhiteSpace(npc.Name) && npc.Name.Equals(value, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(npc.EditorId) && npc.EditorId.Equals(value, StringComparison.OrdinalIgnoreCase)) ||
        npc.Keywords.Contains(value) ||
        (virtualKeywords?.Contains(value) ?? false) ||
        (!string.IsNullOrWhiteSpace(npc.TemplateEditorId) &&
         npc.TemplateEditorId.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool
        PartialMatchesNpcStrings(NpcFilterData npc, string value, IReadOnlySet<string>? virtualKeywords) =>
        (!string.IsNullOrWhiteSpace(npc.Name) && npc.Name.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(npc.EditorId) &&
         npc.EditorId.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
        npc.Keywords.Any(k => k.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
        (virtualKeywords?.Any(k => k.Contains(value, StringComparison.OrdinalIgnoreCase)) ?? false) ||
        (!string.IsNullOrWhiteSpace(npc.TemplateEditorId) &&
         npc.TemplateEditorId.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesFormFilters(NpcFilterData npc, SpidFilterSection filters)
    {
        if (filters.IsEmpty)
        {
            return true;
        }

        if (filters.GlobalExclusions.Any(e => MatchesFormValue(npc, e.Value)))
        {
            return false;
        }

        if (filters.Expressions.Count == 0)
        {
            return true;
        }

        return filters.Expressions.Any(e => MatchesFormExpression(npc, e));
    }

    private static bool MatchesFormExpression(NpcFilterData npc, SpidFilterExpression expression) =>
        expression.Parts.All(part => MatchesFormPart(npc, part));

    private static bool MatchesFormPart(NpcFilterData npc, SpidFilterPart part)
    {
        var matches = MatchesFormValue(npc, part.Value);
        return part.IsNegated ? !matches : matches;
    }

    private static bool MatchesFormValue(NpcFilterData npc, string value)
    {
        if (FormKeyHelper.IsModKeyFileName(value))
        {
            return string.Equals(npc.SourceMod.FileName, value, StringComparison.OrdinalIgnoreCase);
        }

        if (TryParseAsFormKey(value, out var formKey))
        {
            return npc.FormKey == formKey ||
                   npc.RaceFormKey == formKey ||
                   npc.ClassFormKey == formKey ||
                   npc.CombatStyleFormKey == formKey ||
                   npc.VoiceTypeFormKey == formKey ||
                   npc.DefaultOutfitFormKey == formKey ||
                   npc.TemplateFormKey == formKey ||
                   npc.Factions.Any(f => f.FactionFormKey == formKey);
        }

        return MatchesEditorId(npc.EditorId, value) ||
               MatchesEditorId(npc.RaceEditorId, value) ||
               MatchesEditorId(npc.ClassEditorId, value) ||
               MatchesEditorId(npc.CombatStyleEditorId, value) ||
               MatchesEditorId(npc.VoiceTypeEditorId, value) ||
               MatchesEditorId(npc.DefaultOutfitEditorId, value) ||
               MatchesEditorId(npc.TemplateEditorId, value) ||
               npc.Factions.Any(f => MatchesEditorId(f.FactionEditorId, value));
    }

    private static bool MatchesEditorId(string? editorId, string value) =>
        !string.IsNullOrWhiteSpace(editorId) && editorId.Equals(value, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseAsFormKey(string value, out FormKey formKey)
    {
        formKey = FormKey.Null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return FormKey.TryFactory(value, out formKey) || FormKeyHelper.TryParse(value, out formKey);
    }

    private static bool MatchesLevelFilters(NpcFilterData npc, string? levelFilters)
    {
        if (string.IsNullOrWhiteSpace(levelFilters) ||
            levelFilters.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = levelFilters.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();

            if (TryParseSkillFilter(trimmed, out var skillIndex, out var minSkill, out var maxSkill))
            {
                if (!MatchesSkillFilter(npc, skillIndex, minSkill, maxSkill))
                {
                    return false;
                }

                continue;
            }

            if (!ParseLevelRange(trimmed, out var minLevel, out var maxLevel))
            {
                continue;
            }

            if (npc.Level < minLevel)
            {
                return false;
            }

            if (maxLevel.HasValue && npc.Level > maxLevel.Value)
            {
                return false;
            }
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
        {
            return false;
        }

        var indexPart = value[..openParen].Trim();
        var rangePart = value[(openParen + 1)..closeParen].Trim();

        if (!int.TryParse(indexPart, out skillIndex))
        {
            return false;
        }

        if (skillIndex is < 6 or > 23)
        {
            return false;
        }

        return ParseLevelRange(rangePart, out minSkill, out maxSkill);
    }

    private static bool MatchesSkillFilter(NpcFilterData npc, int skillIndex, int minSkill, int? maxSkill)
    {
        if (skillIndex < 0 || skillIndex >= npc.SkillValues.Length)
        {
            return true;
        }

        var skillValue = npc.SkillValues[skillIndex];

        if (skillValue < minSkill)
        {
            return false;
        }

        if (maxSkill.HasValue && skillValue > maxSkill.Value)
        {
            return false;
        }

        return true;
    }

    private static bool ParseLevelRange(string value, out int minLevel, out int? maxLevel)
    {
        maxLevel = null;

        var slashIndex = value.IndexOf('/');

        if (slashIndex < 0)
        {
            return int.TryParse(value, out minLevel);
        }

        var minPart = value[..slashIndex];
        var maxPart = slashIndex < value.Length - 1 ? value[(slashIndex + 1)..] : null;

        if (!int.TryParse(minPart, out minLevel))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(maxPart) && int.TryParse(maxPart, out var max))
        {
            maxLevel = max;
        }

        return true;
    }

    private static bool MatchesTraitFilters(NpcFilterData npc, SpidTraitFilters traits)
    {
        if (traits.IsEmpty)
        {
            return true;
        }

        if (traits.IsFemale.HasValue && npc.IsFemale != traits.IsFemale.Value)
        {
            return false;
        }

        if (traits.IsUnique.HasValue && npc.IsUnique != traits.IsUnique.Value)
        {
            return false;
        }

        if (traits.IsSummonable.HasValue && npc.IsSummonable != traits.IsSummonable.Value)
        {
            return false;
        }

        if (traits.IsChild.HasValue && npc.IsChild != traits.IsChild.Value)
        {
            return false;
        }

        if (traits.IsLeveled.HasValue && npc.IsLeveled != traits.IsLeveled.Value)
        {
            return false;
        }

        return true;
    }
}
