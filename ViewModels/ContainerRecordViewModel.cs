using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.ViewModels;

public sealed class ContainerRecordViewModel
{
    public ContainerRecordViewModel(
        IContainerGetter container,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        string? merchantFaction = null,
        IReadOnlyList<string>? cellPlacements = null)
    {
        FormKey = container.FormKey;
        EditorId = container.EditorID ?? string.Empty;
        Name = container.Name?.String ?? container.EditorID ?? container.FormKey.ToString();
        ModName = container.FormKey.ModKey.FileName;
        Respawns = container.Flags.HasFlag(Container.Flag.Respawns);
        Items = ResolveItems(container, linkCache);
        MerchantFaction = merchantFaction;
        CellPlacements = cellPlacements ?? [];
    }

    public FormKey FormKey { get; }
    public string EditorId { get; }
    public string Name { get; }
    public string ModName { get; }
    public bool Respawns { get; }
    public IReadOnlyList<ContainerContentItem> Items { get; }
    public string? MerchantFaction { get; }
    public IReadOnlyList<string> CellPlacements { get; }

    public int ItemCount => Items.Count;
    public string DisplayName => string.IsNullOrEmpty(Name) ? EditorId : Name;
    public bool IsMerchantContainer => !string.IsNullOrEmpty(MerchantFaction);
    public string CellPlacementsDisplay => CellPlacements.Count > 0 ? string.Join(", ", CellPlacements.Take(3)) + (CellPlacements.Count > 3 ? $" (+{CellPlacements.Count - 3})" : "") : "";

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
            var count = entry.Item.Count;

            if (itemLink.IsNull)
            {
                continue;
            }

            var formKey = itemLink.FormKey;
            var name = string.Empty;
            var editorId = string.Empty;

            if (linkCache.TryResolve<ISkyrimMajorRecordGetter>(formKey, out var record))
            {
                editorId = record.EditorID ?? string.Empty;
                name = (record as ITranslatedNamedGetter)?.Name?.String ?? string.Empty;
            }

            items.Add(new ContainerContentItem(
                formKey,
                name,
                editorId,
                count,
                formKey.ModKey.FileName));
        }

        return items;
    }
}
