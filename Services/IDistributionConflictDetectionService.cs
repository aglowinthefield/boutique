using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

/// <summary>
/// Service for detecting conflicts between new distribution entries and existing distribution files.
/// </summary>
public interface IDistributionConflictDetectionService
{
    /// <summary>
    /// Detects conflicts between new distribution entries and existing distribution files.
    /// </summary>
    /// <param name="entries">The new distribution entries being created</param>
    /// <param name="existingFiles">Existing distribution files to check against</param>
    /// <param name="newFileName">The filename of the new distribution file</param>
    /// <param name="linkCache">LinkCache for resolving FormKeys</param>
    /// <returns>Conflict detection result with summary and suggested filename</returns>
    ConflictDetectionResult DetectConflicts(
        IReadOnlyList<DistributionEntryViewModel> entries,
        IReadOnlyList<DistributionFileViewModel> existingFiles,
        string newFileName,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache);
}

