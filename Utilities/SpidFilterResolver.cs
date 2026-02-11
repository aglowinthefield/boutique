using System.Globalization;
using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Utilities;

public sealed class FormIdLookupCache
{
  public FormIdLookupCache(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    NpcsByFormId = linkCache.WinningOverrides<INpcGetter>()
                            .GroupBy(r => r.FormKey.ID)
                            .ToDictionary(g => g.Key, g => g.First().FormKey);

    FactionsByFormId = linkCache.WinningOverrides<IFactionGetter>()
                                .GroupBy(r => r.FormKey.ID)
                                .ToDictionary(g => g.Key, g => g.First().FormKey);

    RacesByFormId = linkCache.WinningOverrides<IRaceGetter>()
                             .GroupBy(r => r.FormKey.ID)
                             .ToDictionary(g => g.Key, g => g.First().FormKey);

    LocationsByFormId = linkCache.WinningOverrides<ILocationGetter>()
                                 .GroupBy(r => r.FormKey.ID)
                                 .ToDictionary(g => g.Key, g => g.First().FormKey);

    OutfitsByFormId = linkCache.WinningOverrides<IOutfitGetter>()
                               .GroupBy(r => r.FormKey.ID)
                               .ToDictionary(g => g.Key, g => g.First().FormKey);
  }

  public IReadOnlyDictionary<uint, FormKey> NpcsByFormId { get; }
  public IReadOnlyDictionary<uint, FormKey> FactionsByFormId { get; }
  public IReadOnlyDictionary<uint, FormKey> RacesByFormId { get; }
  public IReadOnlyDictionary<uint, FormKey> LocationsByFormId { get; }
  public IReadOnlyDictionary<uint, FormKey> OutfitsByFormId { get; }
}

