using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public interface IMatchingService
{
    /// <summary>
    ///     Groups armors by outfit set based on shared keywords or name patterns
    /// </summary>
    IEnumerable<IGrouping<string, IArmorGetter>> GroupByOutfit(IEnumerable<IArmorGetter> armors);
}