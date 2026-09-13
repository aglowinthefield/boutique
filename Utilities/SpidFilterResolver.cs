using System.Collections.Concurrent;
using System.Globalization;
using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Utilities;

/// <summary>
///   Per-file lookup indexes built once from the link cache: bare-FormID → FormKey across the
///   record types SPID form/string filters can reference, plus lazily-built EditorID → FormKey
///   indexes so resolving a filter value doesn't re-enumerate the whole load order per value.
/// </summary>
public sealed class FormIdLookupCache
{
  private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;
  private readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, FormKey>> _editorIdIndexes = new();

  public FormIdLookupCache(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    _linkCache = linkCache;

    var dict = new Dictionary<uint, FormKey>();
    AddOverrides<INpcGetter>(linkCache, dict);
    AddOverrides<IFactionGetter>(linkCache, dict);
    AddOverrides<IRaceGetter>(linkCache, dict);
    AddOverrides<ILocationGetter>(linkCache, dict);
    AddOverrides<IOutfitGetter>(linkCache, dict);
    FormKeysByFormId = dict;
  }

  public IReadOnlyDictionary<uint, FormKey> FormKeysByFormId { get; }

  /// <summary>
  ///   Resolves an EditorID to its winning override's FormKey using a per-type index that is
  ///   built on first use (large load orders only pay for the types actually queried).
  /// </summary>
  public bool TryGetFormKeyByEditorId<T>(string editorId, out FormKey formKey)
    where T : class, ISkyrimMajorRecordGetter
  {
    var index = _editorIdIndexes.GetOrAdd(typeof(T), _ => BuildEditorIdIndex<T>(_linkCache));
    return index.TryGetValue(editorId, out formKey);
  }

