using Boutique.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Utilities;

/// <summary>Resolves editor-supported SkyPatcher rules for both editing and matching.</summary>
public sealed class SkyPatcherEntryParser(ILogger logger)
{
  private readonly ILogger _logger = logger;
  public (DistributionEntry? Entry, string? Reason) Parse(
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

      var clauses = line.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
      if (clauses.Any(c => !c.Contains('=') ||
            !SkyPatcherSyntax.GetAllFilterNames(c).Contains(c.Split('=')[0], StringComparer.OrdinalIgnoreCase)) ||
          new[] { "filterByNpcs", "filterByFactions", "filterByKeywords" }.Any(
            name => SkyPatcherSyntax.HasFilter(line, name) && SkyPatcherSyntax.HasFilter(line, name + "Or")) ||
          SkyPatcherSyntax.HasNonRoundtrippableFilters(line))
      {
        _logger.Debug(
          "Line has filters that cannot be round-tripped through the UI - preserving unchanged: {Line}",
          line);
        return (null, "SkyPatcher filter distribution (preserved)");
      }

      var npcStrings = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByNpcs");
      var excludedNpcStrings = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcsExcluded");
      var factionStrings = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByFactions");
      var excludedFactionStrings = SkyPatcherSyntax.ExtractFilterValues(line, "filterByFactionsExcluded");
      var excludedClassStrings = SkyPatcherSyntax.ExtractFilterValues(line, "filterByClassExclude");
      var keywordStrings = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByKeywords");
      var excludedKeywordStrings = SkyPatcherSyntax.ExtractFilterValues(line, "filterByKeywordsExcluded");
      var raceStrings = SkyPatcherSyntax.ExtractFilterValues(line, "filterByRaces");
      var classStrings = SkyPatcherSyntax.ExtractFilterValues(line, "filterByClass");
      var outfitFilterStrings = SkyPatcherSyntax.ExtractFilterValues(line, "filterByDefaultOutfits");
      var genderFilter = SkyPatcherSyntax.ParseGenderFilter(line);
      var uniqueFilter = SkyPatcherSyntax.ParseUniqueFilter(line);

      var npcFilters = ResolveNpcIdentifiersToFilters(npcStrings, excludedNpcStrings, linkCache);
      var factionFilters = ResolveFactionIdentifiers(factionStrings, linkCache);
      var keywordFilters = ResolveKeywordIdentifiersToFilters(keywordStrings, excludedKeywordStrings, linkCache);
      var raceFilters = ResolveRaceIdentifiers(raceStrings, linkCache);
      var classFormKeys = ResolveClassIdentifiers(classStrings, linkCache);
      var outfitFilterFormKeys = ResolveOutfitFilterIdentifiers(outfitFilterStrings, linkCache, outfitByEditorId);

      var excludedFactions = ResolveFactionIdentifiers(excludedFactionStrings, linkCache);
      var excludedClasses = ResolveClassIdentifiers(excludedClassStrings, linkCache);

      // A partially resolved rule is not a broader rule. Preserve it instead.
      if (npcFilters.Count != npcStrings.Count + excludedNpcStrings.Count ||
          factionFilters.Count != factionStrings.Count ||
          excludedFactions.Count != excludedFactionStrings.Count ||
          raceFilters.Count != raceStrings.Count ||
          classFormKeys.Count != classStrings.Count ||
          excludedClasses.Count != excludedClassStrings.Count ||
          outfitFilterFormKeys.Count != outfitFilterStrings.Count ||
          (SkyPatcherSyntax.HasFilter(line, "filterByGender") && !genderFilter.HasValue) ||
          SkyPatcherSyntax.ExtractFilterValues(line, "restrictToFlags").Any(v => !v.Equals("unique", StringComparison.OrdinalIgnoreCase)))
      {
        return (null, "Unresolved SkyPatcher filters (preserved)");
      }

      factionFilters.AddRange(excludedFactions.Select(f => new FormKeyFilter(f.FormKey, true)));
      var hasAnyParsedFilter = excludedClasses.Count > 0 || npcFilters.Count > 0 ||
                               factionFilters.Count > 0 ||
                               keywordFilters.Count > 0 ||
                               raceFilters.Count > 0 ||
                               classFormKeys.Count > 0 ||
                               outfitFilterFormKeys.Count > 0 ||
                               genderFilter.HasValue ||
                               uniqueFilter.HasValue;

      var hasAnyFilterInLine =
        SkyPatcherSyntax.HasAnyVariant(line, "filterByNpcs") ||
        SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions") ||
        SkyPatcherSyntax.HasAnyVariant(line, "filterByKeywords") ||
        SkyPatcherSyntax.HasFilter(line, "filterByRaces") ||
        SkyPatcherSyntax.HasFilter(line, "filterByClass") ||
        SkyPatcherSyntax.HasFilter(line, "filterByDefaultOutfits") ||
        SkyPatcherSyntax.HasFilter(line, "restrictToFlags") ||
        SkyPatcherSyntax.HasFilter(line, "filterByGender");

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

      var npcLogicMode = npcStrings.Count > 1 ? FilterLogicMode.Or : FilterLogicMode.And;
      var factionLogicMode = SkyPatcherSyntax.HasFilter(line, "filterByFactionsOr") ? FilterLogicMode.Or : FilterLogicMode.And;
      var keywordLogicMode = SkyPatcherSyntax.HasFilter(line, "filterByKeywordsOr") ? FilterLogicMode.Or : FilterLogicMode.And;

      return (
               new DistributionEntry
               {
                 Outfit = outfit,
                 NpcFilters = npcFilters,
                 FactionFilters = factionFilters,
                 KeywordFilters = keywordFilters,
                 RaceFilters = raceFilters,
                 RaceLogicMode = FilterLogicMode.Or,
                 ClassFormKeys = classFormKeys,
                 ExcludedClassFormKeys = excludedClasses,
                 OutfitFilterFormKeys = outfitFilterFormKeys,
                 TraitFilters = new SpidTraitFilters { IsFemale = genderFilter, IsUnique = uniqueFilter },
                 NpcLogicMode = npcLogicMode,
                 FactionLogicMode = factionLogicMode,
                 KeywordLogicMode = keywordLogicMode
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
                                            || string.Equals(n.Name.SafeString(n), id, StringComparison.OrdinalIgnoreCase));
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
    if (TryParseFormKey(id) is { } formKey &&
        linkCache.TryResolve<IKeywordGetter>(formKey, out var keyword) &&
        !string.IsNullOrWhiteSpace(keyword.EditorID))
    {
      return new KeywordFilter(keyword.EditorID, isExcluded);
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

      var classRecord = linkCache.PriorityOrder.WinningOverrides<IClassGetter>()
                                 .FirstOrDefault(c => string.Equals(c.EditorID, id, StringComparison.OrdinalIgnoreCase));
      if (classRecord != null)
      {
        results.Add(classRecord.FormKey);
        _logger.Debug("Resolved Class EditorID '{Id}' to {FormKey}", id, classRecord.FormKey);
      }
      else
      {
        _logger.Warning("Could not resolve Class identifier: {Id}", id);
      }
    }

    return results;
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

  private List<FormKey> ResolveOutfitFilterIdentifiers(
    List<string> identifiers,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyDictionary<string, FormKey> outfitByEditorId)
  {
    var results = new List<FormKey>();
    foreach (var id in identifiers)
    {
      var formKey = FormKeyHelper.ResolveOutfit(id, outfitByEditorId)
                    ?? FormKeyHelper.ResolveOutfit(id, linkCache);
      if (formKey.HasValue)
      {
        results.Add(formKey.Value);
      }
      else
      {
        _logger.Warning("Could not resolve outfit filter identifier: {Id}", id);
      }
    }

    return results;
  }

  private static FormKey? TryParseFormKey(string text) =>
    FormKeyHelper.TryParse(text, out var formKey) ? formKey : null;

}
