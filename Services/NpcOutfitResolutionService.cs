using System.Collections.Concurrent;
using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class NpcOutfitResolutionService(
  MutagenService mutagenService,
  KeywordDistributionResolver keywordResolver,
  ILogger logger)
{
  private readonly ILogger _logger = logger.ForContext<NpcOutfitResolutionService>();

  public async Task<IReadOnlyList<NpcOutfitAssignment>> ResolveNpcOutfitsWithFiltersAsync(
    IReadOnlyList<DistributionFile> distributionFiles,
    IReadOnlyList<NpcFilterData> npcFilterData,
    CancellationToken cancellationToken = default)
  {
    _logger.Debug(
      "ResolveNpcOutfitsWithFiltersAsync called with {FileCount} distribution files and {NpcCount} NPCs",
      distributionFiles.Count,
      npcFilterData.Count);

    return await Task.Run<IReadOnlyList<NpcOutfitAssignment>>(
             () =>
             {
               if (mutagenService.LinkCache is not { } linkCache)
               {
                 _logger.Warning("LinkCache not available for NPC outfit resolution.");
                 return [];
               }

               try
               {
                 var sortedFiles = SortDistributionFiles(distributionFiles);
                 _logger.Information("Processing {Count} distribution files in order", sortedFiles.Count);

                 var npcDistributions = new Dictionary<FormKey, List<OutfitDistribution>>();

                 var outfitByEditorId = FormKeyHelper.BuildOutfitEditorIdLookup(linkCache);
                 _logger.Debug("Built Outfit EditorID lookup with {Count} entries", outfitByEditorId.Count);

                 // Pre-build lookups for SPID identifier classification
                 var keywordEditorIds = linkCache.WinningOverrides<IKeywordGetter>()
                                                 .Select(k => k.EditorID)
                                                 .Where(id => !string.IsNullOrWhiteSpace(id))
                                                 .Cast<string>()
                                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);
                 var factionEditorIds = linkCache.WinningOverrides<IFactionGetter>()
                                                 .Select(f => f.EditorID)
                                                 .Where(id => !string.IsNullOrWhiteSpace(id))
                                                 .Cast<string>()
                                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);
                 var raceEditorIds = linkCache.WinningOverrides<IRaceGetter>()
                                              .Select(r => r.EditorID)
                                              .Where(id => !string.IsNullOrWhiteSpace(id))
                                              .Cast<string>()
                                              .ToHashSet(StringComparer.OrdinalIgnoreCase);

                 var keywordEntries = keywordResolver.ParseKeywordDistributions(sortedFiles);
                 var (sortedKeywords, cyclicKeywords) = keywordResolver.TopologicalSort(keywordEntries);

                 if (cyclicKeywords.Count > 0)
                 {
                   _logger.Warning(
                     "Skipping {Count} keywords with circular dependencies: {Keywords}",
                     cyclicKeywords.Count,
                     string.Join(", ", cyclicKeywords.Take(5)));
                 }

                 var simulatedKeywords = keywordResolver.SimulateKeywordDistribution(sortedKeywords, npcFilterData);
                 _logger.Debug(
                   "Keyword simulation: {KeywordCount} keyword types, {NpcCount} NPCs with assignments",
                   sortedKeywords.Count,
                   simulatedKeywords.Count(kvp => kvp.Value.Count > 0));

                 _logger.Debug("Scanning NPCs for ESP-provided default outfits...");
                 ProcessEspProvidedOutfitsFromFilterData(linkCache, npcFilterData, npcDistributions);
                 _logger.Debug(
                   "After processing ESP outfits: {NpcCount} unique NPCs with distributions",
                   npcDistributions.Count);

                 for (var fileIndex = 0; fileIndex < sortedFiles.Count; fileIndex++)
                 {
                   cancellationToken.ThrowIfCancellationRequested();
                   var file = sortedFiles[fileIndex];

                   _logger.Debug(
                     "Processing file {Index}/{Total}: {FileName}",
                     fileIndex + 1,
                     sortedFiles.Count,
                     file.FileName);

                   ProcessDistributionFileWithFilters(
                     file,
                     fileIndex + 1,
                     linkCache,
                     npcFilterData,
                     outfitByEditorId,
                     npcDistributions,
                     simulatedKeywords,
                     keywordEditorIds,
                     factionEditorIds,
                     raceEditorIds);

                   _logger.Debug(
                     "After processing {FileName}: {NpcCount} unique NPCs with distributions",
                     file.FileName,
                     npcDistributions.Count);
                 }

                 _logger.Debug("Total unique NPCs with distributions: {Count}", npcDistributions.Count);

                 var assignments = BuildNpcOutfitAssignmentsFromFilterData(npcDistributions, npcFilterData);

                 _logger.Information(
                   "Resolved outfit assignments for {Count} NPCs using full filter matching",
                   assignments.Count);
                 return assignments;
               }
               catch (OperationCanceledException)
               {
                 _logger.Information("NPC outfit resolution cancelled.");
                 return [];
               }
               catch (Exception ex)
               {
                 _logger.Error(ex, "Failed to resolve NPC outfit assignments.");
                 return [];
               }
             },
             cancellationToken);
  }

  private void ProcessDistributionFileWithFilters(
    DistributionFile file,
    int processingOrder,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<NpcFilterData> allNpcs,
    IReadOnlyDictionary<string, FormKey> outfitByEditorId,
    Dictionary<FormKey, List<OutfitDistribution>> npcDistributions,
    Dictionary<FormKey, HashSet<string>> simulatedKeywords,
    HashSet<string> keywordEditorIds,
    HashSet<string> factionEditorIds,
    HashSet<string> raceEditorIds)
  {
    var spidLines =
      new List<(DistributionLine Line, SpidDistributionFilter Filter, FormKey OutfitFormKey, string? OutfitEditorId,
        bool HasRaceTargeting, bool UsesKeywordTargeting, bool UsesFactionTargeting, string TargetingDescription, bool
        HasTraitFilters)>();
    var skyPatcherLines = new List<DistributionLine>();

    foreach (var line in file.Lines)
    {
      if (!line.IsOutfitDistribution)
      {
        continue;
      }

      if (file.Type == DistributionFileType.Spid)
      {
        if (SpidLineParser.TryParse(line.RawText, out var filter) && filter != null)
        {
          var outfitSource = ResolveOutfitFromIdentifier(filter.OutfitIdentifier, linkCache);
          if (outfitSource.HasValue)
          {
            var hasRaceTargeting = filter.FormFilters.Expressions
                                         .SelectMany(e => e.Parts.Where(p => !p.IsNegated))
                                         .Any(p => raceEditorIds.Contains(p.Value));

            var usesKeywordTargeting = filter.StringFilters.Expressions
                                             .SelectMany(e => e.Parts.Where(p => !p.HasWildcard && !p.IsNegated))
                                             .Any(p => keywordEditorIds.Contains(p.Value));

            var usesFactionTargeting = filter.FormFilters.Expressions
                                             .SelectMany(e => e.Parts.Where(p => !p.IsNegated))
                                             .Any(p => factionEditorIds.Contains(p.Value));

            var targetingDescription = filter.GetTargetingDescription();
            var hasTraitFilters      = !filter.TraitFilters.IsEmpty;

            spidLines.Add(
              (line, filter, outfitSource.Value.FormKey, outfitSource.Value.EditorId, hasRaceTargeting,
               usesKeywordTargeting, usesFactionTargeting, targetingDescription, hasTraitFilters));
          }
        }
      }
      else if (file.Type == DistributionFileType.SkyPatcher)
      {
        skyPatcherLines.Add(line);
      }
    }

    if (spidLines.Count == 0 && skyPatcherLines.Count == 0)
    {
      return;
    }

    var localDistributions = new ConcurrentDictionary<FormKey, ConcurrentBag<OutfitDistribution>>();

    if (spidLines.Count > 0)
    {
      Parallel.ForEach(
        allNpcs,
        npc =>
        {
          simulatedKeywords.TryGetValue(npc.FormKey, out var virtualKeywords);

          foreach (var (line, filter, outfitFormKey, outfitEditorId, hasRaceTargeting, usesKeywordTargeting,
                     usesFactionTargeting, targetingDescription, hasTraitFilters) in spidLines)
          {
            if (SpidFilterMatchingService.NpcMatchesFilterForBatch(npc, filter, virtualKeywords))
            {
              var bag = localDistributions.GetOrAdd(npc.FormKey, _ => new ConcurrentBag<OutfitDistribution>());

              bag.Add(
                new OutfitDistribution(
                  file.FullPath,
                  file.FileName,
                  file.Type,
                  outfitFormKey,
                  outfitEditorId,
                  processingOrder,
                  false,
                  line.RawText,
                  targetingDescription,
                  filter.Chance,
                  filter.TargetsAllNpcs,
                  usesKeywordTargeting,
                  usesFactionTargeting,
                  hasRaceTargeting,
                  hasTraitFilters));
            }
          }
        });
    }

    if (skyPatcherLines.Count > 0)
    {
      foreach (var line in skyPatcherLines)
      {
        var results = new List<(FormKey NpcFormKey, FormKey OutfitFormKey, string? OutfitEditorId)>();
        ParseSkyPatcherLineCore(line.RawText, linkCache, outfitByEditorId, results);

        foreach (var (npcFormKey, outfitFormKey, outfitEditorId) in results)
        {
          var bag = localDistributions.GetOrAdd(npcFormKey, _ => new ConcurrentBag<OutfitDistribution>());
          bag.Add(
            new OutfitDistribution(
              file.FullPath,
              file.FileName,
              file.Type,
              outfitFormKey,
              outfitEditorId,
              processingOrder,
              false,
              line.RawText,
              "Specific NPC targeting"));
        }
      }
    }

    // Merge local distributions back into the main dictionary
    foreach (var kvp in localDistributions)
    {
      if (!npcDistributions.TryGetValue(kvp.Key, out var list))
      {
        list                      = [];
        npcDistributions[kvp.Key] = list;
      }

      list.AddRange(kvp.Value);
    }

    _logger.Debug(
      "File {FileName} summary: {SpidLines} SPID lines, {SkyPatcherLines} SkyPatcher lines, {MatchedNpcs} unique NPCs matched",
      file.FileName,
      spidLines.Count,
      skyPatcherLines.Count,
      localDistributions.Count);
  }

  private static void ParseSkyPatcherLineCore(
    string lineText,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyDictionary<string, FormKey> outfitByEditorId,
    List<(FormKey npcFormKey, FormKey outfitFormKey, string? outfitEditorId)> results)
  {
    var npcFormKeys = ParseNpcFormKeysWithEditorIdFallback(lineText, linkCache);
    var (outfitFormKey, outfitEditorId) = ResolveOutfitFromLine(lineText, linkCache, outfitByEditorId);

    if (!outfitFormKey.HasValue || npcFormKeys.Count == 0)
    {
      return;
    }

    var genderFilter = SkyPatcherSyntax.ParseGenderFilter(lineText);

    foreach (var npcFormKey in npcFormKeys)
    {
      if (genderFilter.HasValue && linkCache.TryResolve<INpcGetter>(npcFormKey, out var npc))
      {
        var isFemale = npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female);
        if (isFemale != genderFilter.Value)
        {
          continue;
        }
      }

      results.Add((npcFormKey, outfitFormKey.Value, outfitEditorId));
    }
  }

  private static List<FormKey> ParseNpcFormKeysWithEditorIdFallback(
    string lineText,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var npcFormKeys    = new List<FormKey>();
    var npcIdentifiers = SkyPatcherSyntax.ExtractFilterValues(lineText, "filterByNpcs");

    foreach (var npcIdentifier in npcIdentifiers)
    {
      if (FormKeyHelper.TryParse(npcIdentifier, out var formKey))
      {
        npcFormKeys.Add(formKey);
      }
      else if (linkCache.TryResolve<INpcGetter>(npcIdentifier, out var npc))
      {
        npcFormKeys.Add(npc.FormKey);
      }
    }

    return npcFormKeys;
  }

  private static (FormKey? OutfitFormKey, string? EditorId) ResolveOutfitFromLine(
    string lineText,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyDictionary<string, FormKey> outfitByEditorId)
  {
    var outfitString = SkyPatcherSyntax.ExtractFilterValue(lineText, "outfitDefault")
                       ?? SkyPatcherSyntax.ExtractFilterValue(lineText, "filterByOutfits");

    if (string.IsNullOrWhiteSpace(outfitString))
    {
      return (null, null);
    }

    var resolvedFormKey = FormKeyHelper.ResolveOutfit(outfitString, outfitByEditorId);
    if (!resolvedFormKey.HasValue)
    {
      return (null, null);
    }

    var editorId = linkCache.TryResolve<IOutfitGetter>(resolvedFormKey.Value, out var outfit)
                     ? outfit.EditorID
                     : null;

    return (resolvedFormKey, editorId);
  }

  private static (FormKey FormKey, string? EditorId)? ResolveOutfitFromIdentifier(
    string identifier,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var outfitFormKey = FormKeyHelper.ResolveOutfit(identifier, linkCache);
    if (outfitFormKey.HasValue && !outfitFormKey.Value.IsNull)
    {
      if (linkCache.TryResolve<IOutfitGetter>(outfitFormKey.Value, out var outfit))
      {
        return (outfit.FormKey, outfit.EditorID);
      }
    }

    return null;
  }

  private static List<NpcOutfitAssignment> BuildNpcOutfitAssignmentsFromFilterData(
    Dictionary<FormKey, List<OutfitDistribution>> npcDistributions,
    IReadOnlyList<NpcFilterData> allNpcs)
  {
    var npcLookup = allNpcs.ToDictionary(
      n => n.FormKey,
      n => new NpcBasicInfo(n.FormKey, n.EditorId, n.Name, n.SourceMod));
    return BuildNpcOutfitAssignmentsCore(npcDistributions, npcLookup);
  }

  private static List<NpcOutfitAssignment> BuildNpcOutfitAssignmentsCore(
    Dictionary<FormKey, List<OutfitDistribution>> npcDistributions,
    IReadOnlyDictionary<FormKey, NpcBasicInfo> npcLookup)
  {
    var assignments = new List<NpcOutfitAssignment>();

    foreach (var (npcFormKey, distributions) in npcDistributions)
    {
      var sortedDistributions = distributions
                                .OrderBy(d => d.ProcessingOrder)
                                .ToList();

      var iniDistributions = sortedDistributions
                             .Where(d => d.FileType != DistributionFileType.Esp)
                             .ToList();

      var hasIniDistributions = iniDistributions.Count > 0;
      var distributionsToUse  = hasIniDistributions ? iniDistributions : sortedDistributions;

      var winnerIndex = distributionsToUse.Count - 1;
      var updatedDistributions = sortedDistributions
                                 .Select((d, i) =>
                                 {
                                   var isWinner = hasIniDistributions
                                                    ? d.FileType != DistributionFileType.Esp &&
                                                      i == sortedDistributions.IndexOf(distributionsToUse[winnerIndex])
                                                    : i == sortedDistributions.Count - 1;
                                   return d with { IsWinner = isWinner };
                                 })
                                 .ToList();

      var winner = distributionsToUse[winnerIndex];

      string? editorId  = null;
      string? name      = null;
      var     sourceMod = npcFormKey.ModKey;

      if (npcLookup.TryGetValue(npcFormKey, out var npcData))
      {
        editorId  = npcData.EditorId;
        name      = npcData.Name;
        sourceMod = npcData.SourceMod;
      }

      var iniOnlyDistributions = updatedDistributions
                                 .Where(d => d.FileType != DistributionFileType.Esp)
                                 .ToList();
      var hasConflict = iniOnlyDistributions.Count > 1;

      assignments.Add(
        new NpcOutfitAssignment(
          npcFormKey,
          editorId,
          name,
          sourceMod,
          winner.OutfitFormKey,
          winner.OutfitEditorId,
          updatedDistributions,
          hasConflict));
    }

    return assignments
           .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
           .ToList();
  }

  private static List<DistributionFile> SortDistributionFiles(IReadOnlyList<DistributionFile> files)
  {
    var spidFiles = files
                    .Where(f => f.Type == DistributionFileType.Spid)
                    .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

    var skyPatcherFiles = files
                          .Where(f => f.Type == DistributionFileType.SkyPatcher)
                          .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                          .ToList();

    var sorted = new List<DistributionFile>();
    sorted.AddRange(spidFiles);
    sorted.AddRange(skyPatcherFiles);

    return sorted;
  }

  private void ProcessEspProvidedOutfitsFromFilterData(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<NpcFilterData> allNpcs,
    Dictionary<FormKey, List<OutfitDistribution>> npcDistributions)
  {
    var espOutfitCount = 0;

    foreach (var npcData in allNpcs)
    {
      var defaultOutfitFormKey = npcData.DefaultOutfitFormKey;
      if (!defaultOutfitFormKey.HasValue || defaultOutfitFormKey.Value.IsNull)
      {
        continue;
      }

      if (!linkCache.TryResolve<IOutfitGetter>(defaultOutfitFormKey.Value, out var outfit))
      {
        continue;
      }

      var npcFormKey = npcData.FormKey;
      if (!npcDistributions.TryGetValue(npcFormKey, out var distributions))
      {
        distributions                = [];
        npcDistributions[npcFormKey] = distributions;
      }

      var sourcePlugin = npcData.SourceMod.FileName;

      distributions.Add(
        new OutfitDistribution(
          $"{sourcePlugin} (ESP)",
          sourcePlugin,
          DistributionFileType.Esp,
          outfit.FormKey,
          outfit.EditorID ?? npcData.DefaultOutfitEditorId,
          0,     // ESP has lowest priority
          false, // Will be determined later
          null,
          "Default outfit from ESP"));

      espOutfitCount++;
    }

    _logger.Debug("Found {Count} NPCs with ESP-provided default outfits", espOutfitCount);
  }

  private record NpcBasicInfo(FormKey FormKey, string? EditorId, string? Name, ModKey SourceMod);
}