  private static IReadOnlyDictionary<string, FormKey> BuildEditorIdIndex<T>(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    where T : class, ISkyrimMajorRecordGetter
  {
    var dict = new Dictionary<string, FormKey>(StringComparer.OrdinalIgnoreCase);
    foreach (var record in linkCache.WinningOverrides<T>())
    {
      if (!string.IsNullOrWhiteSpace(record.EditorID))
      {
        dict.TryAdd(record.EditorID, record.FormKey);
      }
    }

    return dict;
  }

  private static void AddOverrides<T>(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Dictionary<uint, FormKey> dict)
    where T : class, ISkyrimMajorRecordGetter
  {
    foreach (var formKey in linkCache.WinningOverrides<T>().Select(record => record.FormKey))
    {
      dict.TryAdd(formKey.ID, formKey);
    }
  }
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

      var sections = ResolveSections(filter, linkCache, cachedNpcs, knownVirtualKeywords, formIdCache, logger);

      if (!sections.HasAnyFilter && !filter.TargetsAllNpcs)
      {
        logger?.Debug("No filters could be resolved for SPID line: {Line}", filter.RawLine);
        return null;
      }

      var entry = BuildEntry(filter, sections);
      entry.Outfit = outfit;

      if (sections.OutfitFilterFormKeys.Count > 0)
      {
        logger?.Information(
          "Resolved {Count} outfit filter(s) for line: {Line} => {FormKeys}",
          sections.OutfitFilterFormKeys.Count,
          filter.RawLine,
          string.Join(", ", sections.OutfitFilterFormKeys));
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

      var sections = ResolveSections(filter, linkCache, cachedNpcs, knownVirtualKeywords, formIdCache, logger);

      var entry = BuildEntry(filter, sections);
      entry.Type                = DistributionType.Keyword;
      entry.KeywordToDistribute = keywordToDistribute;

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

  /// <summary>Every filter list resolved from one SPID line's sections, plus unresolvable remainders.</summary>
  private sealed record ResolvedSections(
    List<FormKeyFilter> NpcFilters,
    List<KeywordFilter> KeywordFilters,
    List<FormKeyFilter> FactionFilters,
    List<FormKeyFilter> RaceFilters,
    List<FormKey> ClassFormKeys,
    List<FormKey> CombatStyleFormKeys,
    List<FormKey> OutfitFilterFormKeys,
    List<FormKey> PerkFormKeys,
    List<FormKey> VoiceTypeFormKeys,
    List<FormKey> LocationFormKeys,
    List<FormKey> FormListFormKeys,
    string? RawStringFilters,
    string? RawFormFilters)
  {
    public bool HasAnyFilter =>
      NpcFilters.Count > 0 || FactionFilters.Count > 0 ||
      KeywordFilters.Count > 0 || RaceFilters.Count > 0 ||
      ClassFormKeys.Count > 0 || CombatStyleFormKeys.Count > 0 ||
      OutfitFilterFormKeys.Count > 0 || PerkFormKeys.Count > 0 ||
      VoiceTypeFormKeys.Count > 0 || LocationFormKeys.Count > 0 ||
      FormListFormKeys.Count > 0 ||
      !string.IsNullOrEmpty(RawStringFilters) || !string.IsNullOrEmpty(RawFormFilters);
  }

  private static ResolvedSections ResolveSections(
    SpidDistributionFilter filter,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    IReadOnlyList<INpcGetter> cachedNpcs,
    IReadOnlySet<string>? knownVirtualKeywords,
    FormIdLookupCache? formIdCache,
    ILogger? logger)
  {
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
      formIdCache,
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

    return new ResolvedSections(
      npcFilters,
      keywordFilters,
      factionFilters,
      raceFilters,
      classFormKeys,
      combatStyleFormKeys,
      outfitFilterFormKeys,
      perkFormKeys,
      voiceTypeFormKeys,
      locationFormKeys,
      formListFormKeys,
      rawStringFilters,
      rawFormFilters);
  }

  private static DistributionEntry BuildEntry(SpidDistributionFilter filter, ResolvedSections sections) =>
    new()
    {
      NpcFilters            = sections.NpcFilters,
      KeywordFilters        = sections.KeywordFilters,
      FactionFilters        = sections.FactionFilters,
      RaceFilters           = sections.RaceFilters,
      ClassFormKeys         = sections.ClassFormKeys,
      CombatStyleFormKeys   = sections.CombatStyleFormKeys,
      OutfitFilterFormKeys  = sections.OutfitFilterFormKeys,
      PerkFormKeys          = sections.PerkFormKeys,
      VoiceTypeFormKeys     = sections.VoiceTypeFormKeys,
      LocationFormKeys      = sections.LocationFormKeys,
      FormListFormKeys      = sections.FormListFormKeys,
      TraitFilters          = filter.TraitFilters,
      LevelFilters          = filter.LevelFilters,
      RawStringFilters      = sections.RawStringFilters,
      RawFormFilters        = sections.RawFormFilters,
      NpcLogicMode          = DetectLogicMode(filter.StringFilters, sections.NpcFilters.Count),
      KeywordLogicMode      = DetectLogicMode(filter.StringFilters, sections.KeywordFilters.Count),
      FactionLogicMode      = DetectLogicMode(filter.FormFilters, sections.FactionFilters.Count),
      RaceLogicMode         = DetectLogicMode(filter.FormFilters, sections.RaceFilters.Count),
      ClassLogicMode        = DetectLogicMode(filter.FormFilters, sections.ClassFormKeys.Count),
      LocationLogicMode     = DetectLogicMode(filter.FormFilters, sections.LocationFormKeys.Count),
      OutfitFilterLogicMode = DetectLogicMode(filter.FormFilters, sections.OutfitFilterFormKeys.Count)
    };

  private static void ProcessStringFilters(
    SpidFilterSection stringFilters,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    FormIdLookupCache? formIdCache,
    IReadOnlyList<INpcGetter> cachedNpcs,
    List<FormKeyFilter> npcFilters,
    List<KeywordFilter> keywordFilters,
    IReadOnlySet<string>? knownVirtualKeywords,
    ILogger? logger)
  {
    var context = new StringFilterContext(
      linkCache,
      formIdCache,
      cachedNpcs,
      npcFilters,
      keywordFilters,
      knownVirtualKeywords,
      logger);

    foreach (var expr in stringFilters.Expressions)
    {
      foreach (var part in expr.Parts)
      {
        if (part.HasWildcard)
        {
          continue;
        }

        ResolveStringFilterValue(part.Value, part.IsNegated, false, context);
      }
    }

    foreach (var exclusion in stringFilters.GlobalExclusions)
    {
      if (exclusion.HasWildcard)
      {
        continue;
      }

      ResolveStringFilterValue(exclusion.Value, true, true, context);
    }
  }

  private sealed record StringFilterContext(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> LinkCache,
    FormIdLookupCache? FormIdCache,
    IReadOnlyList<INpcGetter> CachedNpcs,
    List<FormKeyFilter> NpcFilters,
    List<KeywordFilter> KeywordFilters,
    IReadOnlySet<string>? KnownVirtualKeywords,
    ILogger? Logger);

  private static void ResolveStringFilterValue(
    string value,
    bool isNegated,
    bool isExclusion,
    StringFilterContext context)
  {
    var npc = context.CachedNpcs.FirstOrDefault(n =>
                                                  string.Equals(
                                                    n.EditorID,
                                                    value,
                                                    StringComparison.OrdinalIgnoreCase) ||
                                                  string.Equals(
                                                    n.Name.SafeString(n),
                                                    value,
                                                    StringComparison.OrdinalIgnoreCase));
    if (npc != null)
    {
      context.NpcFilters.Add(new FormKeyFilter(npc.FormKey, isNegated));
      context.Logger?.Debug(
        isExclusion
          ? "Resolved excluded NPC string filter '{Value}' to {FormKey}"
          : "Resolved NPC string filter '{Value}' to {FormKey}",
        value,
        npc.FormKey);
      return;
    }

    if (context.FormIdCache?.TryGetFormKeyByEditorId<IKeywordGetter>(value, out var keywordFormKey) == true)
    {
      context.KeywordFilters.Add(new KeywordFilter(value, isNegated));
      return;
    }

    var keyword = context.LinkCache.WinningOverrides<IKeywordGetter>()
                         .FirstOrDefault(k => string.Equals(
                                           k.EditorID,
                                           value,
                                           StringComparison.OrdinalIgnoreCase));
    if (keyword != null)
    {
      context.KeywordFilters.Add(new KeywordFilter(keyword.EditorID ?? value, isNegated));
      return;
    }

    if (context.KnownVirtualKeywords != null && context.KnownVirtualKeywords.Contains(value))
    {
      context.KeywordFilters.Add(new KeywordFilter(value, isNegated));
      return;
    }

    context.Logger?.Verbose(
      isExclusion
        ? "Unresolved global exclusion (not NPC or keyword): {Value}"
        : "Unresolved string filter (not NPC or keyword): {Value}",
      value);
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
          formIdCache,
          factionFilters,
          resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAddFilter<IRaceGetter>(
          part.Value,
          part.IsNegated,
          linkCache,
          formIdCache,
          raceFilters,
          resolvedEditorIds))
        {
          continue;
        }

        if (part.IsNegated)
        {
          continue;
        }

        if (TryResolveAndAdd<IClassGetter>(part.Value, linkCache, formIdCache, classFormKeys, resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<ICombatStyleGetter>(
          part.Value,
          linkCache,
          formIdCache,
          combatStyleFormKeys,
          resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<IOutfitGetter>(
          part.Value,
          linkCache,
          formIdCache,
          outfitFilterFormKeys,
          resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<IPerkGetter>(part.Value, linkCache, formIdCache, perkFormKeys, resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<IVoiceTypeGetter>(
          part.Value,
          linkCache,
          formIdCache,
          voiceTypeFormKeys,
          resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<ILocationGetter>(
          part.Value,
          linkCache,
          formIdCache,
          locationFormKeys,
          resolvedEditorIds))
        {
          continue;
        }

        if (TryResolveAndAdd<IFormListGetter>(
          part.Value,
          linkCache,
          formIdCache,
          formListFormKeys,
          resolvedEditorIds))
        {
          continue;
        }

        logger?.Debug("Could not resolve form filter: {Value}", part.Value);
      }
    }

    foreach (var value in formFilters.GlobalExclusions.Select(exclusion => exclusion.Value))
    {
      var exclusionPart = new SpidFilterPart { Value = value, IsNegated = true };
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

      if (TryResolveAndAddFilter<IFactionGetter>(value, true, linkCache, formIdCache, factionFilters, resolvedEditorIds))
      {
        continue;
      }

      TryResolveAndAddFilter<IRaceGetter>(value, true, linkCache, formIdCache, raceFilters, resolvedEditorIds);
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
    ILogger? logger)
  {
    if (!linkCache.TryResolve<ISkyrimMajorRecordGetter>(formKey, out var record))
    {
      return false;
    }

    return ClassifyAndAddRecord(
      record,
      isNegated,
      originalValue,
      npcFilters,
      factionFilters,
      raceFilters,
      locationFormKeys,
      outfitFilterFormKeys,
      resolvedEditorIds,
      logger);
  }

  private static bool ClassifyAndAddRecord(
    ISkyrimMajorRecordGetter record,
    bool isNegated,
    string originalValue,
    List<FormKeyFilter> npcFilters,
    List<FormKeyFilter> factionFilters,
    List<FormKeyFilter> raceFilters,
    List<FormKey> locationFormKeys,
    List<FormKey> outfitFilterFormKeys,
    HashSet<string>? resolvedEditorIds,
    ILogger? logger)
  {
    var formKey = record.FormKey;

    switch (record)
    {
      case INpcGetter:
        npcFilters.Add(new FormKeyFilter(formKey, isNegated));
        break;
      case IFactionGetter:
        factionFilters.Add(new FormKeyFilter(formKey, isNegated));
        break;
      case IRaceGetter:
        raceFilters.Add(new FormKeyFilter(formKey, isNegated));
        break;
      case ILocationGetter:
        locationFormKeys.Add(formKey);
        break;
      case IOutfitGetter:
        outfitFilterFormKeys.Add(formKey);
        break;
      default:
        logger?.Debug(
          "FormID {Value} resolved to unsupported type {Type}",
          originalValue,
          record.GetType().Name);
        return false;
    }

    resolvedEditorIds?.Add(originalValue);
    logger?.Debug(
      "Resolved FormID {Value} as {Type}: {EditorId}",
      originalValue,
      record.GetType().Name,
      record.EditorID);
    return true;
  }

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
      if (!formIdCache.FormKeysByFormId.TryGetValue(formId, out var formKey))
      {
        logger?.Warning("Could not resolve bare FormID {Value} (0x{FormId:X}) via cache", originalValue, formId);
        return false;
      }

      return TryResolveByFormKey(
        formKey,
        isNegated,
        originalValue,
        linkCache,
        npcFilters,
        factionFilters,
        raceFilters,
        locationFormKeys,
        outfitFilterFormKeys,
        resolvedEditorIds,
        logger);
    }

    var record = FindRecordByBareFormId(formId, linkCache);
    if (record == null)
    {
      logger?.Warning(
        "Could not resolve bare FormID {Value} (0x{FormId:X}) as NPC, Faction, Race, Location, or Outfit",
        originalValue,
        formId);
      return false;
    }

    return ClassifyAndAddRecord(
      record,
      isNegated,
      originalValue,
      npcFilters,
      factionFilters,
      raceFilters,
      locationFormKeys,
      outfitFilterFormKeys,
      resolvedEditorIds,
      logger);
  }

  private static ISkyrimMajorRecordGetter? FindRecordByBareFormId(
    uint formId,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    ISkyrimMajorRecordGetter? record = FindByBareId<INpcGetter>(formId, linkCache);
    record ??= FindByBareId<IFactionGetter>(formId, linkCache);
    record ??= FindByBareId<IRaceGetter>(formId, linkCache);
    record ??= FindByBareId<ILocationGetter>(formId, linkCache);
    record ??= FindByBareId<IOutfitGetter>(formId, linkCache);
    return record;
  }

  private static T? FindByBareId<T>(uint formId, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    where T : class, ISkyrimMajorRecordGetter =>
    linkCache.WinningOverrides<T>().FirstOrDefault(r => r.FormKey.ID == formId);

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
    FormIdLookupCache? formIdCache,
    List<FormKey> targetList,
    HashSet<string>? resolvedEditorIds)
    where T : class, ISkyrimMajorRecordGetter
  {
    if (formIdCache?.TryGetFormKeyByEditorId<T>(editorId, out var cachedFormKey) == true)
    {
      targetList.Add(cachedFormKey);
      resolvedEditorIds?.Add(editorId);
      return true;
    }

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
    FormIdLookupCache? formIdCache,
    List<FormKeyFilter> targetList,
    HashSet<string>? resolvedEditorIds)
    where T : class, ISkyrimMajorRecordGetter
  {
    if (formIdCache?.TryGetFormKeyByEditorId<T>(editorId, out var cachedFormKey) == true)
    {
      targetList.Add(new FormKeyFilter(cachedFormKey, isNegated));
      resolvedEditorIds?.Add(editorId);
      return true;
    }

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
                        string.Equals(n.Name.SafeString(n), value, StringComparison.OrdinalIgnoreCase)));

    var unresolvableParts = new List<string>();

    foreach (var expr in stringFilters.Expressions)
    {
      var exprParts = expr.Parts
                          .Where(part => part.HasWildcard || !IsResolved(part.Value))
                          .Select(part => part is { HasWildcard: false, IsNegated: true }
                                           ? $"-{part.Value}"
                                           : part.Value)
                          .ToList();

      if (exprParts.Count > 0)
      {
        unresolvableParts.Add(string.Join("+", exprParts));
      }
    }

    foreach (var value in stringFilters.GlobalExclusions
               .Select(exclusion => exclusion.Value)
               .Where(v => !IsResolved(v)))
    {
      unresolvableParts.Add($"-{value}");
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

    foreach (var value in formFilters.GlobalExclusions
               .Select(exclusion => exclusion.Value)
               .Where(v => !resolvedEditorIds.Contains(v)))
    {
      unresolvableParts.Add($"-{value}");
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
