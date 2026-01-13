namespace Boutique.Models;

public enum SpidFormType
{
    Outfit,
    Keyword,
    Spell,
    Perk,
    Item,
    Shout,
    Package,
    Faction,
    SleepOutfit,
    Skin,
    Unknown
}

/// <summary>
/// Represents a parsed SPID distribution line with all filter sections.
/// SPID syntax: FormType = FormOrEditorID|StringFilters|FormFilters|LevelFilters|TraitFilters|CountOrPackageIdx|Chance
/// </summary>
public sealed class SpidDistributionFilter
{
    /// <summary>
    /// The type of form being distributed (Outfit, Keyword, Spell, etc.)
    /// </summary>
    public SpidFormType FormType { get; init; } = SpidFormType.Outfit;

    /// <summary>
    /// The form identifier - either an EditorID or FormKey string (0x800~Plugin.esp)
    /// </summary>
    public string FormIdentifier { get; init; } = string.Empty;

    /// <summary>
    /// Alias for FormIdentifier for backward compatibility with outfit-specific code.
    /// </summary>
    public string OutfitIdentifier => FormIdentifier;

    /// <summary>
    /// String filters (position 2): NPC name, EditorID, or keyword filters.
    /// Can include wildcards (*Guard), exclusions (-Bandit), and combinations (+).
    /// </summary>
    public SpidFilterSection StringFilters { get; init; } = new();

    /// <summary>
    /// Form filters (position 3): Race, Class, Faction, CombatStyle, Outfit, Perk, VoiceType, Location, or specific NPC.
    /// </summary>
    public SpidFilterSection FormFilters { get; init; } = new();

    /// <summary>
    /// Level filters (position 4): Min/max level requirements, skill requirements.
    /// </summary>
    public string? LevelFilters { get; init; }

    /// <summary>
    /// Trait filters (position 5): F=Female, M=Male, U=Unique, S=Summonable, C=Child, L=Leveled, T=Teammate, D=Dead
    /// </summary>
    public SpidTraitFilters TraitFilters { get; init; } = new();

    /// <summary>
    /// Count or package index (position 6).
    /// </summary>
    public string? CountOrPackageIdx { get; init; }

    /// <summary>
    /// Chance percentage 0-100 (position 7), default 100.
    /// </summary>
    public int Chance { get; init; } = 100;

    /// <summary>
    /// The raw line text for reference.
    /// </summary>
    public string RawLine { get; init; } = string.Empty;

    /// <summary>
    /// True if this distribution targets all NPCs (no string or form filters).
    /// </summary>
    public bool TargetsAllNpcs => StringFilters.IsEmpty && FormFilters.IsEmpty;

    /// <summary>
    /// True if this distribution uses keyword-based targeting (not specific NPC names).
    /// </summary>
    public bool UsesKeywordTargeting => StringFilters.HasKeywords;

    /// <summary>
    /// True if this distribution uses faction-based targeting.
    /// </summary>
    public bool UsesFactionTargeting => FormFilters.HasFactions;

    /// <summary>
    /// Gets a human-readable description of the targeting criteria.
    /// </summary>
    public string GetTargetingDescription()
    {
        var parts = new List<string>();

        if (!StringFilters.IsEmpty) parts.Add($"Names/Keywords: {StringFilters}");

        if (!FormFilters.IsEmpty) parts.Add($"Factions/Forms: {FormFilters}");

        if (!string.IsNullOrEmpty(LevelFilters) && !LevelFilters.Equals("NONE", StringComparison.OrdinalIgnoreCase)) parts.Add($"Level: {LevelFilters}");

        if (!TraitFilters.IsEmpty) parts.Add($"Traits: {TraitFilters}");

        if (Chance < 100) parts.Add($"Chance: {Chance}%");

        return parts.Count > 0 ? string.Join(", ", parts) : "All NPCs";
    }
}

/// <summary>
/// Represents a filter section that can contain multiple OR-expressions and AND-combined values.
/// </summary>
public sealed class SpidFilterSection
{
    /// <summary>
    /// List of filter expressions (comma-separated in SPID = OR logic).
    /// Each expression can have AND-combined parts (+ separator).
    /// </summary>
    public List<SpidFilterExpression> Expressions { get; init; } = [];

    public bool IsEmpty => Expressions.Count == 0;

    /// <summary>
    /// True if any expression contains keywords (non-NPC identifiers like ActorTypeNPC).
    /// </summary>
    public bool HasKeywords => Expressions.Any(e => e.Parts.Any(p => p.LooksLikeKeyword));

