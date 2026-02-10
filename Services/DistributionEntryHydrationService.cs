using System.Collections.ObjectModel;
using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public class DistributionEntryHydrationService(
  GameDataCacheService cache,
  MutagenService mutagenService)
{
  public void HydrateEntry(
    DistributionEntryViewModel entryVm,
    DistributionEntry entry,
    IEnumerable<IOutfitGetter> availableOutfits)
  {
    ResolveEntryOutfit(entryVm, availableOutfits);

    var npcVms = ResolveNpcFilters(entry.NpcFilters);
    if (npcVms.Count > 0)
    {
      entryVm.SelectedNpcs = new ObservableCollection<NpcRecordViewModel>(npcVms);
      entryVm.UpdateEntryNpcs();
    }

    var factionVms = ResolveFactionFilters(entry.FactionFilters);
    if (factionVms.Count > 0)
    {
      entryVm.SelectedFactions = new ObservableCollection<FactionRecordViewModel>(factionVms);
      entryVm.UpdateEntryFactions();
    }

    var keywordVms = ResolveKeywordFilters(entry.KeywordFilters);
    if (keywordVms.Count > 0)
    {
      entryVm.SelectedKeywords = new ObservableCollection<KeywordRecordViewModel>(keywordVms);
      entryVm.UpdateEntryKeywords();
    }

    var raceVms = ResolveRaceFilters(entry.RaceFilters);
    if (raceVms.Count > 0)
    {
      entryVm.SelectedRaces = new ObservableCollection<RaceRecordViewModel>(raceVms);
      entryVm.UpdateEntryRaces();
    }

    var classVms = ResolveClassFormKeys(entry.ClassFormKeys);
    if (classVms.Count > 0)
    {
      entryVm.SelectedClasses = new ObservableCollection<ClassRecordViewModel>(classVms);
      entryVm.UpdateEntryClasses();
    }

    var outfitFilterVms = ResolveOutfitFilterFormKeys(entry.OutfitFilterFormKeys);
    if (outfitFilterVms.Count > 0)
    {
      entryVm.SelectedOutfitFilters = new ObservableCollection<OutfitRecordViewModel>(outfitFilterVms);
      entryVm.UpdateEntryOutfitFilters();
    }
  }

  public static void ResolveEntryOutfit(DistributionEntryViewModel entryVm, IEnumerable<IOutfitGetter> availableOutfits)
  {
    if (entryVm.SelectedOutfit == null)
    {
      return;
    }

    var outfitFormKey  = entryVm.SelectedOutfit.FormKey;
    var matchingOutfit = availableOutfits.FirstOrDefault(o => o.FormKey == outfitFormKey);

    if (matchingOutfit != null)
    {
      entryVm.SelectedOutfit = matchingOutfit;
    }
  }

  public List<NpcRecordViewModel> ResolveNpcFilters(IEnumerable<FormKeyFilter> filters)
  {
    var npcVms = new List<NpcRecordViewModel>();

    foreach (var filter in filters)
    {
      var npcVm = ResolveNpcFormKey(filter.FormKey);
      if (npcVm != null)
      {
        npcVm.IsExcluded = filter.IsExcluded;
        npcVms.Add(npcVm);
      }
    }

    return npcVms;
  }

  public NpcRecordViewModel? ResolveNpcFormKey(FormKey formKey)
  {
    var existingNpc = cache.AllNpcRecords.FirstOrDefault(npc => npc.FormKey == formKey);
    if (existingNpc != null)
    {
      return new NpcRecordViewModel(existingNpc.NpcRecord);
    }

    if (mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
        linkCache.TryResolve<INpcGetter>(formKey, out var npc))
    {
      var npcRecord = new NpcRecord(
        npc.FormKey,
        npc.EditorID,
        npc.Name?.String,
        npc.FormKey.ModKey);
      return new NpcRecordViewModel(npcRecord);
    }

    return null;
  }

  public List<FactionRecordViewModel> ResolveFactionFilters(IEnumerable<FormKeyFilter> filters)
  {
    var factionVms = new List<FactionRecordViewModel>();

    foreach (var filter in filters)
    {
      var factionVm = ResolveFactionFormKey(filter.FormKey);
      if (factionVm != null)
      {
        factionVm.IsExcluded = filter.IsExcluded;
        factionVms.Add(factionVm);
      }
    }

    return factionVms;
  }

  public FactionRecordViewModel? ResolveFactionFormKey(FormKey formKey)
  {
    var existingFaction = cache.AllFactions.FirstOrDefault(f => f.FormKey == formKey);
    if (existingFaction != null)
    {
      return new FactionRecordViewModel(existingFaction.FactionRecord);
    }

    if (mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
        linkCache.TryResolve<IFactionGetter>(formKey, out var faction))
    {
      return new FactionRecordViewModel(FactionRecord.FromGetter(faction));
    }

    return null;
  }

  public List<KeywordRecordViewModel> ResolveKeywordFilters(IEnumerable<KeywordFilter> filters)
  {
    var keywordVms = new List<KeywordRecordViewModel>();

    foreach (var filter in filters)
    {
      var keywordVm = ResolveKeywordEditorId(filter.EditorId);
      if (keywordVm != null)
      {
        keywordVm.IsExcluded = filter.IsExcluded;
        keywordVms.Add(keywordVm);
      }
    }

    return keywordVms;
  }

  public KeywordRecordViewModel? ResolveKeywordEditorId(string editorId)
  {
    if (string.IsNullOrWhiteSpace(editorId))
    {
      return null;
    }

    var existingKeyword = cache.AllKeywords.FirstOrDefault(k =>
                                                             string.Equals(
                                                               k.KeywordRecord.EditorID,
                                                               editorId,
                                                               StringComparison.OrdinalIgnoreCase));
    if (existingKeyword != null)
    {
      return new KeywordRecordViewModel(existingKeyword.KeywordRecord);
    }

    if (mutagenService.LinkCache is { } linkCache)
    {
      var keyword = linkCache.WinningOverrides<IKeywordGetter>()
                             .FirstOrDefault(k => string.Equals(
                                               k.EditorID,
                                               editorId,
                                               StringComparison.OrdinalIgnoreCase));
      if (keyword != null)
      {
        return new KeywordRecordViewModel(KeywordRecord.FromGetter(keyword));
      }
    }

    var virtualRecord = new KeywordRecord(FormKey.Null, editorId, ModKey.Null);
    return new KeywordRecordViewModel(virtualRecord);
  }

  public KeywordRecordViewModel? ResolveKeywordByFormKey(FormKey formKey)
  {
    var existingKeyword = cache.AllKeywords.FirstOrDefault(k => k.FormKey == formKey);
    if (existingKeyword != null)
    {
      return new KeywordRecordViewModel(existingKeyword.KeywordRecord);
    }

    if (mutagenService.LinkCache is { } linkCache &&
        linkCache.TryResolve<IKeywordGetter>(formKey, out var keyword))
    {
      return new KeywordRecordViewModel(KeywordRecord.FromGetter(keyword));
    }

    return null;
  }

  public List<RaceRecordViewModel> ResolveRaceFilters(IEnumerable<FormKeyFilter> filters)
  {
    var raceVms = new List<RaceRecordViewModel>();

    foreach (var filter in filters)
    {
      var raceVm = ResolveRaceFormKey(filter.FormKey);
      if (raceVm != null)
      {
        raceVm.IsExcluded = filter.IsExcluded;
        raceVms.Add(raceVm);
      }
    }

    return raceVms;
  }

  public RaceRecordViewModel? ResolveRaceFormKey(FormKey formKey)
  {
    var existingRace = cache.AllRaces.FirstOrDefault(r => r.FormKey == formKey);
    if (existingRace != null)
    {
      return new RaceRecordViewModel(existingRace.RaceRecord);
    }

    if (mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
        linkCache.TryResolve<IRaceGetter>(formKey, out var race))
    {
      return new RaceRecordViewModel(RaceRecord.FromGetter(race));
    }

    return null;
  }

  private List<ClassRecordViewModel> ResolveClassFormKeys(IEnumerable<FormKey> formKeys) =>
    formKeys.Select(ResolveClassFormKey).OfType<ClassRecordViewModel>().ToList();

  public ClassRecordViewModel? ResolveClassFormKey(FormKey formKey)
  {
    var existingClass = cache.AllClasses.FirstOrDefault(c => c.FormKey == formKey);
    if (existingClass != null)
    {
      return existingClass;
    }

    if (mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
        linkCache.TryResolve<IClassGetter>(formKey, out var classRecord))
    {
      return new ClassRecordViewModel(ClassRecord.FromGetter(classRecord));
    }

    return null;
  }

  private List<OutfitRecordViewModel> ResolveOutfitFilterFormKeys(IEnumerable<FormKey> formKeys) =>
  [
    .. formKeys.Select(ResolveOutfitFilterFormKey).OfType<OutfitRecordViewModel>()
  ];

  public OutfitRecordViewModel? ResolveOutfitFilterFormKey(FormKey formKey)
  {
    if (mutagenService.LinkCache is ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache &&
        linkCache.TryResolve<IOutfitGetter>(formKey, out var outfit))
    {
      return new OutfitRecordViewModel(outfit);
    }

    return null;
  }
}
