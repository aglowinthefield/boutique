using Boutique.Models;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public interface IMatchingService
{
    /// <summary>
    ///     Attempts to auto-match source armors with target armors based on name similarity
    /// </summary>
    IEnumerable<ArmorMatch> AutoMatchArmors(
        IEnumerable<IArmorGetter> sourceArmors,
        IEnumerable<IArmorGetter> targetArmors,
        double confidenceThreshold = 0.6);

    /// <summary>
    ///     Calculates similarity score between two armor names
    /// </summary>
    double CalculateSimilarity(string sourceName, string targetName);

    /// <summary>
    ///     Groups armors by outfit set based on shared keywords or name patterns
    /// </summary>
    IEnumerable<IGrouping<string, IArmorGetter>> GroupByOutfit(IEnumerable<IArmorGetter> armors);
}