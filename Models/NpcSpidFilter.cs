using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
/// Represents SPID-style filter criteria for filtering NPCs.
/// Used to filter the NPC list and generate equivalent SPID/SkyPatcher syntax.
/// </summary>
public class NpcSpidFilter
{
    /// <summary>
    /// Gender filter: null = any, true = female, false = male
    /// </summary>
    public bool? IsFemale { get; set; }

    /// <summary>
    /// Unique NPC filter: null = any, true = unique only, false = non-unique only
    /// </summary>
    public bool? IsUnique { get; set; }

    /// <summary>
    /// Templated NPC filter: null = any, true = templated only, false = non-templated only
    /// NPCs with a TemplateFormKey are considered templated.
    /// </summary>
    public bool? IsTemplated { get; set; }

    /// <summary>
    /// Child NPC filter: null = any, true = children only, false = adults only
    /// </summary>
    public bool? IsChild { get; set; }

    /// <summary>
    /// Summonable NPC filter: null = any, true = summonable only, false = non-summonable only
    /// </summary>
    public bool? IsSummonable { get; set; }

    /// <summary>
    /// Leveled NPC filter: null = any, true = leveled only, false = non-leveled only
    /// </summary>
    public bool? IsLeveled { get; set; }

    /// <summary>
    /// Factions to filter by (AND logic - NPC must be in ALL selected factions).
    /// </summary>
    public List<FormKey> Factions { get; set; } = [];

    /// <summary>
    /// Races to filter by (OR logic - NPC must match ANY selected race).
    /// </summary>
    public List<FormKey> Races { get; set; } = [];

    /// <summary>
    /// Keywords to filter by (AND logic - NPC must have ALL selected keywords).
    /// </summary>
    public List<FormKey> Keywords { get; set; } = [];

    /// <summary>
    /// Minimum level filter (inclusive). Null means no minimum.
    /// </summary>
    public int? MinLevel { get; set; }

    /// <summary>
    /// Maximum level filter (inclusive). Null means no maximum.
    /// </summary>
    public int? MaxLevel { get; set; }

    /// <summary>
    /// Returns true if no filters are active.
    /// </summary>
    public bool IsEmpty =>
        IsFemale == null &&
        IsUnique == null &&
        IsTemplated == null &&
        IsChild == null &&
        IsSummonable == null &&
        IsLeveled == null &&
        Factions.Count == 0 &&
        Races.Count == 0 &&
        Keywords.Count == 0 &&
        MinLevel == null &&
        MaxLevel == null;

    /// <summary>
    /// Returns true if any trait filters are active.
    /// </summary>
    public bool HasTraitFilters =>
        IsFemale != null ||
        IsUnique != null ||
        IsTemplated != null ||
        IsChild != null ||
        IsSummonable != null ||
        IsLeveled != null;

    /// <summary>
    /// Resets all filters to their default (unfiltered) state.
    /// </summary>
    public void Clear()
    {
        IsFemale = null;
        IsUnique = null;
        IsTemplated = null;
        IsChild = null;
        IsSummonable = null;
        IsLeveled = null;
        Factions.Clear();
        Races.Clear();
        Keywords.Clear();
        MinLevel = null;
        MaxLevel = null;
    }

    /// <summary>
    /// Tests if the given NPC matches all active filter criteria.
    /// </summary>
    public bool Matches(NpcFilterData npc)
    {
        if (IsFemale.HasValue && npc.IsFemale != IsFemale.Value)
            return false;

        if (IsUnique.HasValue && npc.IsUnique != IsUnique.Value)
            return false;

        if (IsTemplated.HasValue)
        {
            var isTemplated = npc.TemplateFormKey.HasValue;
            if (isTemplated != IsTemplated.Value)
                return false;
        }

        if (IsChild.HasValue && npc.IsChild != IsChild.Value)
            return false;

        if (IsSummonable.HasValue && npc.IsSummonable != IsSummonable.Value)
            return false;

        if (IsLeveled.HasValue && npc.IsLeveled != IsLeveled.Value)
            return false;

        if (Factions.Count > 0)
        {
            var npcFactionKeys = npc.Factions.Select(f => f.FactionFormKey).ToHashSet();
            if (!Factions.All(f => npcFactionKeys.Contains(f)))
                return false;
        }

        if (Races.Count > 0)
        {
            if (!npc.RaceFormKey.HasValue || !Races.Contains(npc.RaceFormKey.Value))
                return false;
        }

        // Keyword matching requires LinkCache - handled in ViewModel
        if (Keywords.Count > 0)
        {
        }

        if (MinLevel.HasValue && npc.Level < MinLevel.Value)
            return false;

        return !MaxLevel.HasValue || npc.Level <= MaxLevel.Value;
    }
}
