using Boutique.Models;

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
        TryParse(line, out result, formTypeFilter: null);

    public static bool TryParseOutfit(string line, out SpidDistributionFilter? result) =>
        TryParse(line, out result, formTypeFilter: SpidFormType.Outfit);

    public static bool TryParseKeyword(string line, out SpidDistributionFilter? result) =>
        TryParse(line, out result, formTypeFilter: SpidFormType.Keyword);

    public static bool TryParse(string line, out SpidDistributionFilter? result, SpidFormType? formTypeFilter)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var trimmed = line.Trim();

        // Find the = sign
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex < 0)
            return false;

        // Extract the form type keyword before the equals sign
        var beforeEquals = trimmed[..equalsIndex].Trim();
        if (string.IsNullOrWhiteSpace(beforeEquals))
            return false;

        // Check if it's a recognized form type
        if (!FormTypeMap.TryGetValue(beforeEquals, out var formType))
            return false;

        // If a filter is specified, only parse matching types
        if (formTypeFilter.HasValue && formType != formTypeFilter.Value)
            return false;

        var valuePart = trimmed[(equalsIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(valuePart))
            return false;

        // Remove inline comments
        valuePart = StringUtilities.RemoveInlineComment(valuePart);
        if (string.IsNullOrWhiteSpace(valuePart))
            return false;

        result = ParseValuePart(valuePart, trimmed, formType);
        return result != null;
    }

    private static SpidDistributionFilter? ParseValuePart(string valuePart, string rawLine, SpidFormType formType)
    {
        // Split by | to get all sections
        // But first we need to extract the form identifier, which can itself contain | or ~
        var (formIdentifier, remainder) = ExtractFormIdentifier(valuePart);

        if (string.IsNullOrWhiteSpace(formIdentifier))
            return null;

        // Now split the remainder by | to get filter sections
        // Sections: StringFilters|FormFilters|LevelFilters|TraitFilters|CountOrPackageIdx|Chance
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
        // Check for tilde format: 0x800~Plugin.esp or 0x800~Plugin.esp|filters
        var tildeIndex = valuePart.IndexOf('~');
        if (tildeIndex >= 0)
        {
            // Format: FormID~ModKey|filters
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

        // Check for pipe-format FormKey: Plugin.esp|0x12345|filters
        var firstPipe = valuePart.IndexOf('|');
        if (firstPipe >= 0)
        {
            var firstPart = valuePart[..firstPipe];

            // If first part is a mod key
            if (FormKeyHelper.IsModKeyFileName(firstPart))
            {
                var afterFirstPipe = valuePart[(firstPipe + 1)..];
                var secondPipe = afterFirstPipe.IndexOf('|');

                if (secondPipe >= 0)
                {
                    var potentialFormId = afterFirstPipe[..secondPipe];
                    if (FormKeyHelper.LooksLikeFormId(potentialFormId))
                    {
                        // This is ModKey|FormID|filters
                        var identifier = valuePart[..(firstPipe + 1 + secondPipe)];
                        var remainder = afterFirstPipe[(secondPipe + 1)..];
                        return (identifier, remainder);
                    }
                }
                else
                {
                    // Only one pipe: ModKey|FormID (no filters)
                    if (FormKeyHelper.LooksLikeFormId(afterFirstPipe))
                    {
                        return (valuePart, string.Empty);
                    }
                }
            }

            // First part is not a mod key, so it's EditorID|filters
            return (firstPart, valuePart[(firstPipe + 1)..]);
        }

        // No pipe or tilde - just an EditorID
        return (valuePart, string.Empty);
    }


    private static string? GetSection(string[] sections, int index)
    {
        if (index < 0 || index >= sections.Length)
            return null;

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
            return section;

        // Split by comma for OR expressions
        var expressions = sectionText!.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var expr in expressions)
        {
            var trimmedExpr = expr.Trim();
            if (string.IsNullOrEmpty(trimmedExpr))
                continue;

            // Check if this expression is purely negated (starts with -)
            // In SPID, negated items after a comma are AND conditions attached to the previous expression
            // e.g., "A+B,-C,-D" means "(A AND B AND NOT C AND NOT D)", not "(A AND B) OR (NOT C) OR (NOT D)"
            var isPurelyNegated = trimmedExpr.StartsWith('-') && !trimmedExpr.Contains('+');

            if (isPurelyNegated && section.Expressions.Count > 0)
            {
                // Attach to previous expression as additional AND condition
                var previousExpr = section.Expressions[^1];
                var part = ParseFilterPart(trimmedExpr);
                if (part != null)
                {
                    previousExpr.Parts.Add(part);
                }
            }
            else
            {
                var expression = ParseFilterExpression(trimmedExpr);
                if (expression.Parts.Count > 0)
                {
                    section.Expressions.Add(expression);
                }
            }
        }

        return section;
    }

    private static SpidFilterPart? ParseFilterPart(string partText)
    {
        var trimmedPart = partText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedPart))
            return null;

        var isNegated = trimmedPart.StartsWith('-');
        var value = isNegated ? trimmedPart[1..].Trim() : trimmedPart;

        if (string.IsNullOrWhiteSpace(value))
            return null;

        return new SpidFilterPart
        {
            Value = value,
            IsNegated = isNegated
        };
    }

    private static SpidFilterExpression ParseFilterExpression(string exprText)
    {
        var expression = new SpidFilterExpression();

        if (string.IsNullOrWhiteSpace(exprText))
            return expression;

        // Split by + for AND parts
        var parts = exprText.Split('+', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmedPart))
                continue;

            var isNegated = trimmedPart.StartsWith('-');
            var value = isNegated ? trimmedPart[1..].Trim() : trimmedPart;

            if (!string.IsNullOrWhiteSpace(value))
            {
                expression.Parts.Add(new SpidFilterPart
                {
                    Value = value,
                    IsNegated = isNegated
                });
            }
        }

        return expression;
    }

    private static SpidTraitFilters ParseTraitFilters(string? traitText)
    {
        if (IsNone(traitText))
            return new SpidTraitFilters();

        bool? isFemale = null;
        bool? isUnique = null;
        bool? isSummonable = null;
        bool? isChild = null;
        bool? isLeveled = null;
        bool? isTeammate = null;
        bool? isDead = null;

        // Trait filters can be separated by / and can be negated with -
        var traits = traitText!.Split('/', StringSplitOptions.RemoveEmptyEntries);

        foreach (var trait in traits)
        {
            var trimmed = trait.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

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
            return 100;

        if (int.TryParse(chanceText, out var chance))
            return Math.Clamp(chance, 0, 100);

        return 100;
    }

    public static IReadOnlyList<string> GetSpecificNpcIdentifiers(SpidDistributionFilter filter)
    {
        var results = new List<string>();

        foreach (var expr in filter.StringFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                // Skip if it has wildcards, looks like a keyword, or is negated
                if (part.HasWildcard || part.LooksLikeKeyword || part.IsNegated)
                    continue;

                results.Add(part.Value);
            }
        }

        return results;
    }

    public static IReadOnlyList<string> GetKeywordIdentifiers(SpidDistributionFilter filter)
    {
        var results = new List<string>();

        foreach (var expr in filter.StringFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.LooksLikeKeyword && !part.IsNegated)
                {
                    results.Add(part.Value);
                }
            }
        }

        return results;
    }

    public static IReadOnlyList<string> GetFactionIdentifiers(SpidDistributionFilter filter)
    {
        var results = new List<string>();

        foreach (var expr in filter.FormFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.LooksLikeFaction && !part.IsNegated)
                {
                    results.Add(part.Value);
                }
            }
        }

        return results;
    }

    public static IReadOnlyList<string> GetRaceIdentifiers(SpidDistributionFilter filter)
    {
        var results = new List<string>();

        foreach (var expr in filter.FormFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.LooksLikeRace && !part.IsNegated)
                {
                    results.Add(part.Value);
                }
            }
        }

        return results;
    }

    public static IReadOnlyList<string> GetClassIdentifiers(SpidDistributionFilter filter)
    {
        var results = new List<string>();

        foreach (var expr in filter.FormFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                if (part.LooksLikeClass && !part.IsNegated)
                {
                    results.Add(part.Value);
                }
            }
        }

        return results;
    }

    public static bool IsSpidLine(string line) =>
        TryParse(line, out _, formTypeFilter: null);

    public static bool IsOutfitLine(string line) =>
        TryParse(line, out _, formTypeFilter: SpidFormType.Outfit);

    public static bool IsKeywordLine(string line) =>
        TryParse(line, out _, formTypeFilter: SpidFormType.Keyword);

    public static SpidFormType? GetFormType(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = line.Trim();
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex < 0)
            return null;

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
