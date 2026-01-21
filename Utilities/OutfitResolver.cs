using System.Diagnostics.CodeAnalysis;
using Boutique.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

/// <summary>
///     Helper class for resolving outfits from various identifier formats.
/// </summary>
public static class OutfitResolver
{
    /// <summary>
    ///     Tries to resolve an outfit from a string identifier (FormKey or EditorID format).
    /// </summary>
    public static bool TryResolve(
        string identifier,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ref List<IOutfitGetter>? cachedOutfits,
        [NotNullWhen(true)] out IOutfitGetter? outfit,
        out string label)
    {
        outfit = null;
        label = string.Empty;

        if (FormKeyHelper.TryParse(identifier, out var formKey) &&
            linkCache.TryResolve<IOutfitGetter>(formKey, out var resolvedFromFormKey))
        {
            outfit = resolvedFromFormKey;
            label = outfit.EditorID ?? formKey.ToString();
            return true;
        }

        if (TryResolveByEditorId(identifier, linkCache, ref cachedOutfits, out var resolvedFromEditorId))
        {
            outfit = resolvedFromEditorId;
            label = outfit.EditorID ?? identifier;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Tries to resolve an outfit by EditorID, optionally filtering by ModKey.
    /// </summary>
    public static bool TryResolveByEditorId(
        string identifier,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        ref List<IOutfitGetter>? cachedOutfits,
        [NotNullWhen(true)] out IOutfitGetter? outfit)
    {
        outfit = null;

        if (!FormKeyHelper.TryParseEditorIdReference(identifier, out var modKey, out var editorId))
            return false;

        cachedOutfits ??= linkCache.PriorityOrder.WinningOverrides<IOutfitGetter>().ToList();

        var query = cachedOutfits
            .Where(o => string.Equals(o.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

        if (modKey.HasValue)
            query = query.Where(o => o.FormKey.ModKey == modKey.Value);

        outfit = query.FirstOrDefault();
        return outfit != null;
    }

    /// <summary>
    ///     Gathers armor pieces from an outfit for preview, including those nested within leveled lists.
    ///     For leveled lists, respects the "Use All" flag - if set, all entries are used;
    ///     otherwise only the first entry is taken to represent the random selection.
    /// </summary>
    public static List<ArmorRecordViewModel> GatherArmorPieces(
        IOutfitGetter outfit,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var pieces = new List<ArmorRecordViewModel>();
        var visited = new HashSet<FormKey>();

        var items = outfit.Items ?? [];

        foreach (var itemLink in items)
        {
            if (itemLink == null)
                continue;

            var targetKeyNullable = itemLink.FormKeyNullable;
            if (!targetKeyNullable.HasValue || targetKeyNullable.Value == FormKey.Null)
                continue;

            GatherArmorsFromItem(targetKeyNullable.Value, linkCache, pieces, visited);
        }

        return pieces;
    }

    /// <summary>
    ///     Recursively gathers armor pieces from an item, traversing leveled lists as needed.
    /// </summary>
    private static void GatherArmorsFromItem(
        FormKey itemFormKey,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        List<ArmorRecordViewModel> pieces,
        HashSet<FormKey> visited)
    {
        if (!visited.Add(itemFormKey))
            return;

        if (!linkCache.TryResolve<IItemGetter>(itemFormKey, out var itemRecord))
            return;

        switch (itemRecord)
        {
            case IArmorGetter armor:
                pieces.Add(new ArmorRecordViewModel(armor, linkCache));
                break;

            case ILeveledItemGetter leveledItem:
                GatherArmorsFromLeveledItem(leveledItem, linkCache, pieces, visited);
                break;
        }
    }

    /// <summary>
    ///     Gathers armor pieces from a leveled item list.
    ///     If the leveled list has "Use All" flag, all entries are traversed.
    ///     Otherwise, only the first entry is taken to represent what would be randomly selected.
    /// </summary>
    private static void GatherArmorsFromLeveledItem(
        ILeveledItemGetter leveledItem,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        List<ArmorRecordViewModel> pieces,
        HashSet<FormKey> visited)
    {
        var entries = leveledItem.Entries;
        if (entries == null || entries.Count == 0)
            return;

        var useAll = leveledItem.Flags.HasFlag(LeveledItem.Flag.UseAll);

        if (useAll)
        {
            foreach (var entry in entries)
            {
                if (TryGetEntryFormKey(entry, out var formKey))
                    GatherArmorsFromItem(formKey, linkCache, pieces, visited);
            }
        }
        else
        {
            var firstEntry = entries.FirstOrDefault();
            if (TryGetEntryFormKey(firstEntry, out var formKey))
                GatherArmorsFromItem(formKey, linkCache, pieces, visited);
        }
    }

    private static bool TryGetEntryFormKey(ILeveledItemEntryGetter? entry, out FormKey formKey)
    {
        formKey = FormKey.Null;

        var data = entry?.Data;
        if (data == null)
            return false;

        var refFormKey = data.Reference.FormKeyNullable;
        if (!refFormKey.HasValue || refFormKey.Value == FormKey.Null)
            return false;

        formKey = refFormKey.Value;
        return true;
    }
}
