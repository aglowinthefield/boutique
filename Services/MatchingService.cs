using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public class MatchingService
{
    public IEnumerable<IGrouping<string, IArmorGetter>> GroupByOutfit(IEnumerable<IArmorGetter> armors)
    {
        return armors.GroupBy(armor =>
        {
            var name = armor.Name?.String ?? armor.EditorID ?? "";

            // Extract base outfit name by removing armor type suffixes
            var normalized = NormalizeName(name);
            var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            // Remove common armor type words
            var armorTypes = new[] { "boots", "gauntlets", "gloves", "helmet", "hood", "cuirass", "armor", "shield", "bracers" };
            var baseTokens = tokens.Where(t => !armorTypes.Contains(t)).ToList();

            return string.Join(" ", baseTokens);
        });
    }

    private static string NormalizeName(string name)
    {
        return name.ToLowerInvariant()
            .Replace("(", "")
            .Replace(")", "")
            .Replace("[", "")
            .Replace("]", "")
            .Replace("_", " ")
            .Replace("-", " ")
            .Trim();
    }
}