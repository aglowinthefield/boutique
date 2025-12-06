using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Boutique.Models;

namespace Boutique.Services;

public interface INpcOutfitResolutionService
{
    /// <summary>
    /// Scans all distribution files and resolves final outfit assignments for all NPCs.
    /// Files are processed in order: SPID files alphabetically, then SkyPatcher files alphabetically.
    /// The last file in processing order wins for each NPC.
    /// </summary>
    /// <param name="distributionFiles">The discovered distribution files</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of NPC outfit assignments with conflict detection</returns>
    Task<IReadOnlyList<NpcOutfitAssignment>> ResolveNpcOutfitsAsync(
        IReadOnlyList<DistributionFile> distributionFiles,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Scans all distribution files and resolves final outfit assignments for all NPCs,
    /// using pre-scanned NPC filter data for full SPID filter matching.
    /// </summary>
    /// <param name="distributionFiles">The discovered distribution files</param>
    /// <param name="npcFilterData">Pre-scanned NPC data with keywords, factions, etc.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of NPC outfit assignments with conflict detection and targeting info</returns>
    Task<IReadOnlyList<NpcOutfitAssignment>> ResolveNpcOutfitsWithFiltersAsync(
        IReadOnlyList<DistributionFile> distributionFiles,
        IReadOnlyList<NpcFilterData> npcFilterData,
        CancellationToken cancellationToken = default);
}
