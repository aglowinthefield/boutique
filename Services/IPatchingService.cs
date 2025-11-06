using System.Collections.Generic;
using Boutique.Models;

namespace Boutique.Services;

public interface IPatchingService
{
    /// <summary>
    /// Creates a patch ESP with the matched armors
    /// </summary>
    Task<(bool success, string message)> CreatePatchAsync(
        IEnumerable<ArmorMatch> matches,
        string outputPath,
        IProgress<(int current, int total, string message)>? progress = null);

    /// <summary>
    /// Validates that the patch can be created
    /// </summary>
    bool ValidatePatch(IEnumerable<ArmorMatch> matches, out string validationMessage);

    /// <summary>
    /// Creates or updates outfit records in the patch ESP
    /// </summary>
    Task<(bool success, string message, IReadOnlyList<OutfitCreationResult> results)> CreateOrUpdateOutfitsAsync(
        IEnumerable<OutfitCreationRequest> outfits,
        string outputPath,
        IProgress<(int current, int total, string message)>? progress = null);
}
