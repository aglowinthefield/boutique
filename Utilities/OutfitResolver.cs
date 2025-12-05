using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Boutique.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

/// <summary>
/// Helper class for resolving outfits from various identifier formats.
/// </summary>
public static class OutfitResolver
{
    /// <summary>
    /// Tries to resolve an outfit from a string identifier (FormKey or EditorID format).
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
    /// Tries to resolve an outfit by EditorID, optionally filtering by ModKey.
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

        IEnumerable<IOutfitGetter> query = cachedOutfits
            .Where(o => string.Equals(o.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

        if (modKey.HasValue)
            query = query.Where(o => o.FormKey.ModKey == modKey.Value);

        outfit = query.FirstOrDefault();
        return outfit != null;
    }

    /// <summary>
    /// Gathers all armor pieces from an outfit.
    /// </summary>
    public static List<ArmorRecordViewModel> GatherArmorPieces(
        IOutfitGetter outfit,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var pieces = new List<ArmorRecordViewModel>();

        var items = outfit.Items ?? Array.Empty<IFormLinkGetter<IOutfitTargetGetter>>();

        foreach (var itemLink in items)
        {
            if (itemLink == null)
                continue;

            var targetKeyNullable = itemLink.FormKeyNullable;
            if (!targetKeyNullable.HasValue || targetKeyNullable.Value == FormKey.Null)
                continue;

            var targetKey = targetKeyNullable.Value;

            if (!linkCache.TryResolve<IItemGetter>(targetKey, out var itemRecord))
                continue;

            if (itemRecord is not IArmorGetter armor)
                continue;

            var vm = new ArmorRecordViewModel(armor, linkCache);
            pieces.Add(vm);
        }

        return pieces;
    }
}

