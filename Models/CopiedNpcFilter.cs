using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
///   Represents a copied NPC filter from the NPCs tab that can be pasted into a distribution entry.
///   This captures the filter state at the time of copying.
/// </summary>
public class CopiedNpcFilter
{
  /// <summary>
  ///   Gets the gender filter: null = any, true = female, false = male.
  /// </summary>
  public bool? IsFemale { get; init; }

  /// <summary>
  ///   Gets the unique NPC filter: null = any, true = unique only, false = non-unique only.
  /// </summary>
  public bool? IsUnique { get; init; }

  /// <summary>
  ///   Gets the templated NPC filter: null = any, true = templated only, false = non-templated only.
  /// </summary>
  public bool? IsTemplated { get; init; }

  /// <summary>
  ///   Gets the child NPC filter: null = any, true = children only, false = adults only.
  /// </summary>
  public bool? IsChild { get; init; }

  /// <summary>
  ///   Gets the factions to filter by.
  /// </summary>
  public IReadOnlyList<FormKey> Factions { get; init; } = [];

  /// <summary>
  ///   Gets the races to filter by.
  /// </summary>
  public IReadOnlyList<FormKey> Races { get; init; } = [];

  /// <summary>
  ///   Gets the keywords to filter by.
  /// </summary>
  public IReadOnlyList<FormKey> Keywords { get; init; } = [];

  /// <summary>
  ///   Gets the classes to filter by.
  /// </summary>
  public IReadOnlyList<FormKey> Classes { get; init; } = [];

  /// <summary>
  ///   Gets the human-readable description of the filter for display purposes.
  /// </summary>
  public string Description { get; init; } = string.Empty;

  /// <summary>
  ///   Gets a value indicating whether this filter has trait filters (gender, unique, child, etc.)
  ///   that can be applied to SPID format distributions.
  /// </summary>
  public bool HasTraitFilters =>
    IsFemale.HasValue ||
    IsUnique.HasValue ||
    IsChild.HasValue;

  /// <summary>
  ///   Creates a CopiedNpcFilter from an NpcSpidFilter.
  /// </summary>
  public static CopiedNpcFilter FromSpidFilter(NpcSpidFilter filter, string description)
  {
    return new CopiedNpcFilter
           {
             IsFemale    = filter.IsFemale,
             IsUnique    = filter.IsUnique,
             IsTemplated = filter.IsTemplated,
             IsChild     = filter.IsChild,
             Factions    = [.. filter.Factions],
             Races       = [.. filter.Races],
             Keywords    = [.. filter.Keywords],
             Classes     = [.. filter.Classes],
             Description = description
           };
  }
}
