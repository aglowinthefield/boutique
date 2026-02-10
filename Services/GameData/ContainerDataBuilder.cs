using System.Diagnostics;
using Boutique.Utilities;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services.GameData;

public class ContainerDataBuilder(ILogger logger)
{
  private readonly ILogger _logger = logger.ForContext<ContainerDataBuilder>();

  public List<ContainerRecordViewModel> LoadContainers(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted)
  {
    var sw = Stopwatch.StartNew();

    var merchantContainers = BuildMerchantContainerLookup(linkCache);
    var merchantTime       = sw.ElapsedMilliseconds;

    var cellPlacements = BuildCellPlacementLookup(linkCache);
    var cellTime       = sw.ElapsedMilliseconds - merchantTime;

    var containers = linkCache.WinningOverrides<IContainerGetter>()
                              .Where(c => !isBlacklisted(c.FormKey.ModKey))
                              .Select(c => new ContainerRecordViewModel(
                                        c,
                                        linkCache,
                                        merchantContainers.GetValueOrDefault(c.FormKey),
                                        cellPlacements.GetValueOrDefault(c.FormKey)))
                              .OrderBy(c => c.DisplayName)
                              .ToList();

    sw.Stop();
    _logger.Information(
      "Container loading: {MerchantMs}ms merchant lookup, {CellMs}ms cell placement, {TotalMs}ms total for {Count} containers",
      merchantTime,
      cellTime,
      sw.ElapsedMilliseconds,
      containers.Count);

    return containers;
  }

  private Dictionary<FormKey, string> BuildMerchantContainerLookup(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var result = new Dictionary<FormKey, string>();

    foreach (var faction in linkCache.WinningOverrides<IFactionGetter>())
    {
      RecordProcessingHelper.TryProcessRecord(
        _logger,
        faction,
        () =>
        {
          if (faction.MerchantContainer.IsNull)
          {
            return;
          }

          if (!linkCache.TryResolve<IPlacedObjectGetter>(
                faction.MerchantContainer.FormKey,
                out var placedRef) ||
              placedRef.Base.IsNull)
          {
            return;
          }

          var factionName =
            faction.Name?.String ??
            faction.EditorID ?? faction.FormKey.ToString();
          result.TryAdd(placedRef.Base.FormKey, factionName);
        },
        "faction");
    }

    return result;
  }

  private Dictionary<FormKey, List<string>> BuildCellPlacementLookup(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var result = new Dictionary<FormKey, List<string>>();

    foreach (var cell in linkCache.WinningOverrides<ICellGetter>())
    {
      RecordProcessingHelper.TryProcessRecord(
        _logger,
        cell,
        () =>
        {
          var cellName = cell.Name?.String ?? cell.EditorID ?? cell.FormKey.ToString();
          ProcessPlacedObjects(cell.Temporary, cellName);
          ProcessPlacedObjects(cell.Persistent, cellName);
        },
        "cell");
    }

    return result;

    void ProcessPlacedObjects(IReadOnlyList<IPlacedGetter>? placedObjects, string locationName)
    {
      if (placedObjects == null)
      {
        return;
      }

      foreach (var placed in placedObjects)
      {
        if (placed is not IPlacedObjectGetter placedObj || placedObj.Base.IsNull)
        {
          continue;
        }

        AddCellPlacement(placedObj.Base.FormKey, locationName);
      }
    }

    void AddCellPlacement(FormKey containerFormKey, string locationName)
    {
      if (!result.TryGetValue(containerFormKey, out var list))
      {
        list                     = [];
        result[containerFormKey] = list;
      }

      if (!list.Contains(locationName))
      {
        list.Add(locationName);
      }
    }
  }
}
