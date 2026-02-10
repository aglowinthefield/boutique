using System.IO;
using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class DistributionFileEditorService(MutagenService mutagenService, ILogger logger)
{
  private readonly ILogger _logger = logger.ForContext<DistributionFileEditorService>();

  public async Task<(IReadOnlyList<DistributionEntry> Entries, DistributionFileType DetectedFormat)>
    LoadDistributionFileWithFormatAsync(
      string filePath,
      CancellationToken cancellationToken = default)
  {
    var (entries, detectedFormat, _) = await LoadDistributionFileWithErrorsAsync(filePath, cancellationToken);
    return (entries, detectedFormat);
  }

  public async
    Task<(IReadOnlyList<DistributionEntry> Entries, DistributionFileType DetectedFormat,
      IReadOnlyList<DistributionParseError> ParseErrors)> LoadDistributionFileWithErrorsAsync(
      string filePath,
      CancellationToken cancellationToken = default)
  {
    return await Task.Run(
             () =>
             {
               var entries        = new List<DistributionEntry>();
               var parseErrors    = new List<DistributionParseError>();
               var hasSpidLines   = false;
               var detectedFormat = DistributionFileType.SkyPatcher;

               if (!File.Exists(filePath))
               {
                 _logger.Warning("Distribution file does not exist: {FilePath}", filePath);
                 return (entries, detectedFormat, parseErrors);
               }

               if (mutagenService.LinkCache is not { } linkCache)
               {
                 _logger.Warning("LinkCache not available. Cannot load distribution file.");
                 return (entries, detectedFormat, parseErrors);
               }

               try
               {
                 var lines = File.ReadAllLines(filePath, Encoding.UTF8);

                 List<INpcGetter>?    cachedNpcs    = null;
                 List<IOutfitGetter>? cachedOutfits = null;
                 FormIdLookupCache?   formIdCache   = null;

                 var outfitByEditorId = FormKeyHelper.BuildOutfitEditorIdLookup(linkCache);
                 var virtualKeywords  = ExtractVirtualKeywordsFromLines(lines);

                 for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
                 {
                   cancellationToken.ThrowIfCancellationRequested();

                   var line    = lines[lineNumber];
                   var trimmed = line.Trim();
                   if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                   {
                     continue;
                   }

                   DistributionEntry? entry              = null;
                   string?            parseFailureReason = null;

                   if (trimmed.Contains("outfitDefault=", StringComparison.OrdinalIgnoreCase))
                   {
                     var (parsedEntry, reason) = ParseDistributionLine(trimmed, linkCache, outfitByEditorId);
                     entry                     = parsedEntry;
                     parseFailureReason        = reason;
                   }
                   else if (SpidLineParser.TryParse(trimmed, out var spidFilter) && spidFilter != null)
                   {
                     hasSpidLines = true;

                     if (spidFilter.FormType == SpidFormType.Outfit)
                     {
                       cachedNpcs    ??= linkCache.PriorityOrder.WinningOverrides<INpcGetter>().ToList();
                       cachedOutfits ??= linkCache.PriorityOrder.WinningOverrides<IOutfitGetter>().ToList();
                       formIdCache   ??= new FormIdLookupCache(linkCache);

                       entry = SpidFilterResolver.Resolve(
                         spidFilter,
                         linkCache,
                         cachedNpcs,
                         cachedOutfits,
                         virtualKeywords,
                         formIdCache,
                         _logger);
                       if (entry == null)
                       {
                         parseFailureReason = "Could not resolve outfit or filters from SPID syntax";
                       }
                     }
                     else if (spidFilter.FormType == SpidFormType.Keyword)
                     {
                       cachedNpcs  ??= linkCache.PriorityOrder.WinningOverrides<INpcGetter>().ToList();
                       formIdCache ??= new FormIdLookupCache(linkCache);

                       entry = SpidFilterResolver.ResolveKeyword(
                         spidFilter,
                         linkCache,
                         cachedNpcs,
                         virtualKeywords,
                         formIdCache,
                         _logger);
                       if (entry == null)
                       {
                         parseFailureReason =
                           "Could not resolve keyword distribution filters from SPID syntax";
                       }
                     }
                     else if (spidFilter.FormType == SpidFormType.ExclusiveGroup)
                     {
                       entry = new DistributionEntry
                               {
                                 Type                = DistributionType.ExclusiveGroup,
                                 ExclusiveGroupName  = spidFilter.FormIdentifier,
                                 ExclusiveGroupForms = spidFilter.ExclusiveGroupForms.ToList()
                               };
                     }
                     else
                     {
                       parseErrors.Add(
                         new DistributionParseError(
                           lineNumber + 1,
                           trimmed,
                           $"{spidFilter.FormType} distribution (preserved)"));
                     }
                   }
                   else
                   {
                     parseFailureReason = "Unrecognized distribution syntax";
                   }

                   if (entry != null)
                   {
                     entries.Add(entry);
                   }
                   else if (parseFailureReason != null)
                   {
                     parseErrors.Add(new DistributionParseError(lineNumber + 1, trimmed, parseFailureReason));
                   }
                 }

                 detectedFormat = hasSpidLines ? DistributionFileType.Spid : DistributionFileType.SkyPatcher;

                 _logger.Information(
                   "Loaded {Count} distribution entries from {FilePath} (detected format: {Format}, {ErrorCount} parse errors)",
                   entries.Count,
                   filePath,
                   detectedFormat,
                   parseErrors.Count);
               }
               catch (OperationCanceledException)
               {
                 _logger.Information("Distribution file load cancelled.");
               }
               catch (Exception ex)
               {
                 _logger.Error(ex, "Failed to load distribution file: {FilePath}", filePath);
               }

               return (entries, detectedFormat, parseErrors);
             },
             cancellationToken);
  }

  private (DistributionEntry? Entry, string? Reason) ParseDistributionLine(
    string line,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyDictionary<string, FormKey> outfitByEditorId)
  {
    try
    {
      if (!SkyPatcherSyntax.HasFilter(line, "outfitDefault"))
      {
        return (null, null);
      }

      var npcStrings             = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");
      var excludedNpcStrings     = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcsExcluded");
      var factionStrings         = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByFactions");
      var keywordStrings         = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByKeywords");
      var excludedKeywordStrings = SkyPatcherSyntax.ExtractFilterValues(line, "filterByKeywordsExcluded");
      var raceStrings            = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByRaces");
      var classStrings           = SkyPatcherSyntax.ExtractFilterValues(line, "filterByClass");
      var genderFilter           = SkyPatcherSyntax.ParseGenderFilter(line);

      var npcFilters     = ResolveNpcIdentifiersToFilters(npcStrings, excludedNpcStrings, linkCache);
      var factionFilters = ResolveFactionIdentifiers(factionStrings, linkCache);
      var keywordFilters = ResolveKeywordIdentifiersToFilters(keywordStrings, excludedKeywordStrings, linkCache);
      var raceFilters    = ResolveRaceIdentifiers(raceStrings, linkCache);
      var classFormKeys  = ResolveClassIdentifiers(classStrings, linkCache);

      var hasAnyParsedFilter = npcFilters.Count > 0 ||
                               factionFilters.Count > 0 ||
                               keywordFilters.Count > 0 ||
                               raceFilters.Count > 0 ||
                               classFormKeys.Count > 0 ||
                               genderFilter.HasValue;

      var hasAnyFilterInLine =
        SkyPatcherSyntax.HasFilter(line, "filterByNpcs") ||
        SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions") ||
        SkyPatcherSyntax.HasAnyVariant(line, "filterByKeywords") ||
        SkyPatcherSyntax.HasAnyVariant(line, "filterByRaces") ||
        SkyPatcherSyntax.HasFilter(line, "filterByClass") ||
        SkyPatcherSyntax.HasFilter(line, "filterByGender") ||
        SkyPatcherSyntax.HasFilter(line, "filterByEditorIdContains") ||
        SkyPatcherSyntax.HasFilter(line, "filterByEditorIdContainsOr") ||
        SkyPatcherSyntax.HasFilter(line, "filterByModNames") ||
        SkyPatcherSyntax.HasFilter(line, "filterByDefaultOutfits");

      if (hasAnyFilterInLine && !hasAnyParsedFilter)
      {
        _logger.Debug("Line has filters but none could be resolved - preserving unchanged: {Line}", line);
        return (null, "SkyPatcher filter distribution (preserved)");
      }

      var outfitString = SkyPatcherSyntax.ExtractFilterValue(line, "outfitDefault");
      if (string.IsNullOrWhiteSpace(outfitString))
      {
        return (null, null);
      }

      var outfitFormKey = FormKeyHelper.ResolveOutfit(outfitString, outfitByEditorId);

      if (!outfitFormKey.HasValue)
      {
        _logger.Debug("Could not resolve outfit identifier: {Identifier}", outfitString);
        return (null, "SkyPatcher distribution (preserved)");
      }

      if (!linkCache.TryResolve<IOutfitGetter>(outfitFormKey.Value, out var outfit))
      {
        _logger.Debug("Could not resolve outfit FormKey: {FormKey}", outfitFormKey.Value);
        return (null, "SkyPatcher distribution (preserved)");
      }

      return (
               new DistributionEntry
               {
                 Outfit         = outfit,
                 NpcFilters     = npcFilters,
                 FactionFilters = factionFilters,
                 KeywordFilters = keywordFilters,
                 RaceFilters    = raceFilters,
                 ClassFormKeys  = classFormKeys,
                 TraitFilters   = new SpidTraitFilters { IsFemale = genderFilter }
               }, null);
    }
    catch (Exception ex)
    {
      _logger.Debug(ex, "Failed to parse distribution line: {Line}", line);
      return (null, "SkyPatcher distribution (preserved)");
    }
  }

  private List<FormKeyFilter> ResolveNpcIdentifiersToFilters(
    List<string> includedIdentifiers,
    List<string> excludedIdentifiers,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var results = new List<FormKeyFilter>();

    foreach (var id in includedIdentifiers)
    {
      if (TryResolveNpcIdentifier(id, linkCache) is { } formKey)
      {
        results.Add(new FormKeyFilter(formKey));
      }
    }

    foreach (var id in excludedIdentifiers)
    {
      if (TryResolveNpcIdentifier(id, linkCache) is { } formKey)
      {
        results.Add(new FormKeyFilter(formKey, true));
      }
    }

    return results;
  }

  private FormKey? TryResolveNpcIdentifier(string id, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    if (TryParseFormKey(id) is { } formKey)
    {
      return formKey;
    }

    var npc = linkCache.PriorityOrder.WinningOverrides<INpcGetter>()
                       .FirstOrDefault(n => string.Equals(n.EditorID, id, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(n.Name?.String, id, StringComparison.OrdinalIgnoreCase));
    if (npc != null)
    {
      _logger.Debug("Resolved NPC EditorID/Name '{Id}' to {FormKey}", id, npc.FormKey);
      return npc.FormKey;
    }

    _logger.Warning("Could not resolve NPC identifier: {Id}", id);
    return null;
  }

  private List<FormKeyFilter> ResolveFactionIdentifiers(
    List<string> identifiers,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var results = new List<FormKeyFilter>();
    foreach (var id in identifiers)
    {
      if (TryParseFormKey(id) is { } formKey)
      {
        results.Add(new FormKeyFilter(formKey));
        continue;
      }

      var faction = linkCache.PriorityOrder.WinningOverrides<IFactionGetter>()
                             .FirstOrDefault(f => string.Equals(f.EditorID, id, StringComparison.OrdinalIgnoreCase));
      if (faction != null)
      {
        results.Add(new FormKeyFilter(faction.FormKey));
        _logger.Debug("Resolved Faction EditorID '{Id}' to {FormKey}", id, faction.FormKey);
      }
      else
      {
        _logger.Warning("Could not resolve Faction identifier: {Id}", id);
      }
    }

    return results;
  }

  private static List<KeywordFilter> ResolveKeywordIdentifiersToFilters(
    List<string> includedIdentifiers,
    List<string> excludedIdentifiers,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var results = new List<KeywordFilter>();

    foreach (var id in includedIdentifiers)
    {
      results.Add(ResolveKeywordIdentifier(id, false, linkCache));
    }

    foreach (var id in excludedIdentifiers)
    {
      results.Add(ResolveKeywordIdentifier(id, true, linkCache));
    }

    return results;
  }

  private static KeywordFilter ResolveKeywordIdentifier(
    string id,
    bool isExcluded,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    if (TryParseFormKey(id) is { } formKey)
    {
      if (linkCache.TryResolve<IKeywordGetter>(formKey, out var keyword) &&
          !string.IsNullOrWhiteSpace(keyword.EditorID))
      {
        return new KeywordFilter(keyword.EditorID, isExcluded);
      }
    }

    var resolvedKeyword = linkCache.PriorityOrder.WinningOverrides<IKeywordGetter>()
                                   .FirstOrDefault(k => string.Equals(
                                                     k.EditorID,
                                                     id,
                                                     StringComparison.OrdinalIgnoreCase));
    return resolvedKeyword != null
             ? new KeywordFilter(resolvedKeyword.EditorID ?? id, isExcluded)
             : new KeywordFilter(id, isExcluded);
  }

  private List<FormKeyFilter> ResolveRaceIdentifiers(
    List<string> identifiers,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var results = new List<FormKeyFilter>();
    foreach (var id in identifiers)
    {
      if (TryParseFormKey(id) is { } formKey)
      {
        results.Add(new FormKeyFilter(formKey));
        continue;
      }

      var race = linkCache.PriorityOrder.WinningOverrides<IRaceGetter>()
                          .FirstOrDefault(r => string.Equals(r.EditorID, id, StringComparison.OrdinalIgnoreCase));
      if (race != null)
      {
        results.Add(new FormKeyFilter(race.FormKey));
        _logger.Debug("Resolved Race EditorID '{Id}' to {FormKey}", id, race.FormKey);
      }
      else
      {
        _logger.Warning("Could not resolve Race identifier: {Id}", id);
      }
    }

    return results;
  }

  private List<FormKey> ResolveClassIdentifiers(
    List<string> identifiers,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var results = new List<FormKey>();
    foreach (var id in identifiers)
    {
      if (TryParseFormKey(id) is { } formKey)
      {
        results.Add(formKey);
        continue;
      }

      var cls = linkCache.PriorityOrder.WinningOverrides<IClassGetter>()
                         .FirstOrDefault(c => string.Equals(c.EditorID, id, StringComparison.OrdinalIgnoreCase));
      if (cls != null)
      {
        results.Add(cls.FormKey);
        _logger.Debug("Resolved Class EditorID '{Id}' to {FormKey}", id, cls.FormKey);
      }
      else
      {
        _logger.Warning("Could not resolve Class identifier: {Id}", id);
      }
    }

    return results;
  }

  private static FormKey? TryParseFormKey(string text) =>
    FormKeyHelper.TryParse(text, out var formKey) ? formKey : null;

  private static HashSet<string> ExtractVirtualKeywordsFromLines(string[] lines)
  {
    var virtualKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var line in lines)
    {
      var trimmed = line.Trim();
      if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
      {
        continue;
      }

      if (SpidLineParser.TryParse(trimmed, out var spidFilter) &&
          spidFilter?.FormType == SpidFormType.Keyword &&
          !string.IsNullOrWhiteSpace(spidFilter.FormIdentifier))
      {
        virtualKeywords.Add(spidFilter.FormIdentifier);
      }
    }

    return virtualKeywords;
  }
}
