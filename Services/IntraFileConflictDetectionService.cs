using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;

namespace Boutique.Services;

public static class IntraFileConflictDetectionService
{
  public static IntraFileConflictResult Detect(
    IReadOnlyList<DistributionEntryViewModel> outfitEntries,
    IReadOnlyList<NpcFilterData> allNpcs,
    IReadOnlyDictionary<FormKey, HashSet<string>>? virtualKeywordsByNpc = null)
  {
    var outfitToNpcs = new Dictionary<string, HashSet<FormKey>>();

    if (allNpcs.Count > 0)
    {
      foreach (var entry in outfitEntries)
      {
        if (entry.Entry.HasUnresolvedFilters)
        {
          continue;
        }

        var outfitName = entry.SelectedOutfit?.EditorID ?? "(no outfit)";
        var matchingNpcs = FilterMatchingService.GetMatchingNpcsForEntry(
          allNpcs,
          entry.Entry,
          virtualKeywordsByNpc);

        if (!outfitToNpcs.TryGetValue(outfitName, out var npcSet))
        {
          npcSet = [];
          outfitToNpcs[outfitName] = npcSet;
        }

        foreach (var npc in matchingNpcs)
        {
          npcSet.Add(npc.FormKey);
        }
      }
    }
    else
    {
      foreach (var entry in outfitEntries)
      {
        var outfitName = entry.SelectedOutfit?.EditorID ?? "(no outfit)";

        if (!outfitToNpcs.TryGetValue(outfitName, out var npcSet))
        {
          npcSet = [];
          outfitToNpcs[outfitName] = npcSet;
        }

        foreach (var npc in entry.SelectedNpcs.Where(n => !n.IsExcluded))
        {
          npcSet.Add(npc.FormKey);
        }
      }
    }

    var names = outfitToNpcs.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    var pairCounts = new Dictionary<(string, string), int>();
    var allOverlappingNpcs = new HashSet<FormKey>();

    for (var i = 0; i < names.Count; i++)
    {
      for (var j = i + 1; j < names.Count; j++)
      {
        var setA = outfitToNpcs[names[i]];
        var setB = outfitToNpcs[names[j]];
        var shared = setA.Intersect(setB).ToList();

        if (shared.Count > 0)
        {
          pairCounts[(names[i], names[j])] = shared.Count;
          foreach (var npc in shared)
          {
            allOverlappingNpcs.Add(npc);
          }
        }
      }
    }

    return new IntraFileConflictResult(allOverlappingNpcs.Count, names, pairCounts);
  }
}
