using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;

namespace Boutique.Services;

public class FilterMatchingService
{
  public static bool NpcMatchesFilterForBatch(
    NpcFilterData npc,
    SpidDistributionFilter filter,
    IReadOnlySet<string>? virtualKeywords) =>
    NpcMatchesFilter(npc, filter, virtualKeywords);

  private static bool NpcMatchesFilter(
    NpcFilterData npc,
    SpidDistributionFilter filter,
    IReadOnlySet<string>? virtualKeywords) =>
    MatchesStringFilters(npc, filter.StringFilters, virtualKeywords) &&
    MatchesFormFilters(npc, filter.FormFilters) &&
    MatchesLevelFilters(npc, filter.LevelFilters) &&
    MatchesTraitFilters(npc, filter.TraitFilters);

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

  public static IReadOnlyList<NpcFilterData> GetMatchingNpcsForEntry(
    IReadOnlyList<NpcFilterData> allNpcs,
    DistributionEntry entry,
    IReadOnlySet<string>? virtualKeywords = null) =>
    allNpcs.AsParallel().Where(npc => NpcMatchesEntry(npc, entry, virtualKeywords)).ToList();

  private static bool NpcMatchesEntry(
    NpcFilterData npc,
    DistributionEntry entry,
    IReadOnlySet<string>? virtualKeywords)
  {
    if (!MatchesRawStringFilters(npc, entry.RawStringFilters, virtualKeywords))
    {
      return false;
    }

    if (!MatchesFilters(entry.NpcFilters, f => f.FormKey, [npc.FormKey], entry.NpcLogicMode))
    {
      return false;
    }

    var npcFactions = npc.Factions.Select(f => f.FactionFormKey).ToHashSet();
    if (!MatchesFilters(entry.FactionFilters, f => f.FormKey, npcFactions, entry.FactionLogicMode))
    {
      return false;
    }

    if (!MatchesFilters(entry.KeywordFilters, f => f.EditorId, npc.Keywords, entry.KeywordLogicMode))
    {
      return false;
    }

    if (!MatchesFilters(
          entry.RaceFilters,
          f => f.FormKey,
          npc.RaceFormKey.HasValue ? [npc.RaceFormKey.Value] : [],
          entry.RaceLogicMode))
    {
      return false;
    }

    if (entry.ClassFormKeys.Count > 0 &&
        (!npc.ClassFormKey.HasValue || !entry.ClassFormKeys.Contains(npc.ClassFormKey.Value)))
    {
      return false;
    }

    if (entry.OutfitFilterFormKeys.Count > 0 &&
        (!npc.DefaultOutfitFormKey.HasValue || !entry.OutfitFilterFormKeys.Contains(npc.DefaultOutfitFormKey.Value)))
    {
      return false;
    }

    return MatchesTraitFilters(npc, entry.TraitFilters);
  }

  private static bool MatchesRawStringFilters(
    NpcFilterData npc,
    string? rawStringFilters,
    IReadOnlySet<string>? virtualKeywords)
  {
    if (string.IsNullOrWhiteSpace(rawStringFilters))
    {
      return true;
    }

    var expressions = rawStringFilters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (expressions.Length == 0)
    {
      return true;
    }

    foreach (var expression in expressions)
    {
      var parts = expression.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      var allPartsMatch = true;

      foreach (var part in parts)
      {
        var trimmedPart = part.Trim();
        var isNegated = trimmedPart.StartsWith('-');
        var value = isNegated ? trimmedPart[1..] : trimmedPart;
        var hasWildcard = value.Contains('*');

        bool matches;
        if (hasWildcard)
        {
          var searchValue = value.Replace("*", string.Empty);
          matches = PartialMatchesNpcStrings(npc, searchValue, virtualKeywords);
        }
        else
        {
          matches = ExactMatchesNpcStrings(npc, value, virtualKeywords);
        }

        if (isNegated)
        {
          matches = !matches;
        }

        if (!matches)
        {
          allPartsMatch = false;
          break;
        }
      }

      if (allPartsMatch)
      {
        return true;
      }
    }

    return false;
  }

  private static bool MatchesFilters<TFilter, TValue>(
    IReadOnlyList<TFilter> filters,
    Func<TFilter, TValue> valueSelector,
    IReadOnlyCollection<TValue> npcValues,
    FilterLogicMode logicMode = FilterLogicMode.And)
    where TFilter : IExcludable
  {
    if (filters.Count == 0)
    {
      return true;
    }

    var included = filters.Where(f => !f.IsExcluded).Select(valueSelector).ToList();
    var excluded = new HashSet<TValue>(filters.Where(f => f.IsExcluded).Select(valueSelector));

    if (excluded.Any(npcValues.Contains))
    {
      return false;
    }

    if (included.Count > 0)
    {
      return logicMode == FilterLogicMode.Or
               ? included.Any(npcValues.Contains)
               : included.All(npcValues.Contains);
    }

    return true;
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
    ExactMatchesNpcStrings(NpcFilterData npc, string value, IReadOnlySet<string>? virtualKeywords)
  {
    if (npc.MatchKeys != null && npc.MatchKeys.Contains(value))
    {
      return true;
    }

    return virtualKeywords?.Contains(value) ?? false;
  }

  internal static bool PartialMatchesNpcStrings(
    NpcFilterData npc,
    string value,
    IReadOnlySet<string>? virtualKeywords) =>
    StringUtilities.ContainsValue(npc.Name, value) ||
    StringUtilities.ContainsValue(npc.EditorId, value) ||
    StringUtilities.ContainsValue(npc.TemplateEditorId, value) ||
    StringUtilities.AnyContainValue(npc.Keywords, value) ||
    StringUtilities.AnyContainValue(virtualKeywords, value);

  private static bool MatchesFormFilters(NpcFilterData npc, SpidFilterSection filters)
  {
    if (filters.IsEmpty)
    {
      return true;
    }

    if (filters.GlobalExclusions.Any(e => MatchesFormValue(npc, e)))
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
    var matches = MatchesFormValue(npc, part);
    return part.IsNegated ? !matches : matches;
  }

  private static bool MatchesFormValue(NpcFilterData npc, SpidFilterPart part)
  {
    if (part.IsModKey == true)
    {
      return string.Equals(npc.SourceMod.FileName, part.Value, StringComparison.OrdinalIgnoreCase);
    }

    if (part.FormKey.HasValue)
    {
      var formKey = part.FormKey.Value;
      return npc.FormKey == formKey ||
             npc.RaceFormKey == formKey ||
             npc.ClassFormKey == formKey ||
             npc.CombatStyleFormKey == formKey ||
             npc.VoiceTypeFormKey == formKey ||
             npc.DefaultOutfitFormKey == formKey ||
             npc.TemplateFormKey == formKey ||
             npc.Factions.Any(f => f.FactionFormKey == formKey);
    }

    var value = part.Value;

    if (FormKeyHelper.TryParseFormId(value, out var formId))
    {
      return npc.FormKey.ID == formId ||
             npc.RaceFormKey?.ID == formId ||
             npc.ClassFormKey?.ID == formId ||
             npc.CombatStyleFormKey?.ID == formId ||
             npc.VoiceTypeFormKey?.ID == formId ||
             npc.DefaultOutfitFormKey?.ID == formId ||
             npc.TemplateFormKey?.ID == formId ||
             npc.Factions.Any(f => f.FactionFormKey.ID == formId);
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
    minSkill   = 0;
    maxSkill   = null;

    var openParen  = value.IndexOf('(');
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
