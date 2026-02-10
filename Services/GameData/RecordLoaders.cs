using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services.GameData;

public static class RecordLoaders
{
  public static List<FactionRecordViewModel> LoadFactions(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IFactionGetter, FactionRecordViewModel>(
      linkCache,
      f => new FactionRecordViewModel(FactionRecord.FromGetter(f)),
      f => f.DisplayName,
      isBlacklisted);

  public static List<RaceRecordViewModel> LoadRaces(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IRaceGetter, RaceRecordViewModel>(
      linkCache,
      r => new RaceRecordViewModel(RaceRecord.FromGetter(r)),
      r => r.DisplayName,
      isBlacklisted);

  public static List<KeywordRecordViewModel> LoadKeywords(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IKeywordGetter, KeywordRecordViewModel>(
      linkCache,
      k => new KeywordRecordViewModel(KeywordRecord.FromGetter(k)),
      k => k.DisplayName,
      isBlacklisted);

  public static List<ClassRecordViewModel> LoadClasses(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IClassGetter, ClassRecordViewModel>(
      linkCache,
      c => new ClassRecordViewModel(ClassRecord.FromGetter(c)),
      c => c.DisplayName,
      isBlacklisted);

  public static List<IOutfitGetter> LoadOutfits(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRawRecords<IOutfitGetter>(linkCache, isBlacklisted);
}
