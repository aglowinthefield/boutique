using Boutique.Models;

namespace Boutique.Services;

public interface INpcScanningService
{
    /// <summary>
    /// Scans all NPCs from the modlist using the LinkCache
    /// </summary>
    Task<IReadOnlyList<NpcRecord>> ScanNpcsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Scans all NPCs and collects full filter data needed for SPID matching.
    /// This includes keywords, factions, race, gender, level, and other properties.
    /// </summary>
    Task<IReadOnlyList<NpcFilterData>> ScanNpcsWithFilterDataAsync(CancellationToken cancellationToken = default);
}

