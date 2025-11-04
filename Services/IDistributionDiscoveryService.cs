using RequiemGlamPatcher.Models;

namespace RequiemGlamPatcher.Services;

public interface IDistributionDiscoveryService
{
    Task<IReadOnlyList<DistributionFile>> DiscoverAsync(string dataFolderPath, CancellationToken cancellationToken = default);
}
