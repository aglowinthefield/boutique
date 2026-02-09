using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services.GameData;

public static class RecordLoaders
{
  public static List<FactionRecordViewModel> LoadFactions(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<Mutagen.Bethesda.Plugins.ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IFactionGetter, FactionRecordViewModel>(
      linkCache,
      f => new FactionRecordViewModel(new FactionRecord(f.FormKey, f.EditorID, f.Name?.String, f.FormKey.ModKey)),
      f => f.DisplayName,
      isBlacklisted);

  public static List<RaceRecordViewModel> LoadRaces(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<Mutagen.Bethesda.Plugins.ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IRaceGetter, RaceRecordViewModel>(
      linkCache,
      r => new RaceRecordViewModel(new RaceRecord(r.FormKey, r.EditorID, r.Name?.String, r.FormKey.ModKey)),
      r => r.DisplayName,
      isBlacklisted);

  public static List<KeywordRecordViewModel> LoadKeywords(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<Mutagen.Bethesda.Plugins.ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IKeywordGetter, KeywordRecordViewModel>(
      linkCache,
      k => new KeywordRecordViewModel(new KeywordRecord(k.FormKey, k.EditorID, k.FormKey.ModKey)),
      k => k.DisplayName,
      isBlacklisted);

  public static List<ClassRecordViewModel> LoadClasses(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<Mutagen.Bethesda.Plugins.ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRecords<IClassGetter, ClassRecordViewModel>(
      linkCache,
      c => new ClassRecordViewModel(new ClassRecord(c.FormKey, c.EditorID, c.Name?.String, c.FormKey.ModKey)),
      c => c.DisplayName,
      isBlacklisted);

  public static List<IOutfitGetter> LoadOutfits(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<Mutagen.Bethesda.Plugins.ModKey, bool> isBlacklisted) =>
    RecordLoader.LoadRawRecords<IOutfitGetter>(linkCache, isBlacklisted);
}
