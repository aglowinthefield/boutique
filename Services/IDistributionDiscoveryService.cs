using Boutique.Models;

namespace Boutique.Services;

public interface IDistributionDiscoveryService
{
    Task<IReadOnlyList<DistributionFile>> DiscoverAsync(string dataFolderPath, CancellationToken cancellationToken = default);
}
