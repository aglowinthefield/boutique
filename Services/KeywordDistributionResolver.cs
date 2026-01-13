using Boutique.Models;
using Boutique.Utilities;
using Serilog;

namespace Boutique.Services;

/// <summary>
/// Resolves SPID keyword distribution dependencies and simulates keyword assignment to NPCs.
/// </summary>
public class KeywordDistributionResolver(ILogger logger)
{
    private readonly ILogger _logger = logger.ForContext<KeywordDistributionResolver>();

    /// <summary>
    /// Parses all keyword distribution entries from a list of distribution files.
    /// </summary>
    public IReadOnlyList<KeywordDistributionEntry> ParseKeywordDistributions(IReadOnlyList<DistributionFile> files)
    {
        var entries = new List<KeywordDistributionEntry>();

        foreach (var file in files)
        {
            if (file.Type != DistributionFileType.Spid)
                continue;

            foreach (var line in file.Lines)
            {
                if (!line.IsKeywordDistribution)
                    continue;

                if (SpidLineParser.TryParseKeyword(line.RawText, out var filter) && filter != null)
                {
                    entries.Add(KeywordDistributionEntry.FromFilter(filter, file.FullPath, line.LineNumber));
                }
            }
        }

        _logger.Debug("Parsed {Count} keyword distribution entries from {FileCount} files", entries.Count, files.Count);
        return entries;
    }

    /// <summary>
    /// Builds a dependency graph and returns keywords in topological order (dependencies first).
    /// </summary>
    public (IReadOnlyList<KeywordDistributionEntry> Sorted, IReadOnlyList<string> CyclicKeywords) TopologicalSort(
        IReadOnlyList<KeywordDistributionEntry> entries)
    {
        var keywordToEntry = new Dictionary<string, KeywordDistributionEntry>(StringComparer.OrdinalIgnoreCase);
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var keyword = entry.KeywordIdentifier;
            keywordToEntry[keyword] = entry;
            graph.TryAdd(keyword, []);
            inDegree.TryAdd(keyword, 0);
        }

        foreach (var entry in entries)
        {
            var keyword = entry.KeywordIdentifier;
            var dependencies = entry.GetReferencedKeywords();

            foreach (var dep in dependencies)
            {
                if (!keywordToEntry.ContainsKey(dep))
                    continue;

                if (graph[dep].Add(keyword))
                {
                    inDegree[keyword] = inDegree.GetValueOrDefault(keyword, 0) + 1;
                }
            }
        }

        var queue = new Queue<string>();
        foreach (var (keyword, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(keyword);
        }

        var sorted = new List<KeywordDistributionEntry>();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            processed.Add(current);

            if (keywordToEntry.TryGetValue(current, out var entry))
                sorted.Add(entry);

            if (!graph.TryGetValue(current, out var dependents))
                continue;

            foreach (var dependent in dependents)
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        var cyclicKeywords = keywordToEntry.Keys
            .Where(k => !processed.Contains(k))
            .ToList();

        if (cyclicKeywords.Count > 0)
        {
            _logger.Warning("Detected circular keyword dependencies: {Keywords}", string.Join(", ", cyclicKeywords));
        }

        _logger.Debug("Topological sort complete: {SortedCount} keywords sorted, {CyclicCount} cyclic",
            sorted.Count, cyclicKeywords.Count);

        return (sorted, cyclicKeywords);
    }

    /// <summary>
    /// Simulates keyword distribution for all NPCs, returning a dictionary of NPC FormKey -> assigned keywords.
    /// </summary>
    public Dictionary<Mutagen.Bethesda.Plugins.FormKey, HashSet<string>> SimulateKeywordDistribution(
        IReadOnlyList<KeywordDistributionEntry> sortedKeywords,
        IReadOnlyList<NpcFilterData> allNpcs)
    {
        var npcKeywords = new Dictionary<Mutagen.Bethesda.Plugins.FormKey, HashSet<string>>();

        foreach (var npc in allNpcs)
        {
            npcKeywords[npc.FormKey] = new HashSet<string>(npc.Keywords, StringComparer.OrdinalIgnoreCase);
        }

        _logger.Debug("Starting keyword simulation for {NpcCount} NPCs with {KeywordCount} keyword distributions",
            allNpcs.Count, sortedKeywords.Count);

        foreach (var entry in sortedKeywords)
        {
            var matchingNpcs = GetMatchingNpcs(entry, allNpcs, npcKeywords);

            foreach (var npc in matchingNpcs)
            {
                if (ShouldApplyChance(entry.Chance, npc.FormKey))
                {
                    npcKeywords[npc.FormKey].Add(entry.KeywordIdentifier);
                }
            }

            _logger.Debug("Keyword {Keyword}: matched {MatchCount} NPCs (chance: {Chance}%)",
                entry.KeywordIdentifier, matchingNpcs.Count, entry.Chance);
        }

        var totalAssignments = npcKeywords.Values.Sum(k => k.Count);
        _logger.Information("Keyword simulation complete: {TotalAssignments} total keyword assignments",
            totalAssignments);

        return npcKeywords;
    }

    /// <summary>
    /// Gets the set of virtual (SPID-distributed) keywords for a specific NPC.
    /// </summary>
    public static IReadOnlySet<string> GetVirtualKeywordsForNpc(
        Mutagen.Bethesda.Plugins.FormKey npcFormKey,
        Dictionary<Mutagen.Bethesda.Plugins.FormKey, HashSet<string>> simulatedKeywords,
        NpcFilterData npc)
    {
        if (!simulatedKeywords.TryGetValue(npcFormKey, out var keywords))
            return npc.Keywords;

        var combined = new HashSet<string>(npc.Keywords, StringComparer.OrdinalIgnoreCase);
        combined.UnionWith(keywords);
        return combined;
    }

    private static IReadOnlyList<NpcFilterData> GetMatchingNpcs(
        KeywordDistributionEntry entry,
        IReadOnlyList<NpcFilterData> allNpcs,
        Dictionary<Mutagen.Bethesda.Plugins.FormKey, HashSet<string>> currentKeywords)
    {
        var filter = new SpidDistributionFilter
        {
            FormType = SpidFormType.Keyword,
            FormIdentifier = entry.KeywordIdentifier,
            StringFilters = entry.StringFilters,
            FormFilters = entry.FormFilters,
            LevelFilters = entry.LevelFilters,
            TraitFilters = entry.TraitFilters,
            Chance = entry.Chance,
            RawLine = entry.RawLine
        };

        return SpidFilterMatchingService.GetMatchingNpcsWithVirtualKeywords(allNpcs, filter, currentKeywords);
    }

    private static bool ShouldApplyChance(int chance, Mutagen.Bethesda.Plugins.FormKey npcFormKey)
    {
        if (chance >= 100)
            return true;

        if (chance <= 0)
            return false;

        var hash = npcFormKey.GetHashCode();
        var randomValue = Math.Abs(hash) % 100;
        return randomValue < chance;
    }
}
