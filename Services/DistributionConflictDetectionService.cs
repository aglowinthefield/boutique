using System.Globalization;
using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

/// <summary>
/// Detects distribution conflicts both within a single file (intra-file overlaps where multiple
/// entries target the same NPC) and across existing distribution files (hard conflicts and
/// probabilistic overlaps based on NPC assignments).
/// </summary>
public static class DistributionConflictDetectionService
{
  public static IntraFileConflictResult DetectIntraFile(
    IReadOnlyList<DistributionEntryViewModel> outfitEntries,
    IReadOnlyList<NpcFilterData> allNpcs,
    IReadOnlyDictionary<FormKey, HashSet<string>>? virtualKeywordsByNpc = null)
  {
    var outfitToNpcs = new Dictionary<string, HashSet<FormKey>>();

    if (allNpcs.Count > 0)
    {
      foreach (var entry in outfitEntries)
      {
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

        foreach (var npc in matchingNpcs.Where(n => n.IsConfirmed))
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

  /// <summary>
  /// Captures owned matching inputs before yielding. Keep the borrowed link cache alive until completion.
  /// Callers can await this result for save decisions without depending on a background preview refresh.
  /// </summary>
  public static async Task<ConflictDetectionResult> DetectConflictsAsync(
    IReadOnlyList<DistributionEntryViewModel> entries,
    IReadOnlyList<DistributionFileViewModel> existingFiles,
    string newFileName,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<NpcFilterData>? allNpcs = null,
    IReadOnlyDictionary<FormKey, HashSet<string>>? virtualKeywordsByNpc = null)
  {
    var capturedEntries = entries.Select(entry =>
    {
      var captured = new DistributionEntryViewModel(entry.Entry.Copy());
      if (allNpcs is not { Count: > 0 })
      {
        foreach (var npc in entry.SelectedNpcs)
        {
          captured.SelectedNpcs.Add(new NpcRecordViewModel(npc.NpcRecord) { IsExcluded = npc.IsExcluded });
        }
      }

      return captured;
    }).ToList();
    var capturedFiles = existingFiles.Select(file => new DistributionFileViewModel(new DistributionFile(
      file.FileName, file.FullPath, file.RelativePath, file.Type,
      file.Lines.Select(line => line with { OutfitFormKeys = line.OutfitFormKeys.ToArray() }).ToArray(),
      file.OutfitCount, file.KeywordCount, file.ExclusiveGroupCount))).ToList();
    var capturedNpcs = allNpcs?.Select(SnapshotNpc).ToList();
    var capturedKeywords = virtualKeywordsByNpc?.ToDictionary(
      pair => pair.Key,
      pair => new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase));

    await Task.Yield();
    return await Task.Run(() => DetectConflicts(capturedEntries, capturedFiles, newFileName,
      linkCache, capturedNpcs, capturedKeywords)).ConfigureAwait(false);
  }

  private static NpcFilterData SnapshotNpc(NpcFilterData npc) => new()
  {
    FormKey = npc.FormKey,
    EditorId = npc.EditorId,
    Name = npc.Name,
    SourceMod = npc.SourceMod,
    Keywords = new HashSet<string>(npc.Keywords, StringComparer.OrdinalIgnoreCase),
    Factions = npc.Factions.ToArray(),
    RaceFormKey = npc.RaceFormKey,
    RaceEditorId = npc.RaceEditorId,
    ClassFormKey = npc.ClassFormKey,
    ClassEditorId = npc.ClassEditorId,
    CombatStyleFormKey = npc.CombatStyleFormKey,
    CombatStyleEditorId = npc.CombatStyleEditorId,
    VoiceTypeFormKey = npc.VoiceTypeFormKey,
    VoiceTypeEditorId = npc.VoiceTypeEditorId,
    DefaultOutfitFormKey = npc.DefaultOutfitFormKey,
    DefaultOutfitEditorId = npc.DefaultOutfitEditorId,
    WornArmorFormKey = npc.WornArmorFormKey,
    IsFemale = npc.IsFemale,
    IsUnique = npc.IsUnique,
    IsSummonable = npc.IsSummonable,
    IsChild = npc.IsChild,
    IsLeveled = npc.IsLeveled,
    Level = npc.Level,
    Locations = new HashSet<FormKey>(npc.Locations),
    TemplateFormKey = npc.TemplateFormKey,
    TemplateEditorId = npc.TemplateEditorId,
    MatchKeys = npc.MatchKeys == null ? null : new HashSet<string>(npc.MatchKeys, npc.MatchKeys.Comparer),
    SkillValues = npc.SkillValues.ToArray()
  };

  public static ConflictDetectionResult DetectConflicts(
    IReadOnlyList<DistributionEntryViewModel> entries,
    IReadOnlyList<DistributionFileViewModel> existingFiles,
    string newFileName,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<NpcFilterData>? allNpcs = null,
    IReadOnlyDictionary<FormKey, HashSet<string>>? virtualKeywordsByNpc = null)
  {
    var allIssues = CollectNpcIssues(entries, existingFiles, linkCache, allNpcs, virtualKeywordsByNpc);

    var conflicts = allIssues.Where(i => i.IsHardConflict).ToList();
    var overlaps = allIssues.Where(i => i.IsProbabilisticOverlap).ToList();

    var conflictingFileNames = conflicts.Select(c => c.ExistingFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var overlappingFileNames = overlaps.Select(o => o.ExistingFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    var loadsAfterConflicts = DoesFileLoadAfterAll(newFileName, conflictingFileNames);
    var loadsAfterOverlaps = DoesFileLoadAfterAll(newFileName, overlappingFileNames);

    var hasConflicts = conflicts.Count > 0 && !loadsAfterConflicts;
    var conflictsResolvedByFilename = conflicts.Count > 0 && loadsAfterConflicts;
    var hasOverlaps = overlaps.Count > 0 && !loadsAfterOverlaps;
    var overlapsResolvedByFilename = overlaps.Count > 0 && loadsAfterOverlaps;

    var conflictCount = conflicts.Select(c => c.NpcFormKey).Distinct().Count();
    var overlapCount = overlaps.Select(c => c.NpcFormKey).Distinct().Count();
    var conflictSummary = BuildIssueSummary(
      conflicts, loadsAfterConflicts,
      $"✓ {conflictCount} NPC(s) have 100% chance distributions, but your filename '{newFileName}' will load after them.",
      $"⚠ {conflictCount} NPC(s) already have 100% chance outfit distributions:",
      c => $"  • {c.DisplayName ?? c.NpcFormKey.ToString()} ({c.ExistingFileName})");
    var overlapSummary = BuildIssueSummary(
      overlaps, loadsAfterOverlaps,
      $"ℹ {overlapCount} NPC(s) have chance-based distributions. Your file will load after them.",
      $"ℹ {overlapCount} NPC(s) have chance-based distributions (can coexist probabilistically):",
      o => $"  • {o.DisplayName ?? o.NpcFormKey.ToString()} ({o.ExistingFileName}, {o.ExistingChance}% chance)");

    if (allNpcs is not { Count: > 0 })
    {
      conflictSummary = (conflictSummary + Environment.NewLine +
        "NPC matching data unavailable; only explicit NPC selections were checked. Filtered conflicts may be missing.").Trim();
    }

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
    IReadOnlyList<DistributionFileViewModel> files,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<NpcFilterData>? allNpcs,
    IReadOnlyDictionary<FormKey, HashSet<string>>? virtualKeywordsByNpc)
  {
    var issues = new List<NpcConflictInfo>();

    // Each proposed population is invariant across existing rules.
    var candidates = entries.Where(e => e.IsOutfitDistribution).Select(entry => (
      Chance: entry.UseChance ? entry.Chance : 100,
      Npcs: (allNpcs is { Count: > 0 }
        ? FilterMatchingService.GetMatchingNpcsForEntry(allNpcs, entry.Entry, virtualKeywordsByNpc)
            .Where(n => n.IsConfirmed)
            .Select(n => (n.FormKey, n.DisplayName))
        : entry.SelectedNpcs.Where(n => !n.IsExcluded).Select(n => (n.FormKey, n.DisplayName))).ToList())).ToList();
    foreach (var file in files)
    {
      foreach (var line in file.Lines.Where(l => l.IsOutfitDistribution))
      {
        var existingChance = DistributionLineParser.ExtractChanceFromLine(file, line);
        var matchingKeys = allNpcs is { Count: > 0 }
          ? FilterMatchingService.GetMatchingNpcsForDistributionLine(
              allNpcs, file.Type, line.RawText, linkCache, virtualKeywordsByNpc).Select(n => n.FormKey).ToHashSet()
          : DistributionLineParser.ExtractNpcFormKeysFromLine(file, line, linkCache).ToHashSet();
        var fallbackAll = allNpcs is not { Count: > 0 } && DistributionLineParser.LineTargetsAllNpcs(file, line);

        foreach (var candidate in candidates)
        {
          foreach (var npc in candidate.Npcs.Where(n => fallbackAll || matchingKeys.Contains(n.FormKey)))
          {
            issues.Add(new NpcConflictInfo(npc.FormKey, npc.DisplayName, file.FileName,
              existingChance, candidate.Chance));
          }
        }
      }
    }

    return issues.Distinct().ToList();
  }

  private static string BuildIssueSummary(
    List<NpcConflictInfo> issues,
    bool loadsAfter,
    string resolvedMessage,
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

    var sb = new StringBuilder();
    sb.AppendLine(individualHeader);

    foreach (var issue in issues.Take(5))
    {
      sb.AppendLine(formatItem(issue));
    }

    if (issues.Count > 5)
    {
      sb.Append(CultureInfo.InvariantCulture, $"  ... and {issues.Count - 5} more matching distributions").AppendLine();
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

    var zPrefix = new string('Z', maxZCount + 1);
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
          npcVm.HasConflict = !result.ConflictsResolvedByFilename;
          npcVm.ConflictingFileName = conflict.ExistingFileName;
        }
        else
        {
          npcVm.HasConflict = false;
          npcVm.ConflictingFileName = null;
        }

        if (overlapNpcFormKeys.Contains(npcVm.FormKey))
        {
          var overlap = result.Overlaps.First(o => o.NpcFormKey == npcVm.FormKey);
          npcVm.HasOverlap = !result.OverlapsResolvedByFilename;
          npcVm.OverlappingFileName = overlap.ExistingFileName;
        }
        else
        {
          npcVm.HasOverlap = false;
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
        npc.HasConflict = false;
        npc.ConflictingFileName = null;
        npc.HasOverlap = false;
        npc.OverlappingFileName = null;
      }
    }
  }
}
