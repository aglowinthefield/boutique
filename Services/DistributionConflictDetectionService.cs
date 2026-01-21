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
                newFileName,
                []);
        }

        // Build a map of NPC FormKey -> (FileName, OutfitEditorId) from existing distribution files
        // Also track files that target ALL NPCs
        var (existingDistributions, allNpcsDistributions) = BuildExistingDistributionMap(existingFiles, linkCache);

        // Find conflicts
        var conflicts = new List<NpcConflictInfo>();
        var conflictingFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var newOutfitName = entry.SelectedOutfit?.EditorID ?? entry.SelectedOutfit?.FormKey.ToString();

            foreach (var npcVm in entry.SelectedNpcs)
            {
                // First check for specific NPC conflicts
                if (existingDistributions.TryGetValue(npcVm.FormKey, out var existing))
                {
                    conflicts.Add(new NpcConflictInfo(
                        npcVm.FormKey,
                        npcVm.DisplayName,
                        existing.FileName,
                        existing.OutfitName,
                        newOutfitName));

                    conflictingFileNames.Add(existing.FileName);
                }
                else if (allNpcsDistributions.Count > 0)
                {
                    // Use the first "all NPCs" distribution as the conflict
                    var allNpcsDist = allNpcsDistributions[0];
                    conflicts.Add(new NpcConflictInfo(
                        npcVm.FormKey,
                        npcVm.DisplayName,
                        allNpcsDist.FileName,
                        allNpcsDist.OutfitName,
                        newOutfitName));

                    conflictingFileNames.Add(allNpcsDist.FileName);
                }
            }
        }

        // Check if the current filename already loads after all conflicting files
        var currentFileLoadsLast = DoesFileLoadAfterAll(newFileName, conflictingFileNames);

        // Only show as conflict if the user's file wouldn't load last
        var hasConflicts = conflicts.Count > 0 && !currentFileLoadsLast;
        var conflictsResolvedByFilename = conflicts.Count > 0 && currentFileLoadsLast;

        string conflictSummary;
        string suggestedFileName;

        // Check if conflicts are from an "all NPCs" distribution
        var hasAllNpcsConflict = allNpcsDistributions.Count > 0 &&
                                 conflicts.Any(c => allNpcsDistributions.Exists(d => d.FileName == c.ExistingFileName));

        if (conflicts.Count > 0)
        {
            if (currentFileLoadsLast)
            {
                // Conflict exists but is resolved by filename ordering
                var allNpcsNote = hasAllNpcsConflict
                    ? $" ('{allNpcsDistributions[0].FileName}' targets all NPCs)"
                    : string.Empty;
                conflictSummary =
                    $"✓ {conflicts.Count} NPC(s) have existing distributions{allNpcsNote}, but your filename '{newFileName}' will load after them.";
                suggestedFileName = newFileName;
            }
            else
            {
                // Build conflict summary
                var sb = new StringBuilder();

                if (hasAllNpcsConflict)
                {
                    sb.Append(CultureInfo.InvariantCulture,
                            $"⚠ '{allNpcsDistributions[0].FileName}' distributes outfit '{allNpcsDistributions[0].OutfitName}' to ALL NPCs.")
                        .AppendLine();
                    sb.Append(CultureInfo.InvariantCulture,
                        $"All {conflicts.Count} NPC(s) in your entries will be affected.").AppendLine();
                }
                else
                {
                    sb.Append(CultureInfo.InvariantCulture,
                            $"⚠ {conflicts.Count} NPC(s) already have outfit distributions in existing files:")
                        .AppendLine();

                    foreach (var conflict in conflicts.Take(5)) // Show first 5
                        sb.Append(CultureInfo.InvariantCulture,
                                $"  • {conflict.DisplayName ?? conflict.NpcFormKey.ToString()} ({conflict.ExistingFileName})")
                            .AppendLine();

                    if (conflicts.Count > 5)
                        sb.Append(CultureInfo.InvariantCulture, $"  ... and {conflicts.Count - 5} more").AppendLine();
                }

                conflictSummary = sb.ToString().TrimEnd();

                // Calculate suggested filename with Z-prefix
                suggestedFileName = CalculateZPrefixedFileName(newFileName, conflictingFileNames);
            }
        }
        else
        {
            conflictSummary = string.Empty;
            suggestedFileName = newFileName;
        }

        return new ConflictDetectionResult(
            hasConflicts,
            conflictsResolvedByFilename,
            conflictSummary,
            suggestedFileName,
            conflicts);
    }

    private static bool DoesFileLoadAfterAll(string fileName, HashSet<string> conflictingFileNames)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        // No conflicting files means we're already "after" all of them (vacuously true)
        if (conflictingFileNames.Count == 0)
            return true;

        foreach (var conflictingFile in conflictingFileNames)
            // Compare alphabetically (case-insensitive, like file systems)
            if (string.Compare(fileName, conflictingFile, StringComparison.OrdinalIgnoreCase) <= 0)
                return false;

        return true;
    }

    private static (Dictionary<FormKey, (string FileName, string? OutfitName)> SpecificNpcDistributions,
        List<(string FileName, string? OutfitName)> AllNpcsDistributions)
        BuildExistingDistributionMap(
            IReadOnlyList<DistributionFileViewModel> files,
            ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var map = new Dictionary<FormKey, (string FileName, string? OutfitName)>();
        var allNpcsDistributions = new List<(string FileName, string? OutfitName)>();

        // Check if we need NPC lookup dictionaries (for SPID files)
        var hasSpidFiles = files.Any(f => f.TypeDisplay == "SPID");
        Dictionary<string, INpcGetter>? npcByEditorId = null;
        Dictionary<string, INpcGetter>? npcByName = null;

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

                // Check if this line targets all NPCs (no filters)
                if (DistributionLineParser.LineTargetsAllNpcs(file, line))
                {
                    // Only track the first "all NPCs" distribution
                    if (allNpcsDistributions.Count == 0) allNpcsDistributions.Add((file.FileName, outfitName));

                    continue;
                }

                // Parse the line to extract specific NPC FormKeys
                var npcFormKeys =
                    DistributionLineParser.ExtractNpcFormKeysFromLine(file, line, linkCache, npcByEditorId, npcByName);

                foreach (var npcFormKey in npcFormKeys)
                    // Only track first occurrence (earlier files in load order)
                    if (!map.ContainsKey(npcFormKey))
                        map[npcFormKey] = (file.FileName, outfitName);
            }
        }

        return (map, allNpcsDistributions);
    }

    private static string CalculateZPrefixedFileName(string newFileName, HashSet<string> conflictingFileNames)
    {
        if (string.IsNullOrWhiteSpace(newFileName) || conflictingFileNames.Count == 0)
            return newFileName;

        // Find the maximum number of leading Z's in conflicting filenames
        var maxZCount = 0;
        foreach (var fileName in conflictingFileNames)
        {
            var zCount = 0;
            foreach (var c in fileName)
            {
                if (c == 'Z' || c == 'z')
                    zCount++;
                else
                    break;
            }

            maxZCount = Math.Max(maxZCount, zCount);
        }

        // Add one more Z than the maximum
        var zPrefix = new string('Z', maxZCount + 1);

        // Remove any existing Z prefix from the new filename
        var baseName = newFileName.TrimStart('Z', 'z');

        return zPrefix + baseName;
    }
}