public static class SpidFilterResolver
{
  public static DistributionEntry? Resolve(
    SpidDistributionFilter filter,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<INpcGetter> cachedNpcs,
    IReadOnlyList<IOutfitGetter> cachedOutfits,
    IReadOnlySet<string>? knownVirtualKeywords,
    FormIdLookupCache? formIdCache,
    ILogger? logger = null)
  {
    try
    {
      var outfit = ResolveOutfit(filter.OutfitIdentifier, linkCache, cachedOutfits, logger);
      if (outfit == null)
      {
        logger?.Debug("Could not resolve outfit from identifier: {Identifier}", filter.OutfitIdentifier);
        return null;
      }

      var npcFilters           = new List<FormKeyFilter>();
      var keywordFilters       = new List<KeywordFilter>();
      var factionFilters       = new List<FormKeyFilter>();
      var raceFilters          = new List<FormKeyFilter>();
      var classFormKeys        = new List<FormKey>();
      var combatStyleFormKeys  = new List<FormKey>();
      var outfitFilterFormKeys = new List<FormKey>();
      var perkFormKeys         = new List<FormKey>();
      var voiceTypeFormKeys    = new List<FormKey>();
      var locationFormKeys     = new List<FormKey>();
      var formListFormKeys     = new List<FormKey>();

      ProcessStringFilters(
        filter.StringFilters,
        linkCache,
        cachedNpcs,
        npcFilters,
        keywordFilters,
        knownVirtualKeywords,
        logger);

      var resolvedFormEditorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      ProcessFormFilters(
        filter.FormFilters,
        linkCache,
        formIdCache,
        npcFilters,
        factionFilters,
        raceFilters,
        classFormKeys,
        combatStyleFormKeys,
        outfitFilterFormKeys,
        perkFormKeys,
        voiceTypeFormKeys,
        locationFormKeys,
        formListFormKeys,
        resolvedFormEditorIds,
        logger);

      var rawStringFilters =
        ExtractUnresolvableStringFilters(filter.StringFilters, npcFilters, keywordFilters, cachedNpcs);
      var rawFormFilters = ExtractUnresolvableFormFilters(filter.FormFilters, resolvedFormEditorIds);

      var hasAnyFilter = npcFilters.Count > 0 || factionFilters.Count > 0 ||
                         keywordFilters.Count > 0 || raceFilters.Count > 0 ||
                         classFormKeys.Count > 0 || combatStyleFormKeys.Count > 0 ||
                         outfitFilterFormKeys.Count > 0 || perkFormKeys.Count > 0 ||
                         voiceTypeFormKeys.Count > 0 ||
                         locationFormKeys.Count > 0 || formListFormKeys.Count > 0 ||
                         !string.IsNullOrEmpty(rawStringFilters) || !string.IsNullOrEmpty(rawFormFilters);

      if (!hasAnyFilter && !filter.TargetsAllNpcs)
      {
        logger?.Debug("No filters could be resolved for SPID line: {Line}", filter.RawLine);
        return null;
      }

      var entry = new DistributionEntry
                  {
                    Outfit               = outfit,
                    NpcFilters           = npcFilters,
                    KeywordFilters       = keywordFilters,
                    FactionFilters       = factionFilters,
                    RaceFilters          = raceFilters,
                    ClassFormKeys        = classFormKeys,
                    CombatStyleFormKeys  = combatStyleFormKeys,
                    OutfitFilterFormKeys = outfitFilterFormKeys,
                    PerkFormKeys         = perkFormKeys,
                    VoiceTypeFormKeys    = voiceTypeFormKeys,
                    LocationFormKeys     = locationFormKeys,
                    FormListFormKeys     = formListFormKeys,
                    TraitFilters         = filter.TraitFilters,
                    LevelFilters         = filter.LevelFilters,
                    RawStringFilters     = rawStringFilters,
                    RawFormFilters       = rawFormFilters,
                    NpcLogicMode         = DetectLogicMode(filter.StringFilters, npcFilters.Count),
                    KeywordLogicMode     = DetectLogicMode(filter.StringFilters, keywordFilters.Count),
                    FactionLogicMode     = DetectLogicMode(filter.FormFilters, factionFilters.Count),
                    RaceLogicMode        = DetectLogicMode(filter.FormFilters, raceFilters.Count),
                    ClassLogicMode       = DetectLogicMode(filter.FormFilters, classFormKeys.Count),
                    LocationLogicMode    = DetectLogicMode(filter.FormFilters, locationFormKeys.Count),
                    OutfitFilterLogicMode = DetectLogicMode(filter.FormFilters, outfitFilterFormKeys.Count)
                  };

      if (outfitFilterFormKeys.Count > 0)
      {
        logger?.Information(
          "Resolved {Count} outfit filter(s) for line: {Line} => {FormKeys}",
          outfitFilterFormKeys.Count,
          filter.RawLine,
          string.Join(", ", outfitFilterFormKeys));
      }

      if (filter.Chance != 100)
      {
        entry.Chance = filter.Chance;
      }

      return entry;
    }
    catch (Exception ex)
    {
      logger?.Debug(ex, "Failed to resolve SPID filter: {Line}", filter.RawLine);
      return null;
    }
  }

  public static DistributionEntry? ResolveKeyword(
    SpidDistributionFilter filter,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<INpcGetter> cachedNpcs,
    IReadOnlySet<string>? knownVirtualKeywords,
    FormIdLookupCache? formIdCache,
    ILogger? logger = null)
  {
    try
    {
      var keywordToDistribute = filter.FormIdentifier;
      if (string.IsNullOrWhiteSpace(keywordToDistribute))
      {
        logger?.Debug("Empty keyword identifier in Keyword distribution line: {Line}", filter.RawLine);
        return null;
      }

      var npcFilters           = new List<FormKeyFilter>();
      var keywordFilters       = new List<KeywordFilter>();
      var factionFilters       = new List<FormKeyFilter>();
      var raceFilters          = new List<FormKeyFilter>();
      var classFormKeys        = new List<FormKey>();
      var combatStyleFormKeys  = new List<FormKey>();
      var outfitFilterFormKeys = new List<FormKey>();
      var perkFormKeys         = new List<FormKey>();
      var voiceTypeFormKeys    = new List<FormKey>();
      var locationFormKeys     = new List<FormKey>();
      var formListFormKeys     = new List<FormKey>();

      ProcessStringFilters(
        filter.StringFilters,
        linkCache,
        cachedNpcs,
        npcFilters,
        keywordFilters,
        knownVirtualKeywords,
        logger);

      var resolvedFormEditorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      ProcessFormFilters(
        filter.FormFilters,
        linkCache,
        formIdCache,
        npcFilters,
        factionFilters,
        raceFilters,
        classFormKeys,
        combatStyleFormKeys,
        outfitFilterFormKeys,
        perkFormKeys,
        voiceTypeFormKeys,
        locationFormKeys,
        formListFormKeys,
        resolvedFormEditorIds,
        logger);

      var rawStringFilters =
        ExtractUnresolvableStringFilters(filter.StringFilters, npcFilters, keywordFilters, cachedNpcs);
      var rawFormFilters = ExtractUnresolvableFormFilters(filter.FormFilters, resolvedFormEditorIds);

      var entry = new DistributionEntry
                  {
                    Type                 = DistributionType.Keyword,
                    KeywordToDistribute  = keywordToDistribute,
                    NpcFilters           = npcFilters,
                    KeywordFilters       = keywordFilters,
                    FactionFilters       = factionFilters,
                    RaceFilters          = raceFilters,
                    ClassFormKeys        = classFormKeys,
                    CombatStyleFormKeys  = combatStyleFormKeys,
                    OutfitFilterFormKeys = outfitFilterFormKeys,
                    PerkFormKeys         = perkFormKeys,
                    VoiceTypeFormKeys    = voiceTypeFormKeys,
                    LocationFormKeys     = locationFormKeys,
                    FormListFormKeys     = formListFormKeys,
                    TraitFilters         = filter.TraitFilters,
                    LevelFilters         = filter.LevelFilters,
                    RawStringFilters     = rawStringFilters,
                    RawFormFilters       = rawFormFilters,
                    NpcLogicMode         = DetectLogicMode(filter.StringFilters, npcFilters.Count),
                    KeywordLogicMode     = DetectLogicMode(filter.StringFilters, keywordFilters.Count),
                    FactionLogicMode     = DetectLogicMode(filter.FormFilters, factionFilters.Count),
                    RaceLogicMode        = DetectLogicMode(filter.FormFilters, raceFilters.Count),
                    ClassLogicMode       = DetectLogicMode(filter.FormFilters, classFormKeys.Count),
                    LocationLogicMode    = DetectLogicMode(filter.FormFilters, locationFormKeys.Count),
                    OutfitFilterLogicMode = DetectLogicMode(filter.FormFilters, outfitFilterFormKeys.Count)
                  };

      if (filter.Chance != 100)
      {
        entry.Chance = filter.Chance;
      }

      return entry;
    }
    catch (Exception ex)
    {
      logger?.Debug(ex, "Failed to resolve keyword SPID filter: {Line}", filter.RawLine);
      return null;
    }
  }

  public static IOutfitGetter? ResolveOutfit(
    string outfitIdentifier,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<IOutfitGetter> cachedOutfits,
    ILogger? logger = null)
  {
    var tildeIndex = outfitIdentifier.IndexOf('~');
    if (tildeIndex >= 0)
    {
      var formIdString = outfitIdentifier[..tildeIndex].Trim();
      var modKeyString = outfitIdentifier[(tildeIndex + 1)..].Trim();

      formIdString = FormKeyHelper.StripHexPrefix(formIdString);
      if (uint.TryParse(formIdString, NumberStyles.HexNumber, null, out var formId) &&
          ModKey.TryFromNameAndExtension(modKeyString, out var modKey))
      {
        var outfitFormKey = new FormKey(modKey, formId);
        if (linkCache.TryResolve<IOutfitGetter>(outfitFormKey, out var outfit))
        {
          return outfit;
        }
      }

      logger?.Debug("Failed to resolve tilde-format outfit: {Identifier}", outfitIdentifier);
      return null;
    }

    if (outfitIdentifier.Contains('|'))
    {
      var pipeIndex    = outfitIdentifier.IndexOf('|');
      var modKeyString = outfitIdentifier[..pipeIndex].Trim();
      var formIdString = outfitIdentifier[(pipeIndex + 1)..].Trim();

      formIdString = FormKeyHelper.StripHexPrefix(formIdString);
      if (uint.TryParse(formIdString, NumberStyles.HexNumber, null, out var formId) &&
          ModKey.TryFromNameAndExtension(modKeyString, out var modKey))
      {
        var outfitFormKey = new FormKey(modKey, formId);
        if (linkCache.TryResolve<IOutfitGetter>(outfitFormKey, out var outfit))
        {
          return outfit;
        }
      }

      logger?.Debug("Failed to resolve pipe-format outfit: {Identifier}", outfitIdentifier);
      return null;
    }

    var resolvedOutfit = cachedOutfits.FirstOrDefault(o =>
                                                        string.Equals(
                                                          o.EditorID,
                                                          outfitIdentifier,
                                                          StringComparison.OrdinalIgnoreCase));

    if (resolvedOutfit == null)
    {
      logger?.Debug("Failed to resolve EditorID outfit: {Identifier}", outfitIdentifier);
    }

    return resolvedOutfit;
  }

