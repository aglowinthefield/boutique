using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.ViewModels;

public sealed class ContainerRecordViewModel(
  IContainerGetter container,
  ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
  string? merchantFaction = null,
  IReadOnlyList<string>? cellPlacements = null)
{
  public FormKey FormKey { get; } = container.FormKey;
  public string EditorId { get; } = container.EditorID ?? string.Empty;
  public string Name { get; } = container.Name?.String ?? container.EditorID ?? container.FormKey.ToString();
  public string ModName { get; } = container.FormKey.ModKey.FileName;
  public bool Respawns { get; } = container.Flags.HasFlag(Container.Flag.Respawns);
  public IReadOnlyList<ContainerContentItem> Items { get; } = ResolveItems(container, linkCache);
  public string? MerchantFaction { get; } = merchantFaction;
  public IReadOnlyList<string> CellPlacements { get; } = cellPlacements ?? [];

  public int ItemCount => Items.Count;
  public string DisplayName => string.IsNullOrEmpty(Name) ? EditorId : Name;
  public bool IsMerchantContainer => !string.IsNullOrEmpty(MerchantFaction);

  public string CellPlacementsDisplay => CellPlacements.Count > 0
                                           ? string.Join(", ", CellPlacements.Take(3)) +
                                             (CellPlacements.Count > 3
                                                ? $" (+{CellPlacements.Count - 3})"
                                                : string.Empty)
                                           : string.Empty;

  private static List<ContainerContentItem> ResolveItems(
    IContainerGetter container,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    if (container.Items is null || container.Items.Count == 0)
    {
      return [];
    }

    var items = new List<ContainerContentItem>(container.Items.Count);

    foreach (var entry in container.Items)
    {
      var itemLink = entry.Item.Item;
      var count    = entry.Item.Count;

      if (itemLink.IsNull)
      {
        continue;
      }

      var formKey  = itemLink.FormKey;
      var name     = string.Empty;
      var editorId = string.Empty;

      if (linkCache.TryResolve<ISkyrimMajorRecordGetter>(formKey, out var record))
      {
        editorId = record.EditorID ?? string.Empty;
        name     = (record as ITranslatedNamedGetter)?.Name?.String ?? string.Empty;
      }

      items.Add(
        new ContainerContentItem(
          formKey,
          name,
          editorId,
          count,
          formKey.ModKey.FileName));
    }

    return items;
  }
}
