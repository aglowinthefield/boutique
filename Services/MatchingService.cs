using Boutique.Models;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public class MatchingService : IMatchingService
{
    public IEnumerable<ArmorMatch> AutoMatchArmors(
        IEnumerable<IArmorGetter> sourceArmors,
        IEnumerable<IArmorGetter> targetArmors,
        double confidenceThreshold = 0.6)
    {
        var targetList = targetArmors.ToList();
        var matches = new List<ArmorMatch>();

        foreach (var source in sourceArmors)
        {
            var sourceName = source.Name?.String ?? source.EditorID ?? "";
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                matches.Add(new ArmorMatch(source));
                continue;
            }

            // Find best match
            IArmorGetter? bestMatch = null;
            double bestScore = 0;

            foreach (var target in targetList)
            {
                var targetName = target.Name?.String ?? target.EditorID ?? "";
                if (string.IsNullOrWhiteSpace(targetName))
                    continue;

                var similarity = CalculateSimilarity(sourceName, targetName);

                if (similarity > bestScore)
                {
                    bestScore = similarity;
                    bestMatch = target;
                }
            }

            // Only add match if it meets confidence threshold
            if (bestMatch != null && bestScore >= confidenceThreshold)
                matches.Add(new ArmorMatch(source, bestMatch, bestScore, false));
            else
                matches.Add(new ArmorMatch(source, matchConfidence: bestScore));
        }

        return matches;
    }

    public double CalculateSimilarity(string sourceName, string targetName)
    {
        if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName))
            return 0;

        var source = NormalizeName(sourceName);
        var target = NormalizeName(targetName);

        // Check for exact match
        if (source == target)
            return 1.0;

        // Check if one contains the other
        if (source.Contains(target) || target.Contains(source))
            return 0.8;

        // Check for common armor type keywords
        var sourceTokens = source.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var targetTokens = target.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        // Check for armor piece type match (boots, gauntlets, helmet, etc.)
        var armorTypes = new[] { "boots", "gauntlets", "gloves", "helmet", "hood", "cuirass", "armor", "shield" };
        var sourceType = sourceTokens.FirstOrDefault(t => armorTypes.Contains(t));
        var targetType = targetTokens.FirstOrDefault(t => armorTypes.Contains(t));

        if (sourceType != null && targetType != null && sourceType != targetType)
            return 0; // Different armor types don't match

        // Calculate Jaccard similarity
        var intersection = sourceTokens.Intersect(targetTokens).Count();
        var union = sourceTokens.Union(targetTokens).Count();

        if (union == 0)
            return 0;

        var jaccardSimilarity = (double)intersection / union;

        // Boost score if armor type matches
        if (sourceType != null && targetType != null && sourceType == targetType)
            jaccardSimilarity = Math.Min(1.0, jaccardSimilarity + 0.2);

        return jaccardSimilarity;
    }

    public IEnumerable<IGrouping<string, IArmorGetter>> GroupByOutfit(IEnumerable<IArmorGetter> armors)
    {
        return armors.GroupBy(armor =>
        {
            var name = armor.Name?.String ?? armor.EditorID ?? "";

            // Extract base outfit name by removing armor type suffixes
            var normalized = NormalizeName(name);
            var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            // Remove common armor type words
            var armorTypes = new[]
                { "boots", "gauntlets", "gloves", "helmet", "hood", "cuirass", "armor", "shield", "bracers" };
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