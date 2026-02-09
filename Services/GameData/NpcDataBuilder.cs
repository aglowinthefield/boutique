using System.Collections.Concurrent;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services.GameData;

public static class NpcDataBuilder
{
  public static (List<NpcFilterData> FilterData, List<NpcRecordViewModel> ViewModels) LoadNpcs(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Dictionary<FormKey, string> keywordLookup,
    Dictionary<FormKey, string> factionLookup,
    Dictionary<FormKey, string> raceLookup,
    Dictionary<FormKey, string> classLookup,
    Dictionary<FormKey, string> outfitLookup,
    Dictionary<FormKey, string> templateLookup,
    Dictionary<FormKey, string> combatStyleLookup,
    Dictionary<FormKey, string> voiceTypeLookup,
    Dictionary<FormKey, HashSet<string>> raceKeywordLookup,
    Func<ModKey, bool> isBlacklisted)
  {
    var validNpcs = linkCache.WinningOverrides<INpcGetter>()
      .Where(npc => npc.FormKey != FormKey.Null && !string.IsNullOrWhiteSpace(npc.EditorID) && !isBlacklisted(npc.FormKey.ModKey));

    var filterDataBag = new ConcurrentBag<NpcFilterData>();
    var recordsBag = new ConcurrentBag<NpcRecordViewModel>();

    Parallel.ForEach(
      validNpcs,
      npc =>
      {
        try
        {
          var originalModKey = npc.FormKey.ModKey;
          var filterData = BuildNpcFilterData(
            npc,
            originalModKey,
            keywordLookup,
            factionLookup,
            raceLookup,
            classLookup,
            outfitLookup,
            templateLookup,
            combatStyleLookup,
            voiceTypeLookup,
            raceKeywordLookup);
          if (filterData is not null)
          {
            filterDataBag.Add(filterData);
          }

          var record = new NpcRecord(
            npc.FormKey,
            npc.EditorID!,
            NpcDataExtractor.GetName(npc),
            originalModKey);
          recordsBag.Add(new NpcRecordViewModel(record));
        }
        catch
        {
          // ignored
        }
      });

    return ([.. filterDataBag], [.. recordsBag]);
  }

  private static NpcFilterData? BuildNpcFilterData(
    INpcGetter npc,
    ModKey originalModKey,
    Dictionary<FormKey, string> keywordLookup,
    Dictionary<FormKey, string> factionLookup,
    Dictionary<FormKey, string> raceLookup,
    Dictionary<FormKey, string> classLookup,
    Dictionary<FormKey, string> outfitLookup,
    Dictionary<FormKey, string> templateLookup,
    Dictionary<FormKey, string> combatStyleLookup,
    Dictionary<FormKey, string> voiceTypeLookup,
    Dictionary<FormKey, HashSet<string>> raceKeywordLookup)
  {
    try
    {
      var keywords = ExtractKeywordsFast(npc, keywordLookup, raceKeywordLookup);
      var factions = ExtractFactionsFast(npc, factionLookup);

      var (raceFormKey, raceEditorId) = ResolveLinkFast(npc.Race, raceLookup);
      var (classFormKey, classEditorId) = ResolveLinkFast(npc.Class, classLookup);
      var (outfitFormKey, outfitEditorId) = ResolveLinkFast(npc.DefaultOutfit, outfitLookup);
      var (templateFormKey, templateEditorId) = ResolveLinkFast(npc.Template, templateLookup);
      var (combatStyleFormKey, combatStyleEditorId) = ResolveLinkFast(npc.CombatStyle, combatStyleLookup);
      var (voiceTypeFormKey, voiceTypeEditorId) = ResolveLinkFast(npc.Voice, voiceTypeLookup);
      var wornArmorFormKey = npc.WornArmor.FormKeyNullable;

      var (isFemale, isUnique, isSummonable, isLeveled) = NpcDataExtractor.ExtractTraits(npc);
      var isChild = NpcDataExtractor.IsChildRace(raceEditorId);
      var level = NpcDataExtractor.ExtractLevel(npc);
      var skillValues = NpcDataExtractor.ExtractSkillValues(npc);

      var filterData = new NpcFilterData
      {
        FormKey = npc.FormKey,
        EditorId = npc.EditorID,
        Name = NpcDataExtractor.GetName(npc),
        SourceMod = originalModKey,
        Keywords = keywords,
        Factions = factions,
        RaceFormKey = raceFormKey,
        RaceEditorId = raceEditorId,
        ClassFormKey = classFormKey,
        ClassEditorId = classEditorId,
        CombatStyleFormKey = combatStyleFormKey,
        CombatStyleEditorId = combatStyleEditorId,
        VoiceTypeFormKey = voiceTypeFormKey,
        VoiceTypeEditorId = voiceTypeEditorId,
        DefaultOutfitFormKey = outfitFormKey,
        DefaultOutfitEditorId = outfitEditorId,
        WornArmorFormKey = wornArmorFormKey,
        IsFemale = isFemale,
        IsUnique = isUnique,
        IsSummonable = isSummonable,
        IsChild = isChild,
        IsLeveled = isLeveled,
        Level = level,
        TemplateFormKey = templateFormKey,
        TemplateEditorId = templateEditorId,
        SkillValues = skillValues
      };

      var matchKeys = BuildMatchKeys(filterData);
      filterData.MatchKeys = matchKeys;

      return filterData;
    }
    catch
    {
      return null;
    }
  }

  private static HashSet<string> BuildMatchKeys(NpcFilterData filterData)
  {
    var matchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    if (!string.IsNullOrWhiteSpace(filterData.Name))
    {
      matchKeys.Add(filterData.Name);
    }

    if (!string.IsNullOrWhiteSpace(filterData.EditorId))
    {
      matchKeys.Add(filterData.EditorId);
    }

    if (!string.IsNullOrWhiteSpace(filterData.TemplateEditorId))
    {
      matchKeys.Add(filterData.TemplateEditorId);
    }

    foreach (var kw in filterData.Keywords)
    {
      matchKeys.Add(kw);
    }

    foreach (var f in filterData.Factions)
    {
      if (!string.IsNullOrWhiteSpace(f.FactionEditorId))
      {
        matchKeys.Add(f.FactionEditorId);
      }
    }

    if (!string.IsNullOrWhiteSpace(filterData.RaceEditorId))
    {
      matchKeys.Add(filterData.RaceEditorId);
    }

    if (!string.IsNullOrWhiteSpace(filterData.ClassEditorId))
    {
      matchKeys.Add(filterData.ClassEditorId);
    }

    if (!string.IsNullOrWhiteSpace(filterData.CombatStyleEditorId))
    {
      matchKeys.Add(filterData.CombatStyleEditorId);
    }

    if (!string.IsNullOrWhiteSpace(filterData.VoiceTypeEditorId))
    {
      matchKeys.Add(filterData.VoiceTypeEditorId);
    }

    if (!string.IsNullOrWhiteSpace(filterData.DefaultOutfitEditorId))
    {
      matchKeys.Add(filterData.DefaultOutfitEditorId);
    }

    return matchKeys;
  }

  private static HashSet<string> ExtractKeywordsFast(
    INpcGetter npc,
    Dictionary<FormKey, string> keywordLookup,
    Dictionary<FormKey, HashSet<string>> raceKeywordLookup)
  {
    raceKeywordLookup.TryGetValue(npc.Race.FormKey, out var raceKeywords);
    var hasRaceKeywords = raceKeywords != null && raceKeywords.Count > 0;
    var hasNpcKeywords = npc.Keywords != null && npc.Keywords.Count > 0;

    if (!hasNpcKeywords && !hasRaceKeywords)
    {
      return [];
    }

    if (!hasNpcKeywords && hasRaceKeywords)
    {
      return new HashSet<string>(raceKeywords!, StringComparer.OrdinalIgnoreCase);
    }

    var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AddKeywordsFromAspectFast(npc, keywordLookup, keywords);

    if (hasRaceKeywords)
    {
      keywords.UnionWith(raceKeywords!);
    }

    return keywords;
  }

  private static void AddKeywordsFromAspectFast(
    IKeywordedGetter record,
    Dictionary<FormKey, string> lookup,
    HashSet<string> target)
  {
    if (record.Keywords == null)
    {
      return;
    }

    foreach (var kw in record.Keywords)
    {
      if (lookup.TryGetValue(kw.FormKey, out var editorId))
      {
        target.Add(editorId);
      }
    }
  }

  private static (FormKey? FormKey, string? EditorId) ResolveLinkFast(
    IFormLinkGetter link,
    Dictionary<FormKey, string> lookup)
  {
    if (link.IsNull)
    {
      return (null, null);
    }

    lookup.TryGetValue(link.FormKey, out var editorId);
    return (link.FormKey, editorId);
  }

  private static List<FactionMembership> ExtractFactionsFast(
    INpcGetter npc,
    Dictionary<FormKey, string> factionLookup)
  {
    var factions = new List<FactionMembership>();
    if (npc.Factions != null)
    {
      foreach (var f in npc.Factions)
      {
        if (factionLookup.TryGetValue(f.Faction.FormKey, out var editorId))
        {
          factions.Add(new FactionMembership
          {
            FactionFormKey = f.Faction.FormKey, FactionEditorId = editorId, Rank = f.Rank
          });
        }
        else
        {
          factions.Add(new FactionMembership
          {
            FactionFormKey = f.Faction.FormKey, FactionEditorId = null, Rank = f.Rank
          });
        }
      }
    }

    return factions;
  }
}
