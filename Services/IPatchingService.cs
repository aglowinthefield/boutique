using RequiemGlamPatcher.Models;

namespace RequiemGlamPatcher.Services;

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
}
