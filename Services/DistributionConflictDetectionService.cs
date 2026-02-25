using System.Globalization;
using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public static class DistributionConflictDetectionService
{
  public static ConflictDetectionResult DetectConflicts(
    IReadOnlyList<DistributionEntryViewModel> entries,
    IReadOnlyList<DistributionFileViewModel> existingFiles,
    string newFileName,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var npcFormKeysInEntries = entries
                               .SelectMany(e => e.SelectedNpcs)
                               .Select(npc => npc.FormKey)
                               .ToHashSet();

    if (npcFormKeysInEntries.Count == 0)
    {
      return new ConflictDetectionResult(false, false, string.Empty, false, false, string.Empty, newFileName, [], []);
    }

    var (existingDistributions, allNpcsDistributions) = BuildExistingDistributionMap(existingFiles, linkCache);
    var allIssues = CollectNpcIssues(entries, existingDistributions, allNpcsDistributions);

    var conflicts = allIssues.Where(i => i.IsHardConflict).ToList();
    var overlaps  = allIssues.Where(i => i.IsProbabilisticOverlap).ToList();

    var conflictingFileNames = conflicts.Select(c => c.ExistingFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var overlappingFileNames = overlaps.Select(o => o.ExistingFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    var loadsAfterConflicts = DoesFileLoadAfterAll(newFileName, conflictingFileNames);
    var loadsAfterOverlaps  = DoesFileLoadAfterAll(newFileName, overlappingFileNames);

    var hasConflicts                = conflicts.Count > 0 && !loadsAfterConflicts;
    var conflictsResolvedByFilename = conflicts.Count > 0 && loadsAfterConflicts;
    var hasOverlaps                 = overlaps.Count > 0 && !loadsAfterOverlaps;
    var overlapsResolvedByFilename  = overlaps.Count > 0 && loadsAfterOverlaps;

    var hasAllNpcsConflict = allNpcsDistributions.Count > 0 &&
                             conflicts.Any(c => allNpcsDistributions.Exists(d => d.FileName == c.ExistingFileName));
    var hasAllNpcsOverlap = allNpcsDistributions.Count > 0 &&
                            overlaps.Any(o => allNpcsDistributions.Exists(d => d.FileName == o.ExistingFileName));

    var conflictAllNpcsNote = hasAllNpcsConflict
                                ? $" ('{allNpcsDistributions[0].FileName}' targets all NPCs)"
                                : string.Empty;
    var overlapAllNpcsNote = hasAllNpcsOverlap
                               ? $" ('{allNpcsDistributions[0].FileName}' targets all NPCs)"
                               : string.Empty;

    string conflictAllNpcsMsg = string.Empty;
    if (hasAllNpcsConflict)
    {
      var d = allNpcsDistributions[0];
      conflictAllNpcsMsg =
        $"⚠ '{d.FileName}' distributes outfit '{d.OutfitName}' to ALL NPCs (100% chance).{Environment.NewLine}" +
        $"All {conflicts.Count} NPC(s) in your entries will be overridden.";
    }

    string overlapAllNpcsMsg = string.Empty;
    if (hasAllNpcsOverlap)
    {
      var d = allNpcsDistributions[0];
      overlapAllNpcsMsg =
        $"ℹ '{d.FileName}' distributes outfit '{d.OutfitName}' to ALL NPCs ({d.Chance}% chance).{Environment.NewLine}" +
        $"{overlaps.Count} NPC(s) in your entries may receive multiple outfits probabilistically.";
    }

    var conflictSummary = BuildIssueSummary(
      conflicts,
      loadsAfterConflicts,
      hasAllNpcsConflict,
      $"✓ {conflicts.Count} NPC(s) have 100% chance distributions{conflictAllNpcsNote}, but your filename '{newFileName}' will load after them.",
      conflictAllNpcsMsg,
      $"⚠ {conflicts.Count} NPC(s) already have 100% chance outfit distributions:",
      c => $"  • {c.DisplayName ?? c.NpcFormKey.ToString()} ({c.ExistingFileName})");

    var overlapSummary = BuildIssueSummary(
      overlaps,
      loadsAfterOverlaps,
      hasAllNpcsOverlap,
      $"ℹ {overlaps.Count} NPC(s) have chance-based distributions{overlapAllNpcsNote}. Your file will load after them.",
      overlapAllNpcsMsg,
      $"ℹ {overlaps.Count} NPC(s) have chance-based distributions (can coexist probabilistically):",
      o => $"  • {o.DisplayName ?? o.NpcFormKey.ToString()} ({o.ExistingFileName}, {o.ExistingChance}% chance)");

    var suggestedFileName = conflicts.Count > 0 && !loadsAfterConflicts
                              ? CalculateZPrefixedFileName(newFileName, conflictingFileNames)
                              : newFileName;

    return new ConflictDetectionResult(
      hasConflicts,
      conflictsResolvedByFilename,
      conflictSummary,
      hasOverlaps,
      overlapsResolvedByFilename,
      overlapSummary,
      suggestedFileName,
      conflicts,
      overlaps);
  }

  private static List<NpcConflictInfo> CollectNpcIssues(
    IReadOnlyList<DistributionEntryViewModel> entries,
    Dictionary<FormKey, (string FileName, string? OutfitName, int Chance)> existingDistributions,
    List<(string FileName, string? OutfitName, int Chance)> allNpcsDistributions)
  {
    var issues = new List<NpcConflictInfo>();

    foreach (var entry in entries)
    {
      var newChance = entry.UseChance ? entry.Chance : 100;

      foreach (var npcVm in entry.SelectedNpcs)
      {
        var found = existingDistributions.TryGetValue(npcVm.FormKey, out var existing);

        if (!found && allNpcsDistributions.Count > 0)
        {
          existing = allNpcsDistributions[0];
        }
        else if (!found)
        {
          continue;
        }

        issues.Add(new NpcConflictInfo(
          npcVm.FormKey,
          npcVm.DisplayName,
          existing.FileName,
          existing.Chance,
          newChance));
      }
    }

    return issues;
  }

  private static string BuildIssueSummary(
    List<NpcConflictInfo> issues,
    bool loadsAfter,
    bool hasAllNpcsIssue,
    string resolvedMessage,
    string allNpcsMessage,
    string individualHeader,
    Func<NpcConflictInfo, string> formatItem)
  {
    if (issues.Count == 0)
    {
      return string.Empty;
    }

    if (loadsAfter)
    {
      return resolvedMessage;
    }

    if (hasAllNpcsIssue)
    {
      return allNpcsMessage;
    }

    var sb = new StringBuilder();
    sb.AppendLine(individualHeader);

    foreach (var issue in issues.Take(5))
    {
      sb.AppendLine(formatItem(issue));
    }

    if (issues.Count > 5)
    {
      sb.Append(CultureInfo.InvariantCulture, $"  ... and {issues.Count - 5} more").AppendLine();
    }

    return sb.ToString().TrimEnd();
  }

  private static bool DoesFileLoadAfterAll(string fileName, HashSet<string> conflictingFileNames)
  {
    if (string.IsNullOrWhiteSpace(fileName))
    {
      return false;
    }

    if (conflictingFileNames.Count == 0)
    {
      return true;
    }

    foreach (var conflictingFile in conflictingFileNames)
    {
      if (string.Compare(fileName, conflictingFile, StringComparison.OrdinalIgnoreCase) <= 0)
      {
        return false;
      }
    }

    return true;
  }

  private static (Dictionary<FormKey, (string FileName, string? OutfitName, int Chance)> SpecificNpcDistributions,
    List<(string FileName, string? OutfitName, int Chance)> AllNpcsDistributions)
    BuildExistingDistributionMap(
      IReadOnlyList<DistributionFileViewModel> files,
      ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var map                  = new Dictionary<FormKey, (string FileName, string? OutfitName, int Chance)>();
    var allNpcsDistributions = new List<(string FileName, string? OutfitName, int Chance)>();

    var                             hasSpidFiles  = files.Any(f => f.TypeDisplay == "SPID");
    Dictionary<string, INpcGetter>? npcByEditorId = null;
    Dictionary<string, INpcGetter>? npcByName     = null;

    if (hasSpidFiles)
    {
      var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
      npcByEditorId = allNpcs
                      .Where(n => !string.IsNullOrWhiteSpace(n.EditorID))
                      .GroupBy(n => n.EditorID!, StringComparer.OrdinalIgnoreCase)
                      .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
      npcByName = allNpcs
                  .Where(n => !string.IsNullOrWhiteSpace(n.Name?.String))
                  .GroupBy(n => n.Name!.String!, StringComparer.OrdinalIgnoreCase)
                  .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    foreach (var file in files)
    {
      foreach (var line in file.Lines.Where(l => l.IsOutfitDistribution))
      {
        var outfitName = DistributionLineParser.ExtractOutfitNameFromLine(line, linkCache);
        var chance     = DistributionLineParser.ExtractChanceFromLine(file, line);

        if (DistributionLineParser.LineTargetsAllNpcs(file, line))
        {
          if (allNpcsDistributions.Count == 0)
          {
            allNpcsDistributions.Add((file.FileName, outfitName, chance));
          }

          continue;
        }

        var npcFormKeys =
          DistributionLineParser.ExtractNpcFormKeysFromLine(file, line, linkCache, npcByEditorId, npcByName);

        foreach (var npcFormKey in npcFormKeys)
        {
          map.TryAdd(npcFormKey, (file.FileName, outfitName, chance));
        }
      }
    }

    return (map, allNpcsDistributions);
  }

  private static string CalculateZPrefixedFileName(string newFileName, HashSet<string> conflictingFileNames)
  {
    if (string.IsNullOrWhiteSpace(newFileName) || conflictingFileNames.Count == 0)
    {
      return newFileName;
    }

    var maxZCount = 0;
    foreach (var fileName in conflictingFileNames)
    {
      var zCount = 0;
      foreach (var c in fileName)
      {
        if (c == 'Z' || c == 'z')
        {
          zCount++;
        }
        else
        {
          break;
        }
      }

      maxZCount = Math.Max(maxZCount, zCount);
    }

    var zPrefix  = new string('Z', maxZCount + 1);
    var baseName = newFileName.TrimStart('Z', 'z');

    return zPrefix + baseName;
  }

  public static void ApplyConflictIndicators(
    ConflictDetectionResult result,
    IEnumerable<DistributionEntryViewModel> entries)
  {
    var conflictNpcFormKeys = result.Conflicts
                                    .Select(c => c.NpcFormKey)
                                    .ToHashSet();
    var overlapNpcFormKeys = result.Overlaps
                                   .Select(o => o.NpcFormKey)
                                   .ToHashSet();

    foreach (var entry in entries)
    {
      foreach (var npcVm in entry.SelectedNpcs)
      {
        if (conflictNpcFormKeys.Contains(npcVm.FormKey))
        {
          var conflict = result.Conflicts.First(c => c.NpcFormKey == npcVm.FormKey);
          npcVm.HasConflict         = !result.ConflictsResolvedByFilename;
          npcVm.ConflictingFileName = conflict.ExistingFileName;
        }
        else
        {
          npcVm.HasConflict         = false;
          npcVm.ConflictingFileName = null;
        }

        if (overlapNpcFormKeys.Contains(npcVm.FormKey))
        {
          var overlap = result.Overlaps.First(o => o.NpcFormKey == npcVm.FormKey);
          npcVm.HasOverlap         = !result.OverlapsResolvedByFilename;
          npcVm.OverlappingFileName = overlap.ExistingFileName;
        }
        else
        {
          npcVm.HasOverlap         = false;
          npcVm.OverlappingFileName = null;
        }
      }
    }
  }

  public static void ClearConflictIndicators(IEnumerable<DistributionEntryViewModel> entries)
  {
    foreach (var entry in entries)
    {
      foreach (var npc in entry.SelectedNpcs)
      {
        npc.HasConflict         = false;
        npc.ConflictingFileName = null;
        npc.HasOverlap          = false;
        npc.OverlappingFileName = null;
      }
    }
  }
}
