using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public interface IExcludable
{
  bool IsExcluded { get; }
}

public enum DistributionType
{
  Outfit,
  Keyword,
  ExclusiveGroup
}

public enum FilterLogicMode
{
  And,
  Or
}

public readonly record struct KeywordFilter(string EditorId, bool IsExcluded = false) : IExcludable;

public readonly record struct FormKeyFilter(FormKey FormKey, bool IsExcluded = false) : IExcludable;

public sealed class DistributionEntry
{
  public DistributionType Type { get; set; } = DistributionType.Outfit;
  public IOutfitGetter? Outfit { get; set; }
  public string? KeywordToDistribute { get; set; }
  public string? ExclusiveGroupName { get; set; }
  public List<string> ExclusiveGroupForms { get; set; } = [];

  /// <summary>
  ///   NPC filters with negation support. Allows targeting or excluding specific NPCs.
  /// </summary>
  public List<FormKeyFilter> NpcFilters { get; set; } = [];

  /// <summary>
  ///   Keyword filters with negation support. EditorIDs of game keywords or virtual keywords (SPID-distributed via Keyword
  ///   = lines).
  /// </summary>
  public List<KeywordFilter> KeywordFilters { get; set; } = [];

  /// <summary>
  ///   Faction filters with negation support.
  /// </summary>
  public List<FormKeyFilter> FactionFilters { get; set; } = [];

  /// <summary>
  ///   Race filters with negation support.
  /// </summary>
  public List<FormKeyFilter> RaceFilters { get; set; } = [];

  public List<FormKey> ClassFormKeys { get; set; } = [];
  public List<FormKey> CombatStyleFormKeys { get; set; } = [];
  public List<FormKey> OutfitFilterFormKeys { get; set; } = [];
  public List<FormKey> PerkFormKeys { get; set; } = [];
  public List<FormKey> VoiceTypeFormKeys { get; set; } = [];
  public List<FormKey> LocationFormKeys { get; set; } = [];
  public List<FormKey> FormListFormKeys { get; set; } = [];

  public SpidTraitFilters TraitFilters { get; set; } = new();

  /// <summary>
  ///   Gets or sets SPID level filters (position 4). Supports level ranges (e.g., "5/20") and
  ///   skill filters (e.g., "12(85/999)" for Alteration skill 85+).
  /// </summary>
  public string? LevelFilters { get; set; }

  public int? Chance { get; set; }

  /// <summary>
  ///   Logic mode for NPC filters (AND = all must match, OR = at least one must match).
  /// </summary>
  public FilterLogicMode NpcLogicMode { get; set; } = FilterLogicMode.And;

  /// <summary>
  ///   Logic mode for Faction filters (AND = all must match, OR = at least one must match).
  /// </summary>
  public FilterLogicMode FactionLogicMode { get; set; } = FilterLogicMode.And;

  /// <summary>
  ///   Logic mode for Keyword filters (AND = all must match, OR = at least one must match).
  /// </summary>
  public FilterLogicMode KeywordLogicMode { get; set; } = FilterLogicMode.And;

  /// <summary>
  ///   Logic mode for Race filters (AND = all must match, OR = at least one must match).
  /// </summary>
  public FilterLogicMode RaceLogicMode { get; set; } = FilterLogicMode.And;

  /// <summary>
  ///   Logic mode for Class filters (AND = all must match, OR = at least one must match).
  /// </summary>
  public FilterLogicMode ClassLogicMode { get; set; } = FilterLogicMode.And;

  /// <summary>
  ///   Logic mode for Location filters (AND = all must match, OR = at least one must match).
  /// </summary>
  public FilterLogicMode LocationLogicMode { get; set; } = FilterLogicMode.And;

  /// <summary>
  ///   Logic mode for Outfit filter filters (AND = all must match, OR = at least one must match).
  /// </summary>
  public FilterLogicMode OutfitFilterLogicMode { get; set; } = FilterLogicMode.And;

  /// <summary>
  ///   Raw/advanced string filters that can't be represented by dropdowns.
  ///   Supports wildcards (*Mage, *Guard), virtual keywords, and other SPID syntax.
  /// </summary>
  public string? RawStringFilters { get; set; }

  /// <summary>
  ///   Raw/advanced form filters that can't be represented by dropdowns.
  ///   Supports SPID form filter syntax for advanced use cases.
  /// </summary>
  public string? RawFormFilters { get; set; }

  public bool HasUnresolvedFilters =>
    !string.IsNullOrWhiteSpace(RawStringFilters) || !string.IsNullOrWhiteSpace(RawFormFilters);
}

public sealed record DistributionParseError(int LineNumber, string LineContent, string Reason)
{
  public string ErrorHeader => $"Line {LineNumber}: {Reason}";
}
