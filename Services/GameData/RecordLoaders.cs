using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services.GameData;

/// <summary>
///   Loads game records (factions, keywords, races, classes) from the link cache
///   and converts them into view model objects for use in filter dropdowns and selection lists.
///   Records are attributed to the mod supplying their winning override.
/// </summary>
public static class RecordLoaders
{
  public static List<FactionRecordViewModel> LoadFactions(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IFaction, IFactionGetter, FactionRecordViewModel>(
      linkCache,
      (f, sourceMod) => new FactionRecordViewModel(FactionRecord.FromGetter(f, sourceMod)),
      f => f.DisplayName,
      isBlacklisted);

  public static List<RaceRecordViewModel> LoadRaces(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IRace, IRaceGetter, RaceRecordViewModel>(
      linkCache,
      (r, sourceMod) => new RaceRecordViewModel(RaceRecord.FromGetter(r, sourceMod)),
      r => r.DisplayName,
      isBlacklisted);

  public static List<KeywordRecordViewModel> LoadKeywords(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IKeyword, IKeywordGetter, KeywordRecordViewModel>(
      linkCache,
      (k, sourceMod) => new KeywordRecordViewModel(KeywordRecord.FromGetter(k, sourceMod)),
      k => k.DisplayName,
      isBlacklisted);

  public static List<ClassRecordViewModel> LoadClasses(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IClass, IClassGetter, ClassRecordViewModel>(
      linkCache,
      (c, sourceMod) => new ClassRecordViewModel(ClassRecord.FromGetter(c, sourceMod)),
      c => c.DisplayName,
      isBlacklisted);

  public static List<LocationRecordViewModel> LoadLocations(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<ILocation, ILocationGetter, LocationRecordViewModel>(
      linkCache,
      (l, sourceMod) => new LocationRecordViewModel(LocationRecord.FromGetter(l, sourceMod)),
      l => l.DisplayName,
      isBlacklisted);

  public static List<(IOutfitGetter Record, ModKey SourceMod)> LoadOutfits(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRawRecords<IOutfit, IOutfitGetter>(linkCache, isBlacklisted);
}