  private static void ProcessStringFilters(
    SpidFilterSection stringFilters,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<INpcGetter> cachedNpcs,
    List<FormKeyFilter> npcFilters,
    List<KeywordFilter> keywordFilters,
    IReadOnlySet<string>? knownVirtualKeywords,
    ILogger? logger)
  {
    foreach (var expr in stringFilters.Expressions)
    {
      foreach (var part in expr.Parts)
      {
        if (part.HasWildcard)
        {
          continue;
        }

        var npc = cachedNpcs.FirstOrDefault(n =>
                                              string.Equals(
                                                n.EditorID,
                                                part.Value,
                                                StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(
                                                n.Name?.String,
                                                part.Value,
                                                StringComparison.OrdinalIgnoreCase));
        if (npc != null)
        {
          npcFilters.Add(new FormKeyFilter(npc.FormKey, part.IsNegated));
          logger?.Debug("Resolved NPC string filter '{Value}' to {FormKey}", part.Value, npc.FormKey);
          continue;
        }

        var keyword = linkCache.WinningOverrides<IKeywordGetter>()
                               .FirstOrDefault(k => string.Equals(
                                                 k.EditorID,
                                                 part.Value,
                                                 StringComparison.OrdinalIgnoreCase));
        if (keyword != null)
        {
          keywordFilters.Add(new KeywordFilter(keyword.EditorID ?? part.Value, part.IsNegated));
          continue;
        }

        if (knownVirtualKeywords != null && knownVirtualKeywords.Contains(part.Value))
        {
          keywordFilters.Add(new KeywordFilter(part.Value, part.IsNegated));
          continue;
        }

        logger?.Verbose("Unresolved string filter (not NPC or keyword): {Value}", part.Value);
      }
    }

    foreach (var exclusion in stringFilters.GlobalExclusions)
    {
      if (exclusion.HasWildcard)
      {
        continue;
      }

      var npc = cachedNpcs.FirstOrDefault(n =>
                                            string.Equals(
                                              n.EditorID,
                                              exclusion.Value,
                                              StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(
                                              n.Name?.String,
                                              exclusion.Value,
                                              StringComparison.OrdinalIgnoreCase));
      if (npc != null)
      {
        npcFilters.Add(new FormKeyFilter(npc.FormKey, true));
        logger?.Debug("Resolved excluded NPC string filter '{Value}' to {FormKey}", exclusion.Value, npc.FormKey);
        continue;
      }

      var keyword = linkCache.WinningOverrides<IKeywordGetter>()
                             .FirstOrDefault(k => string.Equals(
                                               k.EditorID,
                                               exclusion.Value,
                                               StringComparison.OrdinalIgnoreCase));
      if (keyword != null)
      {
        keywordFilters.Add(new KeywordFilter(keyword.EditorID ?? exclusion.Value, true));
        continue;
      }

      if (knownVirtualKeywords != null && knownVirtualKeywords.Contains(exclusion.Value))
      {
        keywordFilters.Add(new KeywordFilter(exclusion.Value, true));
        continue;
      }

      logger?.Verbose("Unresolved global exclusion (not NPC or keyword): {Value}", exclusion.Value);
    }
  }

  private static void ProcessFormFilters(
    SpidFilterSection formFilters,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    FormIdLookupCache? formIdCache,
    List<FormKeyFilter> npcFilters,
    List<FormKeyFilter> factionFilters,
    List<FormKeyFilter> raceFilters,
    List<FormKey> classFormKeys,
    List<FormKey> combatStyleFormKeys,
    List<FormKey> outfitFilterFormKeys,
    List<FormKey> perkFormKeys,
    List<FormKey> voiceTypeFormKeys,
    List<FormKey> locationFormKeys,
    List<FormKey> formListFormKeys,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger)
  {
    if (formFilters.Expressions.Count > 0)
    {
      logger?.Information("Processing {Count} form filter expression(s)", formFilters.Expressions.Count);
    }

    foreach (var expr in formFilters.Expressions)
    {
      foreach (var part in expr.Parts)
      {
        logger?.Debug("Processing form filter part: {Value}", part.Value);
        if (TryResolveFormFilterByFormKey(
          part,
          linkCache,
          formIdCache,
          npcFilters,
          factionFilters,
          raceFilters,
          locationFormKeys,
          outfitFilterFormKeys,
          resolvedEditorIds,
          logger))
        {
          continue;
        }

        if (TryResolveAndAddFilter<IFactionGetter>(
          part.Value,
          part.IsNegated,
          linkCache,
          factionFilters,
          resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAddFilter<IRaceGetter>(part.Value, part.IsNegated, linkCache, raceFilters, resolvedEditorIds))
        {
          continue;
        }

        if (part.IsNegated)
        {
          continue;
        }

        if (TryResolveAndAdd<IClassGetter>(part.Value, linkCache, classFormKeys, resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<ICombatStyleGetter>(part.Value, linkCache, combatStyleFormKeys, resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<IOutfitGetter>(part.Value, linkCache, outfitFilterFormKeys, resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<IPerkGetter>(part.Value, linkCache, perkFormKeys, resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<IVoiceTypeGetter>(part.Value, linkCache, voiceTypeFormKeys, resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<ILocationGetter>(part.Value, linkCache, locationFormKeys, resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<IFormListGetter>(part.Value, linkCache, formListFormKeys, resolvedEditorIds))
        {
          continue;
        }

        logger?.Debug("Could not resolve form filter: {Value}", part.Value);
      }
    }

    foreach (var exclusion in formFilters.GlobalExclusions)
    {
      var exclusionPart = new SpidFilterPart { Value = exclusion.Value, IsNegated = true };
      if (TryResolveFormFilterByFormKey(
        exclusionPart,
        linkCache,
        formIdCache,
        npcFilters,
        factionFilters,
        raceFilters,
        locationFormKeys,
        outfitFilterFormKeys,
        resolvedEditorIds,
        logger))
      {
        continue;
      }

      if (TryResolveAndAddFilter<IFactionGetter>(exclusion.Value, true, linkCache, factionFilters, resolvedEditorIds))
      {
        continue;
      }

      TryResolveAndAddFilter<IRaceGetter>(exclusion.Value, true, linkCache, raceFilters, resolvedEditorIds);
    }
  }

  private static bool TryResolveFormFilterByFormKey(
    SpidFilterPart part,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    FormIdLookupCache? formIdCache,
    List<FormKeyFilter> npcFilters,
    List<FormKeyFilter> factionFilters,
    List<FormKeyFilter> raceFilters,
    List<FormKey> locationFormKeys,
    List<FormKey> outfitFilterFormKeys,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger)
  {
    if (TryParseAsFormKey(part.Value, out var formKey))
    {
      logger?.Information("Parsed {Value} as explicit FormKey: {FormKey}", part.Value, formKey);
      return TryResolveByFormKey(
        formKey,
        part.IsNegated,
        part.Value,
        linkCache,
        npcFilters,
        factionFilters,
        raceFilters,
        locationFormKeys,
        outfitFilterFormKeys,
        resolvedEditorIds,
        logger);
    }

    if (FormKeyHelper.TryParseFormId(part.Value, out var formId))
    {
      logger?.Information("Parsed {Value} as bare FormID: 0x{FormId:X}", part.Value, formId);
      return TryResolveBareFormId(
        formId,
        part.IsNegated,
        part.Value,
        linkCache,
        formIdCache,
        npcFilters,
        factionFilters,
        raceFilters,
        locationFormKeys,
        outfitFilterFormKeys,
        resolvedEditorIds,
        logger);
    }

    return false;
  }

  private static bool TryResolveByFormKey(
    FormKey formKey,
    bool isNegated,
    string originalValue,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<FormKeyFilter> npcFilters,
    List<FormKeyFilter> factionFilters,
    List<FormKeyFilter> raceFilters,
    List<FormKey> locationFormKeys,
    List<FormKey> outfitFilterFormKeys,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger) =>
    TryResolveFormKeyAs<INpcGetter>(
      formKey,
      isNegated,
      originalValue,
      linkCache,
      npcFilters,
      resolvedEditorIds,
      logger) ||
    TryResolveFormKeyAs<IFactionGetter>(
      formKey,
      isNegated,
      originalValue,
      linkCache,
      factionFilters,
      resolvedEditorIds,
      logger) ||
    TryResolveFormKeyAs<IRaceGetter>(
      formKey,
      isNegated,
      originalValue,
      linkCache,
      raceFilters,
      resolvedEditorIds,
      logger) ||
    TryResolveFormKeyAsFormKey<ILocationGetter>(
      formKey,
      originalValue,
      linkCache,
      locationFormKeys,
      resolvedEditorIds,
      logger) ||
    TryResolveFormKeyAsFormKey<IOutfitGetter>(
      formKey,
      originalValue,
      linkCache,
      outfitFilterFormKeys,
      resolvedEditorIds,
      logger);

  private static bool TryResolveBareFormId(
    uint formId,
    bool isNegated,
    string originalValue,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    FormIdLookupCache? formIdCache,
    List<FormKeyFilter> npcFilters,
    List<FormKeyFilter> factionFilters,
    List<FormKeyFilter> raceFilters,
    List<FormKey> locationFormKeys,
    List<FormKey> outfitFilterFormKeys,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger)
  {
    if (formIdCache != null)
    {
      if (formIdCache.NpcsByFormId.TryGetValue(formId, out var npcFormKey))
      {
        npcFilters.Add(new FormKeyFilter(npcFormKey, isNegated));
        resolvedEditorIds?.Add(originalValue);
        logger?.Debug("Resolved bare FormID {Value} as NPC via cache: {FormKey}", originalValue, npcFormKey);
        return true;
      }

      if (formIdCache.FactionsByFormId.TryGetValue(formId, out var factionFormKey))
      {
        factionFilters.Add(new FormKeyFilter(factionFormKey, isNegated));
        resolvedEditorIds?.Add(originalValue);
        logger?.Debug("Resolved bare FormID {Value} as Faction via cache: {FormKey}", originalValue, factionFormKey);
        return true;
      }

      if (formIdCache.RacesByFormId.TryGetValue(formId, out var raceFormKey))
      {
        raceFilters.Add(new FormKeyFilter(raceFormKey, isNegated));
        resolvedEditorIds?.Add(originalValue);
        logger?.Debug("Resolved bare FormID {Value} as Race via cache: {FormKey}", originalValue, raceFormKey);
        return true;
      }

      if (formIdCache.LocationsByFormId.TryGetValue(formId, out var locationFormKey))
      {
        locationFormKeys.Add(locationFormKey);
        resolvedEditorIds?.Add(originalValue);
        logger?.Debug("Resolved bare FormID {Value} as Location via cache: {FormKey}", originalValue, locationFormKey);
        return true;
      }

      if (formIdCache.OutfitsByFormId.TryGetValue(formId, out var outfitFormKey))
      {
        outfitFilterFormKeys.Add(outfitFormKey);
        resolvedEditorIds?.Add(originalValue);
        logger?.Debug("Resolved bare FormID {Value} as Outfit via cache: {FormKey}", originalValue, outfitFormKey);
        return true;
      }

      logger?.Warning("Could not resolve bare FormID {Value} (0x{FormId:X}) via cache", originalValue, formId);
      return false;
    }

    if (TryResolveBareFormIdAs<INpcGetter>(
          formId,
          isNegated,
          originalValue,
          linkCache,
          npcFilters,
          resolvedEditorIds,
          logger) ||
        TryResolveBareFormIdAs<IFactionGetter>(
          formId,
          isNegated,
          originalValue,
          linkCache,
          factionFilters,
          resolvedEditorIds,
          logger) ||
        TryResolveBareFormIdAs<IRaceGetter>(
          formId,
          isNegated,
          originalValue,
          linkCache,
          raceFilters,
          resolvedEditorIds,
          logger) ||
        TryResolveBareFormIdAsFormKey<ILocationGetter>(
          formId,
          originalValue,
          linkCache,
          locationFormKeys,
          resolvedEditorIds,
          logger) ||
        TryResolveBareFormIdAsFormKey<IOutfitGetter>(
          formId,
          originalValue,
          linkCache,
          outfitFilterFormKeys,
          resolvedEditorIds,
          logger))
    {
      return true;
    }

    logger?.Warning(
      "Could not resolve bare FormID {Value} (0x{FormId:X}) as NPC, Faction, Race, Location, or Outfit",
      originalValue,
      formId);
    return false;
  }

  private static bool TryResolveFormKeyAs<T>(
    FormKey formKey,
    bool isNegated,
    string originalValue,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<FormKeyFilter> targetList,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger)
    where T : class, ISkyrimMajorRecordGetter
  {
    if (!linkCache.TryResolve<T>(formKey, out var record))
    {
      return false;
    }

    targetList.Add(new FormKeyFilter(formKey, isNegated));
    resolvedEditorIds?.Add(originalValue);
    logger?.Debug("Resolved FormID {Value} as {Type}: {EditorId}", originalValue, typeof(T).Name, record.EditorID);
    return true;
  }

  private static bool TryResolveFormKeyAsFormKey<T>(
    FormKey formKey,
    string originalValue,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<FormKey> targetList,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger)
    where T : class, ISkyrimMajorRecordGetter
  {
    if (!linkCache.TryResolve<T>(formKey, out var record))
    {
      return false;
    }

    targetList.Add(formKey);
    resolvedEditorIds?.Add(originalValue);
    logger?.Debug("Resolved FormID {Value} as {Type}: {EditorId}", originalValue, typeof(T).Name, record.EditorID);
    return true;
  }

  private static bool TryResolveBareFormIdAs<T>(
    uint formId,
    bool isNegated,
    string originalValue,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<FormKeyFilter> targetList,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger)
    where T : class, ISkyrimMajorRecordGetter
  {
    var record = linkCache.WinningOverrides<T>().FirstOrDefault(r => r.FormKey.ID == formId);
    if (record == null)
    {
      return false;
    }

    targetList.Add(new FormKeyFilter(record.FormKey, isNegated));
    resolvedEditorIds?.Add(originalValue);
    logger?.Debug("Resolved bare FormID {Value} as {Type}: {EditorId}", originalValue, typeof(T).Name, record.EditorID);
    return true;
  }

  private static bool TryResolveBareFormIdAsFormKey<T>(
    uint formId,
    string originalValue,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<FormKey> targetList,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger)
    where T : class, ISkyrimMajorRecordGetter
  {
    var record = linkCache.WinningOverrides<T>().FirstOrDefault(r => r.FormKey.ID == formId);
    if (record == null)
    {
      return false;
    }

    targetList.Add(record.FormKey);
    resolvedEditorIds?.Add(originalValue);
    logger?.Debug("Resolved bare FormID {Value} as {Type}: {EditorId}", originalValue, typeof(T).Name, record.EditorID);
    return true;
  }

  private static bool TryParseAsFormKey(string value, out FormKey formKey)
  {
    formKey = FormKey.Null;

    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    return FormKey.TryFactory(value, out formKey) || FormKeyHelper.TryParse(value, out formKey);
  }

  private static T? ResolveByEditorId<T>(string editorId, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    where T : class, ISkyrimMajorRecordGetter =>
    linkCache.WinningOverrides<T>()
             .FirstOrDefault(r => string.Equals(r.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

  private static bool TryResolveAndAdd<T>(
    string editorId,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<FormKey> targetList,
    HashSet<string>? resolvedEditorIds)
    where T : class, ISkyrimMajorRecordGetter
  {
    var record = ResolveByEditorId<T>(editorId, linkCache);
    if (record == null)
    {
      return false;
    }

    targetList.Add(record.FormKey);
    resolvedEditorIds?.Add(editorId);
    return true;
  }

  private static bool TryResolveAndAddFilter<T>(
    string editorId,
    bool isNegated,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<FormKeyFilter> targetList,
    HashSet<string>? resolvedEditorIds)
    where T : class, ISkyrimMajorRecordGetter
  {
    var record = ResolveByEditorId<T>(editorId, linkCache);
    if (record == null)
    {
      return false;
    }

    targetList.Add(new FormKeyFilter(record.FormKey, isNegated));
    resolvedEditorIds?.Add(editorId);
    return true;
  }

  private static string? ExtractUnresolvableStringFilters(
    SpidFilterSection stringFilters,
    List<FormKeyFilter> resolvedNpcFilters,
    List<KeywordFilter> resolvedKeywordFilters,
    IReadOnlyList<INpcGetter> cachedNpcs)
  {
    var resolvedNpcFormKeys = new HashSet<FormKey>(resolvedNpcFilters.Select(f => f.FormKey));
    var resolvedKeywordEditorIds = new HashSet<string>(
      resolvedKeywordFilters.Select(k => k.EditorId),
      StringComparer.OrdinalIgnoreCase);

    bool IsResolved(string value) =>
      resolvedKeywordEditorIds.Contains(value) ||
      cachedNpcs.Any(n =>
                       resolvedNpcFormKeys.Contains(n.FormKey) &&
                       (string.Equals(n.EditorID, value, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(n.Name?.String, value, StringComparison.OrdinalIgnoreCase)));

    var unresolvableParts = new List<string>();

    foreach (var expr in stringFilters.Expressions)
    {
      var exprParts = new List<string>();
      foreach (var part in expr.Parts)
      {
        if (part.HasWildcard)
        {
          exprParts.Add(part.Value);
        }
        else if (!IsResolved(part.Value))
        {
          exprParts.Add(part.IsNegated ? $"-{part.Value}" : part.Value);
        }
      }

      if (exprParts.Count > 0)
      {
        unresolvableParts.Add(string.Join("+", exprParts));
      }
    }

    foreach (var exclusion in stringFilters.GlobalExclusions)
    {
      if (!IsResolved(exclusion.Value))
      {
        unresolvableParts.Add($"-{exclusion.Value}");
      }
    }

    return unresolvableParts.Count > 0 ? string.Join(",", unresolvableParts) : null;
  }

  private static string? ExtractUnresolvableFormFilters(
    SpidFilterSection formFilters,
    HashSet<string> resolvedEditorIds)
  {
    var unresolvableParts = new List<string>();

    foreach (var expr in formFilters.Expressions)
    {
      var exprParts = new List<string>();
      foreach (var part in expr.Parts)
      {
        if (resolvedEditorIds.Contains(part.Value))
        {
          continue;
        }

        var prefix = part.IsNegated ? "-" : string.Empty;
        exprParts.Add($"{prefix}{part.Value}");
      }

      if (exprParts.Count > 0)
      {
        unresolvableParts.Add(string.Join("+", exprParts));
      }
    }

    foreach (var exclusion in formFilters.GlobalExclusions)
    {
      if (!resolvedEditorIds.Contains(exclusion.Value))
      {
        unresolvableParts.Add($"-{exclusion.Value}");
      }
    }

    return unresolvableParts.Count > 0 ? string.Join(",", unresolvableParts) : null;
  }

  /// <summary>
  ///   Detects the logic mode (AND/OR) from a SPID filter section.
  ///   AND mode: All values in a single expression joined with + (e.g., "Faction1+Faction2")
  ///   OR mode: Values split across multiple expressions with , (e.g., "Faction1,Faction2")
  /// </summary>
  private static FilterLogicMode DetectLogicMode(SpidFilterSection section, int resolvedCount)
  {
    if (resolvedCount <= 1)
    {
      return FilterLogicMode.And;
    }

    var expressionsWithMultipleParts = section.Expressions.Count(e => e.Parts.Count > 1);

    if (expressionsWithMultipleParts > 0 && section.Expressions.Count == 1)
    {
      return FilterLogicMode.And;
    }

    if (section.Expressions.Count > 1)
    {
      return FilterLogicMode.Or;
    }

    return FilterLogicMode.And;
  }
}
