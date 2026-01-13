using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class NpcOutfitResolutionService
{
    private readonly MutagenService _mutagenService;
    private readonly KeywordDistributionResolver _keywordResolver;
    private readonly ILogger _logger;

    public NpcOutfitResolutionService(
        MutagenService mutagenService,
        KeywordDistributionResolver keywordResolver,
        ILogger logger)
    {
        _mutagenService = mutagenService;
        _keywordResolver = keywordResolver;
        _logger = logger.ForContext<NpcOutfitResolutionService>();
    }

    public async Task<IReadOnlyList<NpcOutfitAssignment>> ResolveNpcOutfitsAsync(
        IReadOnlyList<DistributionFile> distributionFiles,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug("ResolveNpcOutfitsAsync called with {Count} distribution files", distributionFiles.Count);

        return await Task.Run<IReadOnlyList<NpcOutfitAssignment>>(() =>
        {
            if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
            {
                _logger.Warning("LinkCache not available for NPC outfit resolution.");
                return [];
            }

            _logger.Debug("LinkCache is available");

            try
            {
                // Sort files: SPID files alphabetically, then SkyPatcher files alphabetically
                var sortedFiles = SortDistributionFiles(distributionFiles);
                _logger.Information("Processing {Count} distribution files in order", sortedFiles.Count);

                // Log each file for debugging
                foreach (var file in sortedFiles)
                {
                    var outfitLineCount = file.Lines.Count(l => l.IsOutfitDistribution);
                    _logger.Debug("File: {FileName} ({Type}) - {TotalLines} lines, {OutfitLines} outfit distributions",
                        file.FileName, file.Type, file.Lines.Count, outfitLineCount);
                }

                // Build a dictionary of NPC FormKey -> list of distributions
                var npcDistributions = new Dictionary<FormKey, List<OutfitDistribution>>();

                // Cache all NPCs for NPC identifier resolution
                _logger.Debug("Loading all NPCs from LinkCache...");
                var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
                _logger.Debug("Loaded {Count} NPCs from LinkCache", allNpcs.Count);

                // Use GroupBy to handle duplicate EditorIDs (can happen with mod conflicts)
                var npcByEditorId = allNpcs
                    .Where(n => !string.IsNullOrWhiteSpace(n.EditorID))
                    .GroupBy(n => n.EditorID!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                _logger.Debug("Built NPC EditorID lookup with {Count} entries", npcByEditorId.Count);

                var npcByName = allNpcs
                    .Where(n => !string.IsNullOrWhiteSpace(n.Name?.String))
                    .GroupBy(n => n.Name!.String!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                _logger.Debug("Built NPC Name lookup with {Count} entries", npcByName.Count);

                // Build outfit lookup for fast EditorID resolution
                var outfitByEditorId = FormKeyHelper.BuildOutfitEditorIdLookup(linkCache);
                _logger.Debug("Built Outfit EditorID lookup with {Count} entries", outfitByEditorId.Count);

                // First, add ESP-provided outfits (processing order 0, so INI files win)
                _logger.Debug("Scanning NPCs for ESP-provided default outfits...");
                ProcessEspProvidedOutfits(linkCache, allNpcs, npcDistributions);
                _logger.Debug("After processing ESP outfits: {NpcCount} unique NPCs with distributions", npcDistributions.Count);

                // Process each file in order (starting at order 1, so they win over ESP)
                for (int fileIndex = 0; fileIndex < sortedFiles.Count; fileIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = sortedFiles[fileIndex];

                    _logger.Debug("Processing file {Index}/{Total}: {FileName}",
                        fileIndex + 1, sortedFiles.Count, file.FileName);

                    // Processing order starts at 1 (ESP is 0)
                    ProcessDistributionFile(file, fileIndex + 1, linkCache, npcByEditorId, npcByName, outfitByEditorId, npcDistributions);

                    _logger.Debug("After processing {FileName}: {NpcCount} unique NPCs with distributions",
                        file.FileName, npcDistributions.Count);
                }

                _logger.Debug("Total unique NPCs with distributions: {Count}", npcDistributions.Count);

                // Build the final NPC outfit assignments
                var assignments = BuildNpcOutfitAssignments(npcDistributions, linkCache, allNpcs);

                _logger.Information("Resolved outfit assignments for {Count} NPCs", assignments.Count);
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
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<NpcOutfitAssignment>> ResolveNpcOutfitsWithFiltersAsync(
        IReadOnlyList<DistributionFile> distributionFiles,
        IReadOnlyList<NpcFilterData> npcFilterData,
        CancellationToken cancellationToken = default)
    {
        _logger.Debug("ResolveNpcOutfitsWithFiltersAsync called with {FileCount} distribution files and {NpcCount} NPCs",
            distributionFiles.Count, npcFilterData.Count);

        return await Task.Run<IReadOnlyList<NpcOutfitAssignment>>(() =>
        {
            if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
            {
                _logger.Warning("LinkCache not available for NPC outfit resolution.");
                return [];
            }

            try
            {
                // Sort files: SPID files alphabetically, then SkyPatcher files alphabetically
                var sortedFiles = SortDistributionFiles(distributionFiles);
                _logger.Information("Processing {Count} distribution files in order", sortedFiles.Count);

                // Build a dictionary of NPC FormKey -> list of distributions
                var npcDistributions = new Dictionary<FormKey, List<OutfitDistribution>>();

                // Build outfit lookup for fast EditorID resolution
                var outfitByEditorId = FormKeyHelper.BuildOutfitEditorIdLookup(linkCache);
                _logger.Debug("Built Outfit EditorID lookup with {Count} entries", outfitByEditorId.Count);

                // Simulate SPID keyword distributions before processing outfit lines
                var keywordEntries = _keywordResolver.ParseKeywordDistributions(sortedFiles);
                var (sortedKeywords, cyclicKeywords) = _keywordResolver.TopologicalSort(keywordEntries);

                if (cyclicKeywords.Count > 0)
                {
                    _logger.Warning("Skipping {Count} keywords with circular dependencies: {Keywords}",
                        cyclicKeywords.Count, string.Join(", ", cyclicKeywords.Take(5)));
                }

                var simulatedKeywords = _keywordResolver.SimulateKeywordDistribution(sortedKeywords, npcFilterData);
                _logger.Debug("Keyword simulation: {KeywordCount} keyword types, {NpcCount} NPCs with assignments",
                    sortedKeywords.Count, simulatedKeywords.Count(kvp => kvp.Value.Count > 0));

                // First, add ESP-provided outfits (processing order 0, so INI files win)
                _logger.Debug("Scanning NPCs for ESP-provided default outfits...");
                ProcessEspProvidedOutfitsFromFilterData(linkCache, npcFilterData, npcDistributions);
                _logger.Debug("After processing ESP outfits: {NpcCount} unique NPCs with distributions", npcDistributions.Count);

                // Process each file in order (starting at order 1, so they win over ESP)
                for (int fileIndex = 0; fileIndex < sortedFiles.Count; fileIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = sortedFiles[fileIndex];

                    _logger.Debug("Processing file {Index}/{Total}: {FileName}",
                        fileIndex + 1, sortedFiles.Count, file.FileName);

                    // Processing order starts at 1 (ESP is 0)
                    ProcessDistributionFileWithFilters(file, fileIndex + 1, linkCache, npcFilterData, outfitByEditorId, npcDistributions, simulatedKeywords);

                    _logger.Debug("After processing {FileName}: {NpcCount} unique NPCs with distributions",
                        file.FileName, npcDistributions.Count);
                }

                _logger.Debug("Total unique NPCs with distributions: {Count}", npcDistributions.Count);

                // Build the final NPC outfit assignments
                var assignments = BuildNpcOutfitAssignmentsFromFilterData(npcDistributions, npcFilterData);

                _logger.Information("Resolved outfit assignments for {Count} NPCs using full filter matching", assignments.Count);
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
        }, cancellationToken);
    }

    private void ProcessDistributionFileWithFilters(
        DistributionFile file,
        int processingOrder,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyList<NpcFilterData> allNpcs,
        IReadOnlyDictionary<string, FormKey> outfitByEditorId,
        Dictionary<FormKey, List<OutfitDistribution>> npcDistributions,
        Dictionary<FormKey, HashSet<string>> simulatedKeywords)
    {
        var outfitLineCount = 0;
        var matchedNpcCount = 0;

        foreach (var line in file.Lines)
        {
            if (!line.IsOutfitDistribution)
                continue;

            outfitLineCount++;

            if (file.Type == DistributionFileType.Spid)
            {
                ProcessSpidLineWithFilters(file, line, processingOrder, linkCache, allNpcs, npcDistributions, simulatedKeywords, ref matchedNpcCount);
            }
            else if (file.Type == DistributionFileType.SkyPatcher)
            {
                ProcessSkyPatcherLineWithFilters(file, line, processingOrder, linkCache, outfitByEditorId, npcDistributions, ref matchedNpcCount);
            }
        }

        _logger.Debug("File {FileName} summary: {OutfitLines} outfit lines, {MatchedNpcs} total NPC matches",
            file.FileName, outfitLineCount, matchedNpcCount);
    }

    private void ProcessSpidLineWithFilters(
        DistributionFile file,
        DistributionLine line,
        int processingOrder,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyList<NpcFilterData> allNpcs,
        Dictionary<FormKey, List<OutfitDistribution>> npcDistributions,
        Dictionary<FormKey, HashSet<string>> simulatedKeywords,
        ref int matchedNpcCount)
    {
        // Parse the SPID line using the full parser
        if (!SpidLineParser.TryParse(line.RawText, out var filter) || filter == null)
        {
            _logger.Debug("Failed to parse SPID line: {Line}", line.RawText);
            return;
        }

        // Resolve the outfit FormKey
        var outfitFormKey = FormKeyHelper.ResolveOutfit(filter.OutfitIdentifier, linkCache);
        if (outfitFormKey == null || outfitFormKey.Value.IsNull)
        {
            _logger.Debug("Could not resolve outfit identifier: {Identifier}", filter.OutfitIdentifier);
            return;
        }

        string? outfitEditorId = null;
        if (linkCache.TryResolve<IOutfitGetter>(outfitFormKey.Value, out var outfit))
        {
            outfitEditorId = outfit.EditorID;
        }

        // Find all matching NPCs using the filter matching service with virtual keywords
        var matchingNpcs = SpidFilterMatchingService.GetMatchingNpcsWithVirtualKeywords(allNpcs, filter, simulatedKeywords);

        _logger.Debug("SPID line matched {Count} NPCs: {Line}", matchingNpcs.Count,
            line.RawText.Length > 80 ? line.RawText[..80] + "..." : line.RawText);

        // Determine targeting type
        var hasRaceTargeting = filter.FormFilters.Expressions.Any(e =>
            e.Parts.Any(p => p.LooksLikeRace));

        // Add distribution for each matching NPC
        foreach (var npc in matchingNpcs)
        {
            if (!npcDistributions.TryGetValue(npc.FormKey, out var distributions))
            {
                distributions = [];
                npcDistributions[npc.FormKey] = distributions;
            }

            distributions.Add(new OutfitDistribution(
                FilePath: file.FullPath,
                FileName: file.FileName,
                FileType: file.Type,
                OutfitFormKey: outfitFormKey.Value,
                OutfitEditorId: outfitEditorId,
                ProcessingOrder: processingOrder,
                IsWinner: false,
                RawLine: line.RawText,
                TargetingDescription: filter.GetTargetingDescription(),
                Chance: filter.Chance,
                TargetsAllNpcs: filter.TargetsAllNpcs,
                UsesKeywordTargeting: filter.UsesKeywordTargeting,
                UsesFactionTargeting: filter.UsesFactionTargeting,
                UsesRaceTargeting: hasRaceTargeting,
                UsesTraitTargeting: !filter.TraitFilters.IsEmpty
            ));

            matchedNpcCount++;
        }
    }

    private static void ProcessSkyPatcherLineWithFilters(
        DistributionFile file,
        DistributionLine line,
        int processingOrder,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyDictionary<string, FormKey> outfitByEditorId,
        Dictionary<FormKey, List<OutfitDistribution>> npcDistributions,
        ref int matchedNpcCount)
    {
        // SkyPatcher uses explicit NPC FormKeys, not filters
        var results = new List<(FormKey NpcFormKey, FormKey OutfitFormKey, string? OutfitEditorId)>();

        ParseSkyPatcherLineForFilteredResolution(line.RawText, linkCache, outfitByEditorId, results);

        foreach (var (npcFormKey, outfitFormKey, outfitEditorId) in results)
        {
            if (!npcDistributions.TryGetValue(npcFormKey, out var distributions))
            {
                distributions = [];
                npcDistributions[npcFormKey] = distributions;
            }

            distributions.Add(new OutfitDistribution(
                FilePath: file.FullPath,
                FileName: file.FileName,
                FileType: file.Type,
                OutfitFormKey: outfitFormKey,
                OutfitEditorId: outfitEditorId,
                ProcessingOrder: processingOrder,
                IsWinner: false,
                RawLine: line.RawText,
                TargetingDescription: "Specific NPC targeting"
            ));

            matchedNpcCount++;
        }
    }

    private static void ParseSkyPatcherLineForFilteredResolution(
        string lineText,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyDictionary<string, FormKey> outfitByEditorId,
        List<(FormKey, FormKey, string?)> results)
    {
        var trimmed = lineText.Trim();

        // Extract NPC FormKeys
        var npcFormKeys = new List<FormKey>();
        var filterByNpcsIndex = trimmed.IndexOf("filterByNpcs=", StringComparison.OrdinalIgnoreCase);

        if (filterByNpcsIndex >= 0)
        {
            var npcStart = filterByNpcsIndex + "filterByNpcs=".Length;
            var npcEnd = trimmed.IndexOf(':', npcStart);

            if (npcEnd > npcStart)
            {
                var npcString = trimmed.Substring(npcStart, npcEnd - npcStart);

                foreach (var npcPart in npcString.Split(','))
                {
                    if (FormKeyHelper.TryParse(npcPart.Trim(), out var formKey))
                    {
                        npcFormKeys.Add(formKey);
                    }
                }
            }
        }

        // Extract outfit - supports both FormKey format (ModKey|FormID) and EditorID
        FormKey? outfitFormKey = null;
        string? outfitEditorId = null;
        var outfitDefaultIndex = trimmed.IndexOf("outfitDefault=", StringComparison.OrdinalIgnoreCase);

        if (outfitDefaultIndex >= 0)
        {
            var outfitStart = outfitDefaultIndex + "outfitDefault=".Length;
            var outfitEnd = trimmed.IndexOf(':', outfitStart);
            var outfitString = (outfitEnd > outfitStart
                ? trimmed.Substring(outfitStart, outfitEnd - outfitStart)
                : trimmed.Substring(outfitStart)).Trim();

            var resolvedFormKey = FormKeyHelper.ResolveOutfit(outfitString, linkCache, outfitByEditorId);
            if (resolvedFormKey.HasValue)
            {
                outfitFormKey = resolvedFormKey;
                if (linkCache.TryResolve<IOutfitGetter>(resolvedFormKey.Value, out var outfit))
                {
                    outfitEditorId = outfit.EditorID;
                }
            }
        }

        // Also check filterByOutfits= syntax
        if (!outfitFormKey.HasValue)
        {
            var filterByOutfitsIndex = trimmed.IndexOf("filterByOutfits=", StringComparison.OrdinalIgnoreCase);
            if (filterByOutfitsIndex >= 0)
            {
                var outfitStart = filterByOutfitsIndex + "filterByOutfits=".Length;
                var outfitEnd = trimmed.IndexOf(':', outfitStart);
                var outfitString = (outfitEnd > outfitStart
                    ? trimmed.Substring(outfitStart, outfitEnd - outfitStart)
                    : trimmed.Substring(outfitStart)).Trim();

                var resolvedFormKey = FormKeyHelper.ResolveOutfit(outfitString, linkCache, outfitByEditorId);
                if (resolvedFormKey.HasValue)
                {
                    outfitFormKey = resolvedFormKey;
                    if (linkCache.TryResolve<IOutfitGetter>(resolvedFormKey.Value, out var outfit))
                    {
                        outfitEditorId = outfit.EditorID;
                    }
                }
            }
        }

        if (outfitFormKey.HasValue && npcFormKeys.Count > 0)
        {
            foreach (var npcFormKey in npcFormKeys)
            {
                results.Add((npcFormKey, outfitFormKey.Value, outfitEditorId));
            }
        }
    }

    private static List<NpcOutfitAssignment> BuildNpcOutfitAssignmentsFromFilterData(
        Dictionary<FormKey, List<OutfitDistribution>> npcDistributions,
        IReadOnlyList<NpcFilterData> allNpcs)
    {
        var assignments = new List<NpcOutfitAssignment>();
        var npcLookup = allNpcs.ToDictionary(n => n.FormKey);

        foreach (var (npcFormKey, distributions) in npcDistributions)
        {
            // Sort distributions by processing order
            var sortedDistributions = distributions
                .OrderBy(d => d.ProcessingOrder)
                .ToList();

            // Filter out ESP distributions if there are INI distributions (INI wins)
            // Only consider conflicts between INI files, not ESP vs INI
            var iniDistributions = sortedDistributions
                .Where(d => d.FileType != DistributionFileType.Esp)
                .ToList();

            var hasIniDistributions = iniDistributions.Count > 0;
            var distributionsToUse = hasIniDistributions ? iniDistributions : sortedDistributions;

            // Mark the winner (last one in the filtered list)
            var winnerIndex = distributionsToUse.Count - 1;
            var updatedDistributions = sortedDistributions
                .Select((d, i) =>
                {
                    // Mark as winner if it's the last INI distribution, or if there are no INI distributions and it's the last overall
                    var isWinner = hasIniDistributions
                        ? (d.FileType != DistributionFileType.Esp && i == sortedDistributions.IndexOf(distributionsToUse[winnerIndex]))
                        : (i == sortedDistributions.Count - 1);
                    return d with { IsWinner = isWinner };
                })
                .ToList();

            var winner = distributionsToUse[winnerIndex];

            string? editorId = null;
            string? name = null;
            ModKey sourceMod = npcFormKey.ModKey;

            if (npcLookup.TryGetValue(npcFormKey, out var npcData))
            {
                editorId = npcData.EditorId;
                name = npcData.Name;
                sourceMod = npcData.SourceMod;
            }

            // Only count conflicts between INI files, not ESP vs INI
            var iniOnlyDistributions = updatedDistributions
                .Where(d => d.FileType != DistributionFileType.Esp)
                .ToList();
            var hasConflict = iniOnlyDistributions.Count > 1;

            assignments.Add(new NpcOutfitAssignment(
                NpcFormKey: npcFormKey,
                EditorId: editorId,
                Name: name,
                SourceMod: sourceMod,
                FinalOutfitFormKey: winner.OutfitFormKey,
                FinalOutfitEditorId: winner.OutfitEditorId,
                Distributions: updatedDistributions,
                HasConflict: hasConflict
            ));
        }

        // Sort by display name for UI
        return assignments
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<DistributionFile> SortDistributionFiles(IReadOnlyList<DistributionFile> files)
    {
        // SPID files first (alphabetically by filename), then SkyPatcher files (alphabetically by filename)
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

        foreach (var file in sorted)
        {
            _logger.Debug("Processing order: {Type} - {FileName}", file.Type, file.FileName);
        }

        return sorted;
    }

    private void ProcessDistributionFile(
        DistributionFile file,
        int processingOrder,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter> npcByEditorId,
        Dictionary<string, INpcGetter> npcByName,
        IReadOnlyDictionary<string, FormKey> outfitByEditorId,
        Dictionary<FormKey, List<OutfitDistribution>> npcDistributions)
    {
        var outfitLineCount = 0;
        var parsedEntryCount = 0;

        foreach (var line in file.Lines)
        {
            if (!line.IsOutfitDistribution)
                continue;

            outfitLineCount++;
            _logger.Debug("Processing outfit line {LineNum} in {File}: {Text}",
                line.LineNumber, file.FileName, line.RawText.Length > 100 ? line.RawText[..100] + "..." : line.RawText);

            // Parse the line to extract NPC targets and outfit
            var parsedEntries = ParseDistributionLine(file, line, linkCache, npcByEditorId, npcByName, outfitByEditorId);

            _logger.Debug("Parsed {Count} NPC-outfit entries from line {LineNum}", parsedEntries.Count, line.LineNumber);

            foreach (var (npcFormKey, outfitFormKey, outfitEditorId) in parsedEntries)
            {
                parsedEntryCount++;

                if (!npcDistributions.TryGetValue(npcFormKey, out var distributions))
                {
                    distributions = [];
                    npcDistributions[npcFormKey] = distributions;
                }

                distributions.Add(new OutfitDistribution(
                    FilePath: file.FullPath,
                    FileName: file.FileName,
                    FileType: file.Type,
                    OutfitFormKey: outfitFormKey,
                    OutfitEditorId: outfitEditorId,
                    ProcessingOrder: processingOrder,
                    IsWinner: false // Will be set later
                ));

                _logger.Debug("Added distribution: NPC={NpcFormKey}, Outfit={OutfitFormKey} ({OutfitEditorId})",
                    npcFormKey, outfitFormKey, outfitEditorId ?? "null");
            }
        }

        _logger.Debug("File {FileName} summary: {OutfitLines} outfit lines, {ParsedEntries} parsed entries",
            file.FileName, outfitLineCount, parsedEntryCount);
    }

    private List<(FormKey NpcFormKey, FormKey OutfitFormKey, string? OutfitEditorId)> ParseDistributionLine(
        DistributionFile file,
        DistributionLine line,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter> npcByEditorId,
        Dictionary<string, INpcGetter> npcByName,
        IReadOnlyDictionary<string, FormKey> outfitByEditorId)
    {
        var results = new List<(FormKey, FormKey, string?)>();

        if (file.Type == DistributionFileType.SkyPatcher)
        {
            ParseSkyPatcherLine(line.RawText, linkCache, outfitByEditorId, results);
        }
        else if (file.Type == DistributionFileType.Spid)
        {
            ParseSpidLine(line.RawText, linkCache, npcByEditorId, npcByName, results);
        }

        return results;
    }

    private void ParseSkyPatcherLine(
        string lineText,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyDictionary<string, FormKey> outfitByEditorId,
        List<(FormKey, FormKey, string?)> results)
    {
        // SkyPatcher format: filterByNpcs=ModKey|FormID,ModKey|FormID:outfitDefault=ModKey|FormID or EditorID
        var trimmed = lineText.Trim();

        _logger.Debug("ParseSkyPatcherLine: {Line}", trimmed.Length > 150 ? trimmed[..150] + "..." : trimmed);

        // Extract NPC FormKeys
        var npcFormKeys = new List<FormKey>();
        var filterByNpcsIndex = trimmed.IndexOf("filterByNpcs=", StringComparison.OrdinalIgnoreCase);

        _logger.Debug("filterByNpcs index: {Index}", filterByNpcsIndex);

        if (filterByNpcsIndex >= 0)
        {
            var npcStart = filterByNpcsIndex + "filterByNpcs=".Length;
            var npcEnd = trimmed.IndexOf(':', npcStart);
            _logger.Debug("NPC section: start={Start}, end={End}", npcStart, npcEnd);

            if (npcEnd > npcStart)
            {
                var npcString = trimmed.Substring(npcStart, npcEnd - npcStart);
                _logger.Debug("NPC string to parse: {NpcString}", npcString);

                foreach (var npcPart in npcString.Split(','))
                {
                    if (FormKeyHelper.TryParse(npcPart.Trim(), out var formKey))
                    {
                        npcFormKeys.Add(formKey);
                        _logger.Debug("Parsed NPC FormKey: {FormKey}", formKey);
                    }
                    else
                    {
                        _logger.Debug("Failed to parse NPC FormKey from: {Part}", npcPart.Trim());
                    }
                }
            }
        }

        // Extract outfit - supports both FormKey format (ModKey|FormID) and EditorID
        FormKey? outfitFormKey = null;
        string? outfitEditorId = null;
        var outfitDefaultIndex = trimmed.IndexOf("outfitDefault=", StringComparison.OrdinalIgnoreCase);

        _logger.Debug("outfitDefault index: {Index}", outfitDefaultIndex);

        if (outfitDefaultIndex >= 0)
        {
            var outfitStart = outfitDefaultIndex + "outfitDefault=".Length;
            var outfitEnd = trimmed.IndexOf(':', outfitStart);
            var outfitString = (outfitEnd > outfitStart
                ? trimmed.Substring(outfitStart, outfitEnd - outfitStart)
                : trimmed.Substring(outfitStart)).Trim();

            _logger.Debug("Outfit string to resolve: {OutfitString}", outfitString);

            var resolvedFormKey = FormKeyHelper.ResolveOutfit(outfitString, linkCache, outfitByEditorId);
            if (resolvedFormKey.HasValue)
            {
                outfitFormKey = resolvedFormKey;
                outfitEditorId = outfitString; // Use the original string as EditorID hint
                _logger.Debug("Resolved outfit FormKey: {FormKey}", resolvedFormKey.Value);
                if (linkCache.TryResolve<IOutfitGetter>(resolvedFormKey.Value, out var outfit))
                {
                    outfitEditorId = outfit.EditorID;
                    _logger.Debug("Outfit EditorID: {EditorId}", outfitEditorId);
                }
            }
            else
            {
                _logger.Debug("Failed to resolve outfit: {OutfitString}", outfitString);
            }
        }

        // Also check filterByOutfits= syntax (alternative SkyPatcher format)
        if (!outfitFormKey.HasValue)
        {
            var filterByOutfitsIndex = trimmed.IndexOf("filterByOutfits=", StringComparison.OrdinalIgnoreCase);
            if (filterByOutfitsIndex >= 0)
            {
                var outfitStart = filterByOutfitsIndex + "filterByOutfits=".Length;
                var outfitEnd = trimmed.IndexOf(':', outfitStart);
                var outfitString = (outfitEnd > outfitStart
                    ? trimmed.Substring(outfitStart, outfitEnd - outfitStart)
                    : trimmed.Substring(outfitStart)).Trim();

                var resolvedFormKey = FormKeyHelper.ResolveOutfit(outfitString, linkCache, outfitByEditorId);
                if (resolvedFormKey.HasValue)
                {
                    outfitFormKey = resolvedFormKey;
                    if (linkCache.TryResolve<IOutfitGetter>(resolvedFormKey.Value, out var outfit))
                    {
                        outfitEditorId = outfit.EditorID;
                    }
                }
            }
        }

        _logger.Debug("SkyPatcher parse result: {NpcCount} NPCs, outfit={OutfitFormKey}",
            npcFormKeys.Count, outfitFormKey?.ToString() ?? "null");

        if (outfitFormKey.HasValue && npcFormKeys.Count > 0)
        {
            foreach (var npcFormKey in npcFormKeys)
            {
                results.Add((npcFormKey, outfitFormKey.Value, outfitEditorId));
            }
        }
    }

    private void ParseSpidLine(
        string lineText,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter> npcByEditorId,
        Dictionary<string, INpcGetter> npcByName,
        List<(FormKey, FormKey, string?)> results)
    {
        // SPID format: Outfit = 0x800~ModKey|EditorID[,EditorID,...]
        var trimmed = lineText.Trim();

        _logger.Debug("ParseSpidLine: {Line}", trimmed.Length > 150 ? trimmed[..150] + "..." : trimmed);

        if (!trimmed.StartsWith("Outfit", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Debug("Line does not start with 'Outfit', skipping");
            return;
        }

        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex < 0)
        {
            _logger.Debug("No '=' found in line");
            return;
        }

        var valuePart = trimmed.Substring(equalsIndex + 1).Trim();
        _logger.Debug("Value part: {ValuePart}", valuePart);

        // Find the ~ separator between FormID and ModKey
        var tildeIndex = valuePart.IndexOf('~');
        if (tildeIndex < 0)
        {
            _logger.Debug("No '~' found in value part");
            return;
        }

        var formIdString = valuePart.Substring(0, tildeIndex).Trim();
        var rest = valuePart.Substring(tildeIndex + 1).Trim();

        // Parse FormID
        formIdString = formIdString.Replace("0x", "").Replace("0X", "");
        if (!uint.TryParse(formIdString, System.Globalization.NumberStyles.HexNumber, null, out var formId))
        {
            _logger.Debug("Failed to parse FormID: {FormIdString}", formIdString);
            return;
        }

        // Find the | separator between ModKey and EditorIDs
        var pipeIndex = rest.IndexOf('|');
        if (pipeIndex < 0)
        {
            _logger.Debug("No '|' found after ModKey");
            return;
        }

        var modKeyString = rest.Substring(0, pipeIndex).Trim();
        var editorIdsString = rest.Substring(pipeIndex + 1).Trim();

        if (!ModKey.TryFromNameAndExtension(modKeyString, out var modKey))
        {
            _logger.Debug("Failed to parse ModKey: {ModKeyString}", modKeyString);
            return;
        }

        var outfitFormKey = new FormKey(modKey, formId);
        _logger.Debug("Parsed outfit FormKey: {FormKey}", outfitFormKey);

        string? outfitEditorId = null;

        if (linkCache.TryResolve<IOutfitGetter>(outfitFormKey, out var outfit))
        {
            outfitEditorId = outfit.EditorID;
            _logger.Debug("Resolved outfit EditorID: {EditorId}", outfitEditorId);
        }
        else
        {
            _logger.Debug("Could not resolve outfit FormKey in LinkCache");
        }

        // Parse NPC identifiers (comma-separated)
        var npcIdentifiers = editorIdsString
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        _logger.Debug("Found {Count} NPC identifiers: {Identifiers}",
            npcIdentifiers.Count, string.Join(", ", npcIdentifiers.Take(5)));

        var resolvedCount = 0;
        foreach (var identifier in npcIdentifiers)
        {
            // Try to find NPC by EditorID first, then by Name
            INpcGetter? npc = null;
            if (npcByEditorId.TryGetValue(identifier, out var npcById))
            {
                npc = npcById;
                _logger.Debug("Resolved NPC '{Identifier}' by EditorID: {FormKey}", identifier, npc.FormKey);
            }
            else if (npcByName.TryGetValue(identifier, out var npcByNameMatch))
            {
                npc = npcByNameMatch;
                _logger.Debug("Resolved NPC '{Identifier}' by Name: {FormKey}", identifier, npc.FormKey);
            }
            else
            {
                _logger.Debug("Could not resolve NPC identifier: {Identifier}", identifier);
            }

            if (npc != null)
            {
                results.Add((npc.FormKey, outfitFormKey, outfitEditorId));
                resolvedCount++;
            }
        }

        _logger.Debug("SPID line resolved {Resolved}/{Total} NPCs", resolvedCount, npcIdentifiers.Count);
    }

    private void ProcessEspProvidedOutfits(
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        List<INpcGetter> allNpcs,
        Dictionary<FormKey, List<OutfitDistribution>> npcDistributions)
    {
        var espOutfitCount = 0;

        foreach (var npc in allNpcs)
        {
            // Check if NPC has a default outfit
            var defaultOutfit = npc.DefaultOutfit;
            if (defaultOutfit == null || defaultOutfit.IsNull)
                continue;

            // Resolve the outfit
            if (!linkCache.TryResolve<IOutfitGetter>(defaultOutfit.FormKey, out var outfit))
                continue;

            var npcFormKey = npc.FormKey;
            if (!npcDistributions.TryGetValue(npcFormKey, out var distributions))
            {
                distributions = [];
                npcDistributions[npcFormKey] = distributions;
            }

            var sourcePlugin = npcFormKey.ModKey.FileName;

            distributions.Add(new OutfitDistribution(
                FilePath: $"{sourcePlugin} (ESP)",
                FileName: sourcePlugin,
                FileType: DistributionFileType.Esp,
                OutfitFormKey: outfit.FormKey,
                OutfitEditorId: outfit.EditorID,
                ProcessingOrder: 0, // ESP has lowest priority
                IsWinner: false, // Will be determined later
                RawLine: null,
                TargetingDescription: "Default outfit from ESP",
                Chance: 100,
                TargetsAllNpcs: false,
                UsesKeywordTargeting: false,
                UsesFactionTargeting: false,
                UsesRaceTargeting: false,
                UsesTraitTargeting: false
            ));

            espOutfitCount++;
        }

        _logger.Debug("Found {Count} NPCs with ESP-provided default outfits", espOutfitCount);
    }

    private void ProcessEspProvidedOutfitsFromFilterData(
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        IReadOnlyList<NpcFilterData> allNpcs,
        Dictionary<FormKey, List<OutfitDistribution>> npcDistributions)
    {
        var espOutfitCount = 0;

        foreach (var npcData in allNpcs)
        {
            // Check if NPC has a default outfit
            var defaultOutfitFormKey = npcData.DefaultOutfitFormKey;
            if (!defaultOutfitFormKey.HasValue || defaultOutfitFormKey.Value.IsNull)
                continue;

            // Resolve the outfit
            if (!linkCache.TryResolve<IOutfitGetter>(defaultOutfitFormKey.Value, out var outfit))
                continue;

            var npcFormKey = npcData.FormKey;
            if (!npcDistributions.TryGetValue(npcFormKey, out var distributions))
            {
                distributions = [];
                npcDistributions[npcFormKey] = distributions;
            }

            var sourcePlugin = npcData.SourceMod.FileName;

            distributions.Add(new OutfitDistribution(
                FilePath: $"{sourcePlugin} (ESP)",
                FileName: sourcePlugin,
                FileType: DistributionFileType.Esp,
                OutfitFormKey: outfit.FormKey,
                OutfitEditorId: outfit.EditorID ?? npcData.DefaultOutfitEditorId,
                ProcessingOrder: 0, // ESP has lowest priority
                IsWinner: false, // Will be determined later
                RawLine: null,
                TargetingDescription: "Default outfit from ESP",
                Chance: 100,
                TargetsAllNpcs: false,
                UsesKeywordTargeting: false,
                UsesFactionTargeting: false,
                UsesRaceTargeting: false,
                UsesTraitTargeting: false
            ));

            espOutfitCount++;
        }

        _logger.Debug("Found {Count} NPCs with ESP-provided default outfits", espOutfitCount);
    }

    private static List<NpcOutfitAssignment> BuildNpcOutfitAssignments(
        Dictionary<FormKey, List<OutfitDistribution>> npcDistributions,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        List<INpcGetter> allNpcs)
    {
        var assignments = new List<NpcOutfitAssignment>();
        var npcLookup = allNpcs.ToDictionary(n => n.FormKey);

        foreach (var (npcFormKey, distributions) in npcDistributions)
        {
            // Sort distributions by processing order
            var sortedDistributions = distributions
                .OrderBy(d => d.ProcessingOrder)
                .ToList();

            // Filter out ESP distributions if there are INI distributions (INI wins)
            // Only consider conflicts between INI files, not ESP vs INI
            var iniDistributions = sortedDistributions
                .Where(d => d.FileType != DistributionFileType.Esp)
                .ToList();

            var hasIniDistributions = iniDistributions.Count > 0;
            var distributionsToUse = hasIniDistributions ? iniDistributions : sortedDistributions;

            // Mark the winner (last one in the filtered list)
            var winnerIndex = distributionsToUse.Count - 1;
            var updatedDistributions = sortedDistributions
                .Select((d, i) =>
                {
                    // Mark as winner if it's the last INI distribution, or if there are no INI distributions and it's the last overall
                    var isWinner = hasIniDistributions
                        ? (d.FileType != DistributionFileType.Esp && i == sortedDistributions.IndexOf(distributionsToUse[winnerIndex]))
                        : (i == sortedDistributions.Count - 1);
                    return d with { IsWinner = isWinner };
                })
                .ToList();

            var winner = distributionsToUse[winnerIndex];

            string? editorId = null;
            string? name = null;
            ModKey sourceMod = npcFormKey.ModKey;

            if (npcLookup.TryGetValue(npcFormKey, out var npc))
            {
                editorId = npc.EditorID;
                name = npc.Name?.String;

                try
                {
                    var contexts = linkCache.ResolveAllContexts<INpc, INpcGetter>(npcFormKey);
                    var firstContext = contexts.FirstOrDefault();
                    if (firstContext != null)
                        sourceMod = firstContext.ModKey;
                }
                catch { }
            }

            // Only count conflicts between INI files, not ESP vs INI
            var iniOnlyDistributions = updatedDistributions
                .Where(d => d.FileType != DistributionFileType.Esp)
                .ToList();
            var hasConflict = iniOnlyDistributions.Count > 1;

            assignments.Add(new NpcOutfitAssignment(
                NpcFormKey: npcFormKey,
                EditorId: editorId,
                Name: name,
                SourceMod: sourceMod,
                FinalOutfitFormKey: winner.OutfitFormKey,
                FinalOutfitEditorId: winner.OutfitEditorId,
                Distributions: updatedDistributions,
                HasConflict: hasConflict
            ));
        }

        // Sort by display name for UI
        return assignments
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