    /// <summary>
    /// True if any expression looks like a faction (contains "Faction" or similar).
    /// </summary>
    public bool HasFactions => Expressions.Any(e => e.Parts.Any(p => p.LooksLikeFaction));

    public override string ToString() => string.Join(", ", Expressions.Select(e => e.ToString()));
}

/// <summary>
/// Represents a single filter expression that may be AND-combined with +.
/// Example: "ActorTypeNPC+Bandit" has two parts combined with AND logic.
/// </summary>
public sealed class SpidFilterExpression
{
    /// <summary>
    /// Individual filter parts combined with AND logic (+ separator in SPID).
    /// </summary>
    public List<SpidFilterPart> Parts { get; init; } = [];

    public override string ToString() => string.Join(" AND ", Parts.Select(p => p.ToString()));
}

/// <summary>
/// Represents a single filter value, which may be negated or have wildcards.
/// </summary>
public sealed class SpidFilterPart
{
    /// <summary>
    /// The raw filter value (without negation prefix).
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// True if this filter is negated (prefixed with - in SPID).
    /// </summary>
    public bool IsNegated { get; init; }

    /// <summary>
    /// True if this filter contains wildcards (* in SPID for partial matching).
    /// </summary>
    public bool HasWildcard => Value.Contains('*');

    /// <summary>
    /// True if this looks like a keyword (starts with ActorType, has common keyword patterns).
    /// </summary>
    public bool LooksLikeKeyword =>
        Value.StartsWith("ActorType", StringComparison.OrdinalIgnoreCase) ||
        Value.StartsWith("Vampire", StringComparison.OrdinalIgnoreCase) ||
        Value.EndsWith("Keyword", StringComparison.OrdinalIgnoreCase) ||
        Value.Contains("Type");

    /// <summary>
    /// True if this looks like a faction reference.
    /// </summary>
    public bool LooksLikeFaction =>
        Value.Contains("Faction", StringComparison.OrdinalIgnoreCase) ||
        Value.StartsWith("Crime", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this looks like a race reference.
    /// </summary>
    public bool LooksLikeRace =>
        Value.EndsWith("Race", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this looks like a class reference.
    /// </summary>
    public bool LooksLikeClass =>
        Value.EndsWith("Class", StringComparison.OrdinalIgnoreCase);

    public override string ToString()
    {
        var prefix = IsNegated ? "NOT " : "";
        return $"{prefix}{Value}";
    }
}

/// <summary>
/// Parsed trait filters from SPID position 5.
/// </summary>
public sealed record SpidTraitFilters
{
    /// <summary>
    /// Gender filter: null = any, true = female, false = male
    /// </summary>
    public bool? IsFemale { get; init; }

    /// <summary>
    /// Unique NPC filter: null = any, true = must be unique, false = must not be unique
    /// </summary>
    public bool? IsUnique { get; init; }

    /// <summary>
    /// Summonable filter.
    /// </summary>
    public bool? IsSummonable { get; init; }

    /// <summary>
    /// Child filter.
    /// </summary>
    public bool? IsChild { get; init; }

    /// <summary>
    /// Leveled NPC filter.
    /// </summary>
    public bool? IsLeveled { get; init; }

    /// <summary>
    /// Teammate filter.
    /// </summary>
    public bool? IsTeammate { get; init; }

    /// <summary>
    /// Dead filter.
    /// </summary>
    public bool? IsDead { get; init; }

    public bool IsEmpty =>
        IsFemale == null && IsUnique == null && IsSummonable == null &&
        IsChild == null && IsLeveled == null && IsTeammate == null && IsDead == null;

    public override string ToString()
    {
        var parts = new List<string>();

        parts.Add(IsFemale == true ? "Female" : "Male");

        if (IsUnique == true)
            parts.Add("Unique");
        else if (IsUnique == false)
            parts.Add("Non-Unique");

        if (IsSummonable == true)
            parts.Add("Summonable");
        if (IsChild == true)
            parts.Add("Child");
        else if (IsChild == false)
            parts.Add("Adult");
        if (IsLeveled == true)
            parts.Add("Leveled");
        if (IsTeammate == true)
            parts.Add("Teammate");
        if (IsDead == true)
            parts.Add("Dead");

        return parts.Count > 0 ? string.Join(", ", parts) : string.Empty;
    }
}
