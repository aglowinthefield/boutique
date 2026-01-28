using Boutique.Models;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

public static class SpidLineParser
{
    private static readonly Dictionary<string, SpidFormType> FormTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Outfit"] = SpidFormType.Outfit,
        ["Keyword"] = SpidFormType.Keyword,
        ["Spell"] = SpidFormType.Spell,
        ["Perk"] = SpidFormType.Perk,
        ["Item"] = SpidFormType.Item,
        ["Shout"] = SpidFormType.Shout,
        ["Package"] = SpidFormType.Package,
        ["Faction"] = SpidFormType.Faction,
        ["SleepOutfit"] = SpidFormType.SleepOutfit,
        ["Skin"] = SpidFormType.Skin
    };

    public static bool TryParse(string line, out SpidDistributionFilter? result) =>
        TryParse(line, out result, null);

    public static bool TryParseOutfit(string line, out SpidDistributionFilter? result) =>
        TryParse(line, out result, SpidFormType.Outfit);

    public static bool TryParseKeyword(string line, out SpidDistributionFilter? result) =>
        TryParse(line, out result, SpidFormType.Keyword);

    public static bool TryParse(string line, out SpidDistributionFilter? result, SpidFormType? formTypeFilter)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();

        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex < 0)
        {
            return false;
        }

        var beforeEquals = trimmed[..equalsIndex].Trim();
        if (string.IsNullOrWhiteSpace(beforeEquals))
        {
            return false;
        }

        if (!FormTypeMap.TryGetValue(beforeEquals, out var formType))
        {
            return false;
        }

        if (formTypeFilter.HasValue && formType != formTypeFilter.Value)
        {
            return false;
        }

        var valuePart = trimmed[(equalsIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(valuePart))
        {
            return false;
        }

        // Remove inline comments
        valuePart = StringUtilities.RemoveInlineComment(valuePart);
        if (string.IsNullOrWhiteSpace(valuePart))
        {
            return false;
        }

        result = ParseValuePart(valuePart, trimmed, formType);
        return result != null;
    }

    private static SpidDistributionFilter? ParseValuePart(string valuePart, string rawLine, SpidFormType formType)
    {
        var (formIdentifier, remainder) = ExtractFormIdentifier(valuePart);

        if (string.IsNullOrWhiteSpace(formIdentifier))
        {
            return null;
        }

        var sections = string.IsNullOrWhiteSpace(remainder)
            ? []
            : remainder.Split('|');

        var stringFilters = ParseFilterSection(GetSection(sections, 0));
        var formFilters = ParseFilterSection(GetSection(sections, 1));
        var levelFilters = GetSection(sections, 2);
        var traitFilters = ParseTraitFilters(GetSection(sections, 3));
        var countOrPackageIdx = GetSection(sections, 4);
        var chance = ParseChance(GetSection(sections, 5));

        return new SpidDistributionFilter
        {
            FormType = formType,
            FormIdentifier = formIdentifier,
            StringFilters = stringFilters,
            FormFilters = formFilters,
            LevelFilters = IsNone(levelFilters) ? null : levelFilters,
            TraitFilters = traitFilters,
            CountOrPackageIdx = IsNone(countOrPackageIdx) ? null : countOrPackageIdx,
            Chance = chance,
            RawLine = rawLine
        };
    }

    private static (string Identifier, string Remainder) ExtractFormIdentifier(string valuePart)
    {
        var tildeIndex = valuePart.IndexOf('~');
        if (tildeIndex >= 0)
        {
            var afterTilde = valuePart[(tildeIndex + 1)..];
            var modEndIndex = FormKeyHelper.FindModKeyEnd(afterTilde);

            if (modEndIndex > 0)
            {
                var identifier = valuePart[..(tildeIndex + 1 + modEndIndex)];
                var remainder = afterTilde.Length > modEndIndex && afterTilde[modEndIndex] == '|'
                    ? afterTilde[(modEndIndex + 1)..]
                    : string.Empty;
                return (identifier, remainder);
            }
        }

        var firstPipe = valuePart.IndexOf('|');
        if (firstPipe >= 0)
        {
            var firstPart = valuePart[..firstPipe];

            if (FormKeyHelper.IsModKeyFileName(firstPart))
            {
                var afterFirstPipe = valuePart[(firstPipe + 1)..];
                var secondPipe = afterFirstPipe.IndexOf('|');

                if (secondPipe >= 0)
                {
                    var potentialFormId = afterFirstPipe[..secondPipe];
                    if (FormKeyHelper.LooksLikeFormId(potentialFormId))
                    {
                        var identifier = valuePart[..(firstPipe + 1 + secondPipe)];
                        var remainder = afterFirstPipe[(secondPipe + 1)..];
                        return (identifier, remainder);
                    }
                }
                else if (FormKeyHelper.LooksLikeFormId(afterFirstPipe))
                {
                    return (valuePart, string.Empty);
                }
            }

            return (firstPart, valuePart[(firstPipe + 1)..]);
        }

        return (valuePart, string.Empty);
    }

    private static string? GetSection(string[] sections, int index)
    {
        if (index < 0 || index >= sections.Length)
        {
            return null;
        }

        var value = sections[index].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool IsNone(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               value.Equals("NONE", StringComparison.OrdinalIgnoreCase);
    }

    private static SpidFilterSection ParseFilterSection(string? sectionText)
    {
        var section = new SpidFilterSection();

        if (IsNone(sectionText))
        {
            return section;
        }

        var expressions = sectionText!.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var globalExclusions = new List<SpidFilterPart>();

        foreach (var expr in expressions)
        {
            var trimmedExpr = expr.Trim();
            if (string.IsNullOrEmpty(trimmedExpr))
            {
                continue;
            }

            if (trimmedExpr.StartsWith('-') && !trimmedExpr.Contains('+'))
            {
                var exclusionValue = trimmedExpr[1..].Trim();
                if (!string.IsNullOrWhiteSpace(exclusionValue))
                {
                    var exclusionPart = new SpidFilterPart { Value = exclusionValue, IsNegated = true };

                    // Pre-resolve FormKey or ModKey status
                    if (FormKeyHelper.TryParse(exclusionValue, out var formKey))
                    {
                        exclusionPart.FormKey = formKey;
                    }
                    else if (FormKeyHelper.IsModKeyFileName(exclusionValue))
                    {
                        exclusionPart.IsModKey = true;
                    }

                    globalExclusions.Add(exclusionPart);
                }

                continue;
            }

            var expression = ParseFilterExpression(trimmedExpr);
            if (expression.Parts.Count > 0)
            {
                section.Expressions.Add(expression);
            }
        }

        if (globalExclusions.Count > 0)
        {
            section.GlobalExclusions.AddRange(globalExclusions);
        }

        return section;
    }

    private static SpidFilterExpression ParseFilterExpression(string exprText)
    {
        var expression = new SpidFilterExpression();

        if (string.IsNullOrWhiteSpace(exprText))
        {
            return expression;
        }

        // Split by + for AND parts
        var parts = exprText.Split('+', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmedPart))
            {
                continue;
            }

            var isNegated = trimmedPart.StartsWith('-');
            var value = isNegated ? trimmedPart[1..].Trim() : trimmedPart;

            if (!string.IsNullOrWhiteSpace(value))
            {
                var filterPart = new SpidFilterPart { Value = value, IsNegated = isNegated };

                // Pre-resolve FormKey or ModKey status
                if (FormKeyHelper.TryParse(value, out var formKey))
                {
                    filterPart.FormKey = formKey;
                }
                else if (FormKeyHelper.IsModKeyFileName(value))
                {
                    filterPart.IsModKey = true;
                }

                expression.Parts.Add(filterPart);
            }
        }

        return expression;
    }

    private static SpidTraitFilters ParseTraitFilters(string? traitText)
    {
        if (IsNone(traitText))
        {
            return new SpidTraitFilters();
        }

        bool? isFemale = null;
        bool? isUnique = null;
        bool? isSummonable = null;
        bool? isChild = null;
        bool? isLeveled = null;
        bool? isTeammate = null;
        bool? isDead = null;

        var traits = traitText!.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var trait in traits)
        {
            var trimmed = trait.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var isNegated = trimmed.StartsWith('-');
            var code = isNegated ? trimmed[1..].ToUpperInvariant() : trimmed.ToUpperInvariant();

            switch (code)
            {
                case "F":
                    isFemale = !isNegated;
                    break;
                case "M":
                    isFemale = isNegated; // M means male, so isFemale = false unless negated
                    break;
                case "U":
                    isUnique = !isNegated;
                    break;
                case "S":
                    isSummonable = !isNegated;
                    break;
                case "C":
                    isChild = !isNegated;
                    break;
                case "L":
                    isLeveled = !isNegated;
                    break;
                case "T":
                    isTeammate = !isNegated;
                    break;
                case "D":
                    isDead = !isNegated;
                    break;
            }
        }

        return new SpidTraitFilters
        {
            IsFemale = isFemale,
            IsUnique = isUnique,
            IsSummonable = isSummonable,
            IsChild = isChild,
            IsLeveled = isLeveled,
            IsTeammate = isTeammate,
            IsDead = isDead
        };
    }

    private static int ParseChance(string? chanceText)
    {
        if (IsNone(chanceText))
        {
            return 100;
        }

        if (int.TryParse(chanceText, out var chance))
        {
            return Math.Clamp(chance, 0, 100);
        }

        return 100;
    }

    public static IReadOnlyList<string> GetSpecificNpcIdentifiers(
        SpidDistributionFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache = null)
    {
        var results = new List<string>();
        var keywordEditorIds = linkCache?.WinningOverrides<IKeywordGetter>()
                .Select(k => k.EditorID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expr in filter.StringFilters.Expressions)
        {
            foreach (var part in expr.Parts.Where(p => !p.HasWildcard && !p.IsNegated))
            {
                if (keywordEditorIds != null && keywordEditorIds.Contains(part.Value))
                {
                    continue;
                }

                results.Add(part.Value);
            }
        }

        return results;
    }

    public static IReadOnlyList<string> GetKeywordIdentifiers(
        SpidDistributionFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache = null)
    {
        var results = new List<string>();
        var keywordEditorIds = linkCache?.WinningOverrides<IKeywordGetter>()
                .Select(k => k.EditorID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expr in filter.StringFilters.Expressions)
        {
            foreach (var part in expr.Parts.Where(p => !p.HasWildcard && !p.IsNegated))
            {
                if (keywordEditorIds == null || keywordEditorIds.Contains(part.Value))
                {
                    results.Add(part.Value);
                }
            }
        }

        return results;
    }

    public static IReadOnlyList<string> GetFactionIdentifiers(
        SpidDistributionFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache = null)
    {
        var factionEditorIds = linkCache?.WinningOverrides<IFactionGetter>()
                .Select(f => f.EditorID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return filter.FormFilters.Expressions
            .SelectMany(e => e.Parts.Where(p => !p.IsNegated))
            .Where(p => factionEditorIds == null || factionEditorIds.Contains(p.Value))
            .Select(p => p.Value)
            .ToList();
    }

    public static IReadOnlyList<string> GetRaceIdentifiers(
        SpidDistributionFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache = null)
    {
        var raceEditorIds = linkCache?.WinningOverrides<IRaceGetter>()
                .Select(r => r.EditorID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return filter.FormFilters.Expressions
            .SelectMany(e => e.Parts.Where(p => !p.IsNegated))
            .Where(p => raceEditorIds == null || raceEditorIds.Contains(p.Value))
            .Select(p => p.Value)
            .ToList();
    }

    public static IReadOnlyList<string> GetClassIdentifiers(
        SpidDistributionFilter filter,
        ILinkCache<ISkyrimMod, ISkyrimModGetter>? linkCache = null)
    {
        var classEditorIds = linkCache?.WinningOverrides<IClassGetter>()
                .Select(c => c.EditorID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return filter.FormFilters.Expressions
            .SelectMany(e => e.Parts.Where(p => !p.IsNegated))
            .Where(p => classEditorIds == null || classEditorIds.Contains(p.Value))
            .Select(p => p.Value)
            .ToList();
    }

    public static bool IsSpidLine(string line) =>
        TryParse(line, out _, null);

    public static bool IsOutfitLine(string line) =>
        TryParse(line, out _, SpidFormType.Outfit);

    public static bool IsKeywordLine(string line) =>
        TryParse(line, out _, SpidFormType.Keyword);

    public static SpidFormType? GetFormType(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex < 0)
        {
            return null;
        }

        var beforeEquals = trimmed[..equalsIndex].Trim();
        return FormTypeMap.TryGetValue(beforeEquals, out var formType) ? formType : null;
    }

    public static IReadOnlyList<string> GetReferencedKeywords(SpidDistributionFilter filter)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var expr in filter.StringFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                results.Add(part.Value);
            }
        }

        return results.ToList();
    }

}
