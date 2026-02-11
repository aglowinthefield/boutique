using System.Globalization;
using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public class DistributionConflictDetectionService
{
  public static ConflictDetectionResult DetectConflicts(
    IReadOnlyList<DistributionEntryViewModel> entries,
    IReadOnlyList<DistributionFileViewModel> existingFiles,
    string newFileName,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    // Build a set of NPC FormKeys from current entries
    var npcFormKeysInEntries = entries
                               .SelectMany(e => e.SelectedNpcs)
                               .Select(npc => npc.FormKey)
                               .ToHashSet();

    if (npcFormKeysInEntries.Count == 0)
    {
      return new ConflictDetectionResult(
        false,
        false,
        string.Empty,
        false,
        false,
        string.Empty,
        newFileName,
        [],
        []);
    }

    // Build a map of NPC FormKey -> (FileName, OutfitEditorId, Chance) from existing distribution files
    // Also track files that target ALL NPCs
    var (existingDistributions, allNpcsDistributions) = BuildExistingDistributionMap(existingFiles, linkCache);

    // Find conflicts and overlaps
    var allIssues = new List<NpcConflictInfo>();
    var conflictingFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var overlappingFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var entry in entries)
    {
      var newOutfitName = entry.SelectedOutfit?.EditorID ?? entry.SelectedOutfit?.FormKey.ToString();
      var newChance     = entry.UseChance ? entry.Chance : 100;

      foreach (var npcVm in entry.SelectedNpcs)
      {
        // First check for specific NPC conflicts
        if (existingDistributions.TryGetValue(npcVm.FormKey, out var existing))
        {
          var info = new NpcConflictInfo(
            npcVm.FormKey,
            npcVm.DisplayName,
            existing.FileName,
            existing.OutfitName,
            newOutfitName,
            existing.Chance,
            newChance);

          allIssues.Add(info);

          if (info.IsHardConflict)
          {
            conflictingFileNames.Add(existing.FileName);
          }
          else
          {
            overlappingFileNames.Add(existing.FileName);
          }
        }
        else if (allNpcsDistributions.Count > 0)
        {
          // Use the first "all NPCs" distribution as the conflict
          var allNpcsDist = allNpcsDistributions[0];
          var info = new NpcConflictInfo(
            npcVm.FormKey,
            npcVm.DisplayName,
            allNpcsDist.FileName,
            allNpcsDist.OutfitName,
            newOutfitName,
            allNpcsDist.Chance,
            newChance);

          allIssues.Add(info);

          if (info.IsHardConflict)
          {
            conflictingFileNames.Add(allNpcsDist.FileName);
          }
          else
          {
            overlappingFileNames.Add(allNpcsDist.FileName);
          }
        }
      }
    }

    // Separate conflicts from overlaps
    var conflicts = allIssues.Where(i => i.IsHardConflict).ToList();
    var overlaps  = allIssues.Where(i => i.IsProbabilisticOverlap).ToList();

    // Check if the current filename already loads after all conflicting/overlapping files
    var currentFileLoadsAfterConflicts = DoesFileLoadAfterAll(newFileName, conflictingFileNames);
    var currentFileLoadsAfterOverlaps  = DoesFileLoadAfterAll(newFileName, overlappingFileNames);

    // Only show as conflict/overlap if the user's file wouldn't load last
    var hasConflicts                = conflicts.Count > 0 && !currentFileLoadsAfterConflicts;
    var conflictsResolvedByFilename = conflicts.Count > 0 && currentFileLoadsAfterConflicts;
    var hasOverlaps                 = overlaps.Count > 0 && !currentFileLoadsAfterOverlaps;
    var overlapsResolvedByFilename  = overlaps.Count > 0 && currentFileLoadsAfterOverlaps;

    string conflictSummary;
    string overlapSummary;
    string suggestedFileName;

    // Check if conflicts are from an "all NPCs" distribution
    var hasAllNpcsConflict = allNpcsDistributions.Count > 0 &&
                             conflicts.Any(c => allNpcsDistributions.Exists(d => d.FileName == c.ExistingFileName));
    var hasAllNpcsOverlap = allNpcsDistributions.Count > 0 &&
                            overlaps.Any(o => allNpcsDistributions.Exists(d => d.FileName == o.ExistingFileName));

    // Build conflict summary
    if (conflicts.Count > 0)
    {
      if (currentFileLoadsAfterConflicts)
      {
        var allNpcsNote = hasAllNpcsConflict
                            ? $" ('{allNpcsDistributions[0].FileName}' targets all NPCs)"
                            : string.Empty;
        conflictSummary =
          $"✓ {conflicts.Count} NPC(s) have 100% chance distributions{allNpcsNote}, but your filename '{newFileName}' will load after them.";
      }
      else
      {
        var sb = new StringBuilder();

        if (hasAllNpcsConflict)
        {
          sb.Append(
              CultureInfo.InvariantCulture,
              $"⚠ '{allNpcsDistributions[0].FileName}' distributes outfit '{allNpcsDistributions[0].OutfitName}' to ALL NPCs (100% chance).")
            .AppendLine()
            .Append(
              CultureInfo.InvariantCulture,
              $"All {conflicts.Count} NPC(s) in your entries will be overridden.").AppendLine();
        }
        else
        {
          sb.Append(
              CultureInfo.InvariantCulture,
              $"⚠ {conflicts.Count} NPC(s) already have 100% chance outfit distributions:")
            .AppendLine();

          foreach (var conflict in conflicts.Take(5))
          {
            sb.Append(
                CultureInfo.InvariantCulture,
                $"  • {conflict.DisplayName ?? conflict.NpcFormKey.ToString()} ({conflict.ExistingFileName})")
              .AppendLine();
          }

          if (conflicts.Count > 5)
          {
            sb.Append(CultureInfo.InvariantCulture, $"  ... and {conflicts.Count - 5} more").AppendLine();
          }
        }

        conflictSummary = sb.ToString().TrimEnd();
      }
    }
    else
    {
      conflictSummary = string.Empty;
    }

    // Build overlap summary
    if (overlaps.Count > 0)
    {
      if (currentFileLoadsAfterOverlaps)
      {
        var allNpcsNote = hasAllNpcsOverlap
                            ? $" ('{allNpcsDistributions[0].FileName}' targets all NPCs)"
                            : string.Empty;
        overlapSummary =
          $"ℹ {overlaps.Count} NPC(s) have chance-based distributions{allNpcsNote}. Your file will load after them.";
      }
      else
      {
        var sb = new StringBuilder();

        if (hasAllNpcsOverlap)
        {
          var dist = allNpcsDistributions[0];
          sb.Append(
              CultureInfo.InvariantCulture,
              $"ℹ '{dist.FileName}' distributes outfit '{dist.OutfitName}' to ALL NPCs ({dist.Chance}% chance).")
            .AppendLine()
            .Append(
              CultureInfo.InvariantCulture,
              $"{overlaps.Count} NPC(s) in your entries may receive multiple outfits probabilistically.").AppendLine();
        }
        else
        {
          sb.Append(
              CultureInfo.InvariantCulture,
              $"ℹ {overlaps.Count} NPC(s) have chance-based distributions (can coexist probabilistically):")
            .AppendLine();

          foreach (var overlap in overlaps.Take(5))
          {
            sb.Append(
                CultureInfo.InvariantCulture,
                $"  • {overlap.DisplayName ?? overlap.NpcFormKey.ToString()} ({overlap.ExistingFileName}, {overlap.ExistingChance}% chance)")
              .AppendLine();
          }

          if (overlaps.Count > 5)
          {
            sb.Append(CultureInfo.InvariantCulture, $"  ... and {overlaps.Count - 5} more").AppendLine();
          }
        }

        overlapSummary = sb.ToString().TrimEnd();
      }
    }
    else
    {
      overlapSummary = string.Empty;
    }

    // Calculate suggested filename (only based on hard conflicts, not overlaps)
    suggestedFileName = conflicts.Count > 0 && !currentFileLoadsAfterConflicts
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

  private static bool DoesFileLoadAfterAll(string fileName, HashSet<string> conflictingFileNames)
  {
    if (string.IsNullOrWhiteSpace(fileName))
    {
      return false;
    }

    // No conflicting files means we're already "after" all of them (vacuously true)
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

    // Check if we need NPC lookup dictionaries (for SPID files)
    var                             hasSpidFiles  = files.Any(f => f.TypeDisplay == "SPID");
    Dictionary<string, INpcGetter>? npcByEditorId = null;
    Dictionary<string, INpcGetter>? npcByName     = null;

    if (hasSpidFiles)
    {
      // Build NPC lookup dictionaries once for all SPID files
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

        // Check if this line targets all NPCs (no filters)
        if (DistributionLineParser.LineTargetsAllNpcs(file, line))
        {
          // Only track the first "all NPCs" distribution
          if (allNpcsDistributions.Count == 0)
          {
            allNpcsDistributions.Add((file.FileName, outfitName, chance));
          }

          continue;
        }

        // Parse the line to extract specific NPC FormKeys
        var npcFormKeys =
          DistributionLineParser.ExtractNpcFormKeysFromLine(file, line, linkCache, npcByEditorId, npcByName);

        foreach (var npcFormKey in npcFormKeys)
        {
          if (!map.ContainsKey(npcFormKey))
          {
            map[npcFormKey] = (file.FileName, outfitName, chance);
          }
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

    // Find the maximum number of leading Z's in conflicting filenames
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

    // Add one more Z than the maximum
    var zPrefix = new string('Z', maxZCount + 1);

    // Remove any existing Z prefix from the new filename
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
