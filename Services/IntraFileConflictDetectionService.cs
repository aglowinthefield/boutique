using System.Globalization;
using System.Text;
using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;

namespace Boutique.Services;

public static class IntraFileConflictDetectionService
{
  public static IntraFileConflictResult Detect(
    IReadOnlyList<DistributionEntryViewModel> outfitEntries,
    IReadOnlyList<NpcFilterData> allNpcs)
  {
    List<(string NpcName, int EntryCount, List<string> Outfits, bool IsHardConflict)> issues;

    if (allNpcs.Count > 0)
    {
      var npcToEntries = new Dictionary<FormKey, List<(DistributionEntryViewModel Entry, string OutfitName)>>();

      foreach (var entry in outfitEntries)
      {
        if (entry.Entry.HasUnresolvedFilters)
        {
          continue;
        }

        var outfitName   = entry.SelectedOutfit?.EditorID ?? "(no outfit)";
        var matchingNpcs = SpidFilterMatchingService.GetMatchingNpcsForEntry(allNpcs, entry.Entry);

        foreach (var npc in matchingNpcs)
        {
          if (!npcToEntries.TryGetValue(npc.FormKey, out var entryList))
          {
            entryList                 = [];
            npcToEntries[npc.FormKey] = entryList;
          }

          entryList.Add((entry, outfitName));
        }
      }

      issues =
      [
        .. npcToEntries
           .Where(kv => kv.Value.Count > 1)
           .Select(kv =>
           {
             var npc      = allNpcs.FirstOrDefault(n => n.FormKey == kv.Key);
             var entries  = kv.Value;
             var allAre100Percent = entries.All(e => !e.Entry.UseChance || e.Entry.Chance == 100);
             return (
                      NpcName: npc?.DisplayName ?? kv.Key.ToString(),
                      EntryCount: entries.Count,
                      Outfits: entries.Select(e => e.OutfitName).Distinct().ToList(),
                      IsHardConflict: allAre100Percent);
           })
      ];
    }
    else
    {
      issues = outfitEntries
               .SelectMany(entry => entry.SelectedNpcs
                                         .Where(npc => !npc.IsExcluded)
                                         .Select(npc => (Entry: entry, Npc: npc)))
               .GroupBy(x => x.Npc.FormKey)
               .Where(g => g.Count() > 1)
               .Select(g =>
               {
                 var entries          = g.ToList();
                 var allAre100Percent = entries.All(e => !e.Entry.UseChance || e.Entry.Chance == 100);
                 return (
                          NpcName: g.First().Npc.DisplayName,
                          EntryCount: g.Count(),
                          Outfits: g.Select(x => x.Entry.SelectedOutfit?.EditorID ?? "(no outfit)").Distinct().ToList(),
                          IsHardConflict: allAre100Percent);
               })
               .ToList();
    }

    var conflicts = issues.Where(i => i.IsHardConflict).ToList();
    var overlaps  = issues.Where(i => !i.IsHardConflict).ToList();

    return new IntraFileConflictResult(
      conflicts.Count > 0,
      BuildSummary(conflicts, "NPC(s) have multiple 100% chance entries:"),
      overlaps.Count > 0,
      BuildSummary(overlaps, "NPC(s) have multiple chance-based entries (can coexist probabilistically):"));
  }

  private static string BuildSummary(
    List<(string NpcName, int EntryCount, List<string> Outfits, bool IsHardConflict)> items,
    string headerSuffix)
  {
    if (items.Count == 0)
    {
      return string.Empty;
    }

    var sb = new StringBuilder();
    sb.Append(CultureInfo.InvariantCulture, $"{items.Count} {headerSuffix}").AppendLine();
    foreach (var item in items.Take(5))
    {
      sb.Append(
          CultureInfo.InvariantCulture,
          $"  \u2022 {item.NpcName} ({item.EntryCount}x): {string.Join(", ", item.Outfits)}")
        .AppendLine();
    }

    if (items.Count > 5)
    {
      sb.Append(CultureInfo.InvariantCulture, $"  ... and {items.Count - 5} more").AppendLine();
    }

    return sb.ToString().TrimEnd();
  }
}
