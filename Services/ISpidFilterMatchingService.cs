using Boutique.Models;

namespace Boutique.Services;

/// <summary>
/// Service for matching NPCs against SPID distribution filters.
/// </summary>
public interface ISpidFilterMatchingService
{
    /// <summary>
    /// Determines if an NPC matches the given SPID distribution filter.
    /// </summary>
    /// <param name="npc">The NPC's filter data.</param>
    /// <param name="filter">The SPID distribution filter to match against.</param>
    /// <returns>True if the NPC matches all filter criteria.</returns>
    bool NpcMatchesFilter(NpcFilterData npc, SpidDistributionFilter filter);
    
    /// <summary>
    /// Gets all NPCs that match the given SPID distribution filter.
    /// </summary>
    /// <param name="allNpcs">All available NPCs with filter data.</param>
    /// <param name="filter">The SPID distribution filter to match against.</param>
    /// <returns>List of matching NPCs.</returns>
    IReadOnlyList<NpcFilterData> GetMatchingNpcs(IReadOnlyList<NpcFilterData> allNpcs, SpidDistributionFilter filter);
}

