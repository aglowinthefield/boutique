using System.Collections.Concurrent;
using System.Globalization;
using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class NpcOutfitResolutionService
{
    private readonly KeywordDistributionResolver _keywordResolver;
    private readonly ILogger _logger;
    private readonly MutagenService _mutagenService;

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

        return await Task.Run<IReadOnlyList<NpcOutfitAssignment>>(
            () =>
            {
                if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
                {
                    _logger.Warning("LinkCache not available for NPC outfit resolution.");
                    return [];
                }

                _logger.Debug("LinkCache is available");

                try
                {
                    var sortedFiles = SortDistributionFiles(distributionFiles);
                    _logger.Information("Processing {Count} distribution files in order", sortedFiles.Count);

                    foreach (var file in sortedFiles)
                    {
                        var outfitLineCount = file.Lines.Count(l => l.IsOutfitDistribution);
                        _logger.Debug(
                            "File: {FileName} ({Type}) - {TotalLines} lines, {OutfitLines} outfit distributions",
                            file.FileName,
                            file.Type,
                            file.Lines.Count,
                            outfitLineCount);
                    }

                    var npcDistributions = new Dictionary<FormKey, List<OutfitDistribution>>();

                    _logger.Debug("Loading all NPCs from LinkCache...");
                    var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
                    _logger.Debug("Loaded {Count} NPCs from LinkCache", allNpcs.Count);

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

                    var outfitByEditorId = FormKeyHelper.BuildOutfitEditorIdLookup(linkCache);
                    _logger.Debug("Built Outfit EditorID lookup with {Count} entries", outfitByEditorId.Count);

                    _logger.Debug("Scanning NPCs for ESP-provided default outfits...");
                    ProcessEspProvidedOutfits(linkCache, allNpcs, npcDistributions);
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

                        ProcessDistributionFile(
                            file,
                            fileIndex + 1,
                            linkCache,
                            npcByEditorId,
                            npcByName,
                            outfitByEditorId,
                            npcDistributions);

                        _logger.Debug(
                            "After processing {FileName}: {NpcCount} unique NPCs with distributions",
                            file.FileName,
                            npcDistributions.Count);
                    }

                    _logger.Debug("Total unique NPCs with distributions: {Count}", npcDistributions.Count);

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
            },
            cancellationToken);
    }

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
                if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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

                    var keywordEntries = _keywordResolver.ParseKeywordDistributions(sortedFiles);
                    var (sortedKeywords, cyclicKeywords) = _keywordResolver.TopologicalSort(keywordEntries);

                    if (cyclicKeywords.Count > 0)
                    {
                        _logger.Warning(
                            "Skipping {Count} keywords with circular dependencies: {Keywords}",
                            cyclicKeywords.Count,
                            string.Join(", ", cyclicKeywords.Take(5)));
                    }

                    var simulatedKeywords = _keywordResolver.SimulateKeywordDistribution(sortedKeywords, npcFilterData);
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
        var spidLines = new List<(DistributionLine Line, SpidDistributionFilter Filter, FormKey OutfitFormKey, string? OutfitEditorId, bool HasRaceTargeting, bool UsesKeywordTargeting, bool UsesFactionTargeting, string TargetingDescription, bool HasTraitFilters)>();
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
                    var outfitFormKey = FormKeyHelper.ResolveOutfit(filter.OutfitIdentifier, linkCache);
                    if (outfitFormKey != null && !outfitFormKey.Value.IsNull)
                    {
                        string? outfitEditorId = null;
                        if (linkCache.TryResolve<IOutfitGetter>(outfitFormKey.Value, out var outfit))
                        {
                            outfitEditorId = outfit.EditorID;
                        }

                        // Use pre-built lookups for classification
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
                        var hasTraitFilters = !filter.TraitFilters.IsEmpty;

                        spidLines.Add((line, filter, outfitFormKey.Value, outfitEditorId, hasRaceTargeting, usesKeywordTargeting, usesFactionTargeting, targetingDescription, hasTraitFilters));
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
            Parallel.ForEach(allNpcs, npc =>
            {
                simulatedKeywords.TryGetValue(npc.FormKey, out var virtualKeywords);

                foreach (var (line, filter, outfitFormKey, outfitEditorId, hasRaceTargeting, usesKeywordTargeting, usesFactionTargeting, targetingDescription, hasTraitFilters) in spidLines)
                {
                    if (SpidFilterMatchingService.NpcMatchesFilterForBatch(npc, filter, virtualKeywords))
                    {
                        var bag = localDistributions.GetOrAdd(npc.FormKey, _ => new ConcurrentBag<OutfitDistribution>());

                        bag.Add(new OutfitDistribution(
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
                ParseSkyPatcherLineForFilteredResolution(line.RawText, linkCache, outfitByEditorId, results);

                foreach (var (npcFormKey, outfitFormKey, outfitEditorId) in results)
                {
                    var bag = localDistributions.GetOrAdd(npcFormKey, _ => new ConcurrentBag<OutfitDistribution>());
                    bag.Add(new OutfitDistribution(
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
                list = [];
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

    private static void ParseSkyPatcherLineForFilteredResolution(
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
        var npcFormKeys = new List<FormKey>();
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

        var resolvedFormKey = FormKeyHelper.ResolveOutfit(outfitString, linkCache, outfitByEditorId);
        if (!resolvedFormKey.HasValue)
        {
            return (null, null);
        }

        var editorId = linkCache.TryResolve<IOutfitGetter>(resolvedFormKey.Value, out var outfit)
            ? outfit.EditorID
            : null;

        return (resolvedFormKey, editorId);
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
                        ? d.FileType != DistributionFileType.Esp &&
                          i == sortedDistributions.IndexOf(distributionsToUse[winnerIndex])
                        : i == sortedDistributions.Count - 1;
                    return d with { IsWinner = isWinner };
                })
                .ToList();

            var winner = distributionsToUse[winnerIndex];

            string? editorId = null;
            string? name = null;
            var sourceMod = npcFormKey.ModKey;

            if (npcLookup.TryGetValue(npcFormKey, out var npcData))
            {
                editorId = npcData.EditorId;
                name = npcData.Name;
                sourceMod = npcData.SourceMod;
            }

            var iniOnlyDistributions = updatedDistributions
                .Where(d => d.FileType != DistributionFileType.Esp)
                .ToList();
            var hasConflict = iniOnlyDistributions.Count > 1;

            assignments.Add(new NpcOutfitAssignment(
                npcFormKey,
                editorId,
                name,
                sourceMod,
                winner.OutfitFormKey,
                winner.OutfitEditorId,
                updatedDistributions,
                hasConflict));
        }

        // Sort by display name for UI
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
            {
                continue;
            }

            outfitLineCount++;
            _logger.Debug(
                "Processing outfit line {LineNum} in {File}: {Text}",
                line.LineNumber,
                file.FileName,
                line.RawText.Length > 100 ? line.RawText[..100] + "..." : line.RawText);

            var parsedEntries =
                ParseDistributionLine(file, line, linkCache, npcByEditorId, npcByName, outfitByEditorId);

            _logger.Debug(
                "Parsed {Count} NPC-outfit entries from line {LineNum}",
                parsedEntries.Count,
                line.LineNumber);

            foreach (var (npcFormKey, outfitFormKey, outfitEditorId) in parsedEntries)
            {
                parsedEntryCount++;

                if (!npcDistributions.TryGetValue(npcFormKey, out var distributions))
                {
                    distributions = [];
                    npcDistributions[npcFormKey] = distributions;
                }

                distributions.Add(new OutfitDistribution(
                    file.FullPath,
                    file.FileName,
                    file.Type,
                    outfitFormKey,
                    outfitEditorId,
                    processingOrder,
                    false)); // Will be set later

                _logger.Debug(
                    "Added distribution: NPC={NpcFormKey}, Outfit={OutfitFormKey} ({OutfitEditorId})",
                    npcFormKey,
                    outfitFormKey,
                    outfitEditorId ?? "null");
            }
        }

        _logger.Debug(
            "File {FileName} summary: {OutfitLines} outfit lines, {ParsedEntries} parsed entries",
            file.FileName,
            outfitLineCount,
            parsedEntryCount);
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
        List<(FormKey npcFormKey, FormKey outfitFormKey, string? outfitEditorId)> results)
    {
        _logger.Debug("ParseSkyPatcherLine: {Line}", lineText.Length > 150 ? lineText[..150] + "..." : lineText);

        var npcFormKeys = ParseNpcFormKeysWithEditorIdFallback(lineText, linkCache);
        var (outfitFormKey, outfitEditorId) = ResolveOutfitFromLine(lineText, linkCache, outfitByEditorId);

        _logger.Debug(
            "SkyPatcher parse result: {NpcCount} NPCs, outfit={OutfitFormKey}",
            npcFormKeys.Count,
            outfitFormKey?.ToString() ?? "null");

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

    private void ParseSpidLine(
        string lineText,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter> npcByEditorId,
        Dictionary<string, INpcGetter> npcByName,
        List<(FormKey npcFormKey, FormKey outfitFormKey, string? outfitEditorId)> results)
    {
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

        formIdString = formIdString.Replace("0x", string.Empty).Replace("0X", string.Empty);
        if (!uint.TryParse(formIdString, NumberStyles.HexNumber, null, out var formId))
        {
            _logger.Debug("Failed to parse FormID: {FormIdString}", formIdString);
            return;
        }

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

        var outfitEditorId = linkCache.TryResolve<IOutfitGetter>(outfitFormKey, out var outfit)
            ? outfit.EditorID
            : null;

        if (outfit is not null)
        {
            _logger.Debug("Resolved outfit EditorID: {EditorId}", outfitEditorId);
        }
        else
        {
            _logger.Debug("Could not resolve outfit FormKey in LinkCache");
        }

        var npcIdentifiers = editorIdsString
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        _logger.Debug(
            "Found {Count} NPC identifiers: {Identifiers}",
            npcIdentifiers.Count,
            string.Join(", ", npcIdentifiers.Take(5)));

        var resolvedCount = 0;
        foreach (var identifier in npcIdentifiers)
        {
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

            if (npc is null)
            {
                continue;
            }

            results.Add((npc.FormKey, outfitFormKey, outfitEditorId));
            resolvedCount++;
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
            var defaultOutfit = npc.DefaultOutfit;
            if (defaultOutfit is null || defaultOutfit.IsNull)
            {
                continue;
            }

            if (!linkCache.TryResolve<IOutfitGetter>(defaultOutfit.FormKey, out var outfit))
            {
                continue;
            }

            var npcFormKey = npc.FormKey;
            if (!npcDistributions.TryGetValue(npcFormKey, out var distributions))
            {
                distributions = [];
                npcDistributions[npcFormKey] = distributions;
            }

            var sourcePlugin = npcFormKey.ModKey.FileName;

            distributions.Add(new OutfitDistribution(
                $"{sourcePlugin} (ESP)",
                sourcePlugin,
                DistributionFileType.Esp,
                outfit.FormKey,
                outfit.EditorID,
                0, // ESP has lowest priority
                false, // Will be determined later
                null,
                "Default outfit from ESP"));

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
                distributions = [];
                npcDistributions[npcFormKey] = distributions;
            }

            var sourcePlugin = npcData.SourceMod.FileName;

            distributions.Add(new OutfitDistribution(
                $"{sourcePlugin} (ESP)",
                sourcePlugin,
                DistributionFileType.Esp,
                outfit.FormKey,
                outfit.EditorID ?? npcData.DefaultOutfitEditorId,
                0, // ESP has lowest priority
                false, // Will be determined later
                null,
                "Default outfit from ESP"));

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
                        ? d.FileType != DistributionFileType.Esp &&
                          i == sortedDistributions.IndexOf(distributionsToUse[winnerIndex])
                        : i == sortedDistributions.Count - 1;
                    return d with { IsWinner = isWinner };
                })
                .ToList();

            var winner = distributionsToUse[winnerIndex];

            string? editorId = null;
            string? name = null;
            var sourceMod = npcFormKey.ModKey;

            if (npcLookup.TryGetValue(npcFormKey, out var npc))
            {
                editorId = npc.EditorID;
                name = npc.Name?.String;

                try
                {
                    var contexts = linkCache.ResolveAllContexts<INpc, INpcGetter>(npcFormKey);
                    var firstContext = contexts.FirstOrDefault();
                    if (firstContext != null)
                    {
                        sourceMod = firstContext.ModKey;
                    }
                }
                catch { }
            }

            var iniOnlyDistributions = updatedDistributions
                .Where(d => d.FileType != DistributionFileType.Esp)
                .ToList();
            var hasConflict = iniOnlyDistributions.Count > 1;

            assignments.Add(new NpcOutfitAssignment(
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
}
