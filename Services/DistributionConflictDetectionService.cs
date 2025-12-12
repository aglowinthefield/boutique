using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public class DistributionConflictDetectionService : IDistributionConflictDetectionService
{
    public ConflictDetectionResult DetectConflicts(
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
                HasConflicts: false,
                ConflictsResolvedByFilename: false,
                ConflictSummary: string.Empty,
                SuggestedFileName: newFileName,
                Conflicts: Array.Empty<NpcConflictInfo>());
        }

        // Build a map of NPC FormKey -> (FileName, OutfitEditorId) from existing distribution files
        var existingDistributions = BuildExistingDistributionMap(existingFiles, linkCache);

        // Find conflicts
        var conflicts = new List<NpcConflictInfo>();
        var conflictingFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var newOutfitName = entry.SelectedOutfit?.EditorID ?? entry.SelectedOutfit?.FormKey.ToString();
            
            foreach (var npcVm in entry.SelectedNpcs)
            {
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
            }
        }

        // Check if the current filename already loads after all conflicting files
        var currentFileLoadsLast = DoesFileLoadAfterAll(newFileName, conflictingFileNames);

        // Only show as conflict if the user's file wouldn't load last
        var hasConflicts = conflicts.Count > 0 && !currentFileLoadsLast;
        var conflictsResolvedByFilename = conflicts.Count > 0 && currentFileLoadsLast;

        string conflictSummary;
        string suggestedFileName;

        if (conflicts.Count > 0)
        {
            if (currentFileLoadsLast)
            {
                // Conflict exists but is resolved by filename ordering
                conflictSummary = $"✓ {conflicts.Count} NPC(s) have existing distributions, but your filename '{newFileName}' will load after them.";
                suggestedFileName = newFileName;
            }
            else
            {
                // Build conflict summary
                var sb = new StringBuilder();
                sb.AppendLine($"⚠ {conflicts.Count} NPC(s) already have outfit distributions in existing files:");
                
                foreach (var conflict in conflicts.Take(5)) // Show first 5
                {
                    sb.AppendLine($"  • {conflict.DisplayName ?? conflict.NpcFormKey.ToString()} ({conflict.ExistingFileName})");
                }
                
                if (conflicts.Count > 5)
                {
                    sb.AppendLine($"  ... and {conflicts.Count - 5} more");
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
            HasConflicts: hasConflicts,
            ConflictsResolvedByFilename: conflictsResolvedByFilename,
            ConflictSummary: conflictSummary,
            SuggestedFileName: suggestedFileName,
            Conflicts: conflicts);
    }

    /// <summary>
    /// Checks if the given filename would alphabetically load after all the conflicting filenames.
    /// </summary>
    private static bool DoesFileLoadAfterAll(string fileName, HashSet<string> conflictingFileNames)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        // No conflicting files means we're already "after" all of them (vacuously true)
        if (conflictingFileNames.Count == 0)
            return true;

        foreach (var conflictingFile in conflictingFileNames)
        {
            // Compare alphabetically (case-insensitive, like file systems)
            if (string.Compare(fileName, conflictingFile, StringComparison.OrdinalIgnoreCase) <= 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds a map of NPC FormKey to existing distribution info from loaded distribution files.
    /// </summary>
    private Dictionary<FormKey, (string FileName, string? OutfitName)> BuildExistingDistributionMap(
        IReadOnlyList<DistributionFileViewModel> files,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var map = new Dictionary<FormKey, (string FileName, string? OutfitName)>();

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
                // Parse the line to extract NPC FormKeys (reuse cached dictionaries for SPID files)
                var npcFormKeys = DistributionLineParser.ExtractNpcFormKeysFromLine(file, line, linkCache, npcByEditorId, npcByName);
                var outfitName = DistributionLineParser.ExtractOutfitNameFromLine(line, linkCache);

                foreach (var npcFormKey in npcFormKeys)
                {
                    // Only track first occurrence (earlier files in load order)
                    if (!map.ContainsKey(npcFormKey))
                    {
                        map[npcFormKey] = (file.FileName, outfitName);
                    }
                }
            }
        }

        return map;
    }


    /// <summary>
    /// Calculates a Z-prefixed filename that will load after all conflicting files.
    /// </summary>
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

