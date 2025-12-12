using Boutique.Models;

namespace Boutique.Utilities;

/// <summary>
/// Parser for SPID (Spell Perk Item Distributor) outfit distribution lines.
/// SPID syntax: FormType = FormOrEditorID|StringFilters|FormFilters|LevelFilters|TraitFilters|CountOrPackageIdx|Chance
/// </summary>
public static class SpidLineParser
{
    /// <summary>
    /// Attempts to parse a SPID outfit distribution line.
    /// </summary>
    /// <param name="line">The raw line text.</param>
    /// <param name="result">The parsed filter if successful.</param>
    /// <returns>True if parsing succeeded.</returns>
    public static bool TryParse(string line, out SpidDistributionFilter? result)
    {
        result = null;
        
        if (string.IsNullOrWhiteSpace(line))
            return false;
        
        var trimmed = line.Trim();
        
        // Must start with "Outfit" (case-insensitive)
        if (!trimmed.StartsWith("Outfit", StringComparison.OrdinalIgnoreCase))
            return false;
        
        // Find the = sign
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex < 0)
            return false;
        
        // Verify it's "Outfit =" or "Outfit="
        var beforeEquals = trimmed[..equalsIndex].Trim();
        if (!beforeEquals.Equals("Outfit", StringComparison.OrdinalIgnoreCase))
            return false;
        
        var valuePart = trimmed[(equalsIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(valuePart))
            return false;
        
        // Remove inline comments
        valuePart = RemoveInlineComment(valuePart);
        if (string.IsNullOrWhiteSpace(valuePart))
            return false;
        
        result = ParseValuePart(valuePart, trimmed);
        return result != null;
    }
    
    private static SpidDistributionFilter? ParseValuePart(string valuePart, string rawLine)
    {
        // Split by | to get all sections
        // But first we need to extract the outfit identifier, which can itself contain | or ~
        var (outfitIdentifier, remainder) = ExtractOutfitIdentifier(valuePart);
        
        if (string.IsNullOrWhiteSpace(outfitIdentifier))
            return null;
        
        // Now split the remainder by | to get filter sections
        // Sections: StringFilters|FormFilters|LevelFilters|TraitFilters|CountOrPackageIdx|Chance
        var sections = string.IsNullOrWhiteSpace(remainder) 
            ? Array.Empty<string>() 
            : remainder.Split('|');
        
        var stringFilters = ParseFilterSection(GetSection(sections, 0));
        var formFilters = ParseFilterSection(GetSection(sections, 1));
        var levelFilters = GetSection(sections, 2);
        var traitFilters = ParseTraitFilters(GetSection(sections, 3));
        var countOrPackageIdx = GetSection(sections, 4);
        var chance = ParseChance(GetSection(sections, 5));
        
        return new SpidDistributionFilter
        {
            OutfitIdentifier = outfitIdentifier,
            StringFilters = stringFilters,
            FormFilters = formFilters,
            LevelFilters = IsNone(levelFilters) ? null : levelFilters,
            TraitFilters = traitFilters,
            CountOrPackageIdx = IsNone(countOrPackageIdx) ? null : countOrPackageIdx,
            Chance = chance,
            RawLine = rawLine
        };
    }
    
    /// <summary>
    /// Extracts the outfit identifier from the value part, handling FormKey formats.
    /// Returns the identifier and the remaining filter sections.
    /// </summary>
    private static (string Identifier, string Remainder) ExtractOutfitIdentifier(string valuePart)
    {
        // Check for tilde format: 0x800~Plugin.esp or 0x800~Plugin.esp|filters
        var tildeIndex = valuePart.IndexOf('~');
        if (tildeIndex >= 0)
        {
            // Format: FormID~ModKey|filters
            var afterTilde = valuePart[(tildeIndex + 1)..];
            var modEndIndex = FindModKeyEnd(afterTilde);
            
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
            if (IsModKeyFileName(firstPart))
            {
                var afterFirstPipe = valuePart[(firstPipe + 1)..];
                var secondPipe = afterFirstPipe.IndexOf('|');
                
                if (secondPipe >= 0)
                {
                    var potentialFormId = afterFirstPipe[..secondPipe];
                    if (LooksLikeFormId(potentialFormId))
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
                    if (LooksLikeFormId(afterFirstPipe))
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
    
    private static int FindModKeyEnd(string text)
    {
        var extensions = new[] { ".esp", ".esm", ".esl" };
        foreach (var ext in extensions)
        {
            var idx = text.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return idx + ext.Length;
        }
        return -1;
    }
    
    private static bool IsModKeyFileName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        
        return text.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
               text.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
               text.EndsWith(".esl", StringComparison.OrdinalIgnoreCase);
    }
    
    private static bool LooksLikeFormId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];
        
        return trimmed.Length >= 1 && trimmed.Length <= 8 && 
               trimmed.All(char.IsAsciiHexDigit);
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
            var expression = ParseFilterExpression(expr.Trim());
            if (expression.Parts.Count > 0)
            {
                section.Expressions.Add(expression);
            }
        }
        
        return section;
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
    
    private static string RemoveInlineComment(string text)
    {
        var commentIndex = text.IndexOfAny([';', '#']);
        if (commentIndex >= 0)
            text = text[..commentIndex];
        
        return text.Trim();
    }
    
    /// <summary>
    /// Gets all identifiers from the string filters that look like specific NPC names/EditorIDs
    /// (not keywords or patterns).
    /// </summary>
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
    
    /// <summary>
    /// Gets all keyword identifiers from the string filters.
    /// </summary>
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
    
    /// <summary>
    /// Gets all faction identifiers from the form filters.
    /// </summary>
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
    
    /// <summary>
    /// Gets all race identifiers from the form filters.
    /// </summary>
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
}

