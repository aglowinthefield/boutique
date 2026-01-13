namespace Boutique.Models;

/// <summary>
/// Represents a parsed SPID Keyword distribution entry.
/// Used to track keywords that SPID distributes to NPCs at runtime.
/// </summary>
public sealed class KeywordDistributionEntry
{
    /// <summary>
    /// The keyword identifier - either an EditorID or FormKey string.
    /// </summary>
    public required string KeywordIdentifier { get; init; }

    /// <summary>
    /// String filters (position 2): NPC name, EditorID, or keyword filters.
    /// </summary>
    public SpidFilterSection StringFilters { get; init; } = new();

    /// <summary>
    /// Form filters (position 3): Race, Class, Faction, etc.
    /// </summary>
    public SpidFilterSection FormFilters { get; init; } = new();

    /// <summary>
    /// Level filters (position 4): Min/max level requirements, skill requirements.
    /// </summary>
    public string? LevelFilters { get; init; }

    /// <summary>
    /// Trait filters (position 5): F=Female, M=Male, U=Unique, etc.
    /// </summary>
    public SpidTraitFilters TraitFilters { get; init; } = new();

    /// <summary>
    /// Chance percentage 0-100, default 100.
    /// </summary>
    public int Chance { get; init; } = 100;

    /// <summary>
    /// The raw line text for reference.
    /// </summary>
    public string RawLine { get; init; } = string.Empty;

    /// <summary>
    /// The source file path where this distribution was defined.
    /// </summary>
    public string? SourceFile { get; init; }

    /// <summary>
    /// The line number in the source file.
    /// </summary>
    public int LineNumber { get; init; }

    /// <summary>
    /// Creates a KeywordDistributionEntry from a parsed SpidDistributionFilter.
    /// </summary>
    public static KeywordDistributionEntry FromFilter(SpidDistributionFilter filter, string? sourceFile = null, int lineNumber = 0) =>
        new()
        {
            KeywordIdentifier = filter.FormIdentifier,
            StringFilters = filter.StringFilters,
            FormFilters = filter.FormFilters,
            LevelFilters = filter.LevelFilters,
            TraitFilters = filter.TraitFilters,
            Chance = filter.Chance,
            RawLine = filter.RawLine,
            SourceFile = sourceFile,
            LineNumber = lineNumber
        };

    /// <summary>
    /// Gets all keyword identifiers referenced in the string filters (dependencies).
    /// </summary>
    public IReadOnlyList<string> GetReferencedKeywords()
    {
        var results = new List<string>();

        foreach (var expr in StringFilters.Expressions)
        {
            foreach (var part in expr.Parts)
            {
                results.Add(part.Value);
            }
        }

        return results;
    }
}
