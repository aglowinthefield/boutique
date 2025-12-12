using Boutique.Models;

namespace Boutique.Services;

public interface IDistributionFileWriterService
{
    /// <summary>
    /// Writes a distribution file with the given entries in the specified format
    /// </summary>
    Task WriteDistributionFileAsync(
        string filePath,
        IReadOnlyList<DistributionEntry> entries,
        DistributionFileType format,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads distribution entries from an existing SkyPatcher file
    /// </summary>
    Task<IReadOnlyList<DistributionEntry>> LoadDistributionFileAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads distribution entries and detects the file format (SPID or SkyPatcher)
    /// </summary>
    Task<(IReadOnlyList<DistributionEntry> Entries, DistributionFileType DetectedFormat)> LoadDistributionFileWithFormatAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}

