using System.Diagnostics.CodeAnalysis;
using System.Text;
using Boutique.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

/// <summary>
///   Helper class for resolving outfits from various identifier formats.
/// </summary>
public static class OutfitResolver
{
  /// <summary>
  ///   Tries to resolve an outfit from a string identifier (FormKey or EditorID format).
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
  ///   Tries to resolve an outfit by EditorID, optionally filtering by ModKey.
  /// </summary>
  public static bool TryResolveByEditorId(
    string identifier,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    ref List<IOutfitGetter>? cachedOutfits,
    [NotNullWhen(true)] out IOutfitGetter? outfit)
  {
    outfit = null;

    if (!FormKeyHelper.TryParseEditorIdReference(identifier, out var modKey, out var editorId))
    {
      return false;
    }

    cachedOutfits ??= linkCache.PriorityOrder.WinningOverrides<IOutfitGetter>().ToList();

    var query = cachedOutfits
      .Where(o => string.Equals(o.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

    if (modKey.HasValue)
    {
      query = query.Where(o => o.FormKey.ModKey == modKey.Value);
    }

    outfit = query.FirstOrDefault();
    return outfit != null;
  }

  /// <summary>
  ///   Gathers armor pieces from an outfit for preview, including those nested within leveled lists.
  ///   For leveled lists without "Use All", picks a random entry using the provided seed.
  /// </summary>
  /// <param name="outfit">The outfit to gather armor from.</param>
  /// <param name="linkCache">The link cache for resolving records.</param>
  /// <param name="randomSeed">Optional seed for random selection. If null, uses first entry for consistency.</param>
  /// <returns>A result containing armor pieces and whether the outfit contains randomized items.</returns>
  public static OutfitArmorResult GatherArmorPieces(
    IOutfitGetter outfit,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    int? randomSeed = null)
  {
    var pieces = new List<ArmorRecordViewModel>();
    var visited = new HashSet<FormKey>();
    var containsLeveledItems = false;
    var random = randomSeed.HasValue ? new Random(randomSeed.Value) : null;

    var items = outfit.Items ?? [];

    foreach (var itemLink in items)
    {
      var targetKeyNullable = itemLink.FormKeyNullable;
      if (!targetKeyNullable.HasValue || targetKeyNullable.Value == FormKey.Null)
      {
        continue;
      }

      GatherArmorsFromItem(targetKeyNullable.Value, linkCache, pieces, visited, random, ref containsLeveledItems);
    }

    return new OutfitArmorResult(pieces, containsLeveledItems);
  }

  private static void GatherArmorsFromItem(
    FormKey itemFormKey,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<ArmorRecordViewModel> pieces,
    HashSet<FormKey> visited,
    Random? random,
    ref bool containsLeveledItems)
  {
    if (!visited.Add(itemFormKey))
    {
      return;
    }

    if (linkCache.TryResolve<IArmorGetter>(itemFormKey, out var armor))
    {
      pieces.Add(new ArmorRecordViewModel(armor, linkCache));
      return;
    }

    if (linkCache.TryResolve<ILeveledItemGetter>(itemFormKey, out var leveledItem))
    {
      GatherArmorsFromLeveledItem(leveledItem, linkCache, pieces, visited, random, ref containsLeveledItems);
      return;
    }

    if (linkCache.TryResolve<IFormListGetter>(itemFormKey, out var formList))
    {
      GatherArmorsFromFormList(formList, linkCache, pieces, visited, random, ref containsLeveledItems);
    }
  }

  private static void GatherArmorsFromFormList(
    IFormListGetter formList,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<ArmorRecordViewModel> pieces,
    HashSet<FormKey> visited,
    Random? random,
    ref bool containsLeveledItems)
  {
    var items = formList.Items;
    if (items.Count == 0)
    {
      return;
    }

    containsLeveledItems = true;

    foreach (var itemLink in items)
    {
      var formKeyNullable = itemLink.FormKeyNullable;
      if (!formKeyNullable.HasValue || formKeyNullable.Value.IsNull)
      {
        continue;
      }

      GatherArmorsFromItem(formKeyNullable.Value, linkCache, pieces, visited, random, ref containsLeveledItems);
    }
  }

  private static void GatherArmorsFromLeveledItem(
    ILeveledItemGetter leveledItem,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    List<ArmorRecordViewModel> pieces,
    HashSet<FormKey> visited,
    Random? random,
    ref bool containsLeveledItems)
  {
    var entries = leveledItem.Entries;
    if (entries == null || entries.Count == 0)
    {
      return;
    }

    var useAll = leveledItem.Flags.HasFlag(LeveledItem.Flag.UseAll);
    var calculateEach = leveledItem.Flags.HasFlag(LeveledItem.Flag.CalculateForEachItemInCount);

    if (useAll)
    {
      foreach (var entry in entries)
      {
        if (!TryGetEntryFormKey(entry, out var formKey))
        {
          continue;
        }

        var count = entry?.Data?.Count ?? 1;
        if (calculateEach && count > 1)
        {
          containsLeveledItems = true;
          for (var i = 0; i < count; i++)
          {
            var itemVisited = new HashSet<FormKey>(visited);
            GatherArmorsFromItem(formKey, linkCache, pieces, itemVisited, random, ref containsLeveledItems);
          }
        }
        else
        {
          GatherArmorsFromItem(formKey, linkCache, pieces, visited, random, ref containsLeveledItems);
        }
      }
    }
    else
    {
      containsLeveledItems = true;

      var validEntries = entries.Where(e => TryGetEntryFormKey(e, out _)).ToList();
      if (validEntries.Count == 0)
      {
        return;
      }

      var selectedEntry = random != null
        ? validEntries[random.Next(validEntries.Count)]
        : validEntries[0];

      if (!TryGetEntryFormKey(selectedEntry, out var formKey))
      {
        return;
      }

      var count = selectedEntry?.Data?.Count ?? 1;
      if (calculateEach && count > 1)
      {
        for (var i = 0; i < count; i++)
        {
          var itemVisited = new HashSet<FormKey>(visited);
          GatherArmorsFromItem(formKey, linkCache, pieces, itemVisited, random, ref containsLeveledItems);
        }
      }
      else
      {
        GatherArmorsFromItem(formKey, linkCache, pieces, visited, random, ref containsLeveledItems);
      }
    }
  }

  private static bool TryGetEntryFormKey(ILeveledItemEntryGetter? entry, out FormKey formKey)
  {
    formKey = FormKey.Null;

    var data = entry?.Data;
    if (data == null)
    {
      return false;
    }

    var refFormKey = data.Reference.FormKeyNullable;
    if (!refFormKey.HasValue || refFormKey.Value == FormKey.Null)
    {
      return false;
    }

    formKey = refFormKey.Value;
    return true;
  }

  public static OutfitTreeNode BuildOutfitTree(
    IOutfitGetter outfit,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    var root = new OutfitTreeNode(
      outfit.EditorID ?? outfit.FormKey.ToString(),
      OutfitTreeNodeType.Outfit,
      outfit.FormKey);

    var visited = new HashSet<FormKey>();
    var items = outfit.Items ?? [];

    foreach (var itemLink in items)
    {
      var targetKey = itemLink.FormKeyNullable;
      if (!targetKey.HasValue || targetKey.Value == FormKey.Null)
      {
        continue;
      }

      var childNode = BuildTreeNode(targetKey.Value, linkCache, visited);
      if (childNode != null)
      {
        root.Children.Add(childNode);
      }
    }

    return root;
  }

  private static OutfitTreeNode? BuildTreeNode(
    FormKey formKey,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    HashSet<FormKey> visited)
  {
    if (!visited.Add(formKey))
    {
      return null;
    }

    if (linkCache.TryResolve<IArmorGetter>(formKey, out var armor))
    {
      return new OutfitTreeNode(
        armor.EditorID ?? armor.FormKey.ToString(),
        OutfitTreeNodeType.Armor,
        armor.FormKey);
    }

    if (linkCache.TryResolve<ILeveledItemGetter>(formKey, out var leveledItem))
    {
      var flags = BuildLeveledItemFlagsString(leveledItem.Flags);
      var node = new OutfitTreeNode(
        leveledItem.EditorID ?? leveledItem.FormKey.ToString(),
        OutfitTreeNodeType.LeveledList,
        leveledItem.FormKey,
        flags);

      var entries = leveledItem.Entries;
      if (entries == null)
      {
        return node;
      }

      foreach (var entry in entries)
      {
        if (!TryGetEntryFormKey(entry, out var entryFormKey))
        {
          continue;
        }

        var childNode = BuildTreeNode(entryFormKey, linkCache, visited);
        if (childNode != null)
        {
          node.Children.Add(childNode);
        }
      }

      return node;
    }

    if (linkCache.TryResolve<IFormListGetter>(formKey, out var formList))
    {
      var node = new OutfitTreeNode(
        formList.EditorID ?? formList.FormKey.ToString(),
        OutfitTreeNodeType.FormList,
        formList.FormKey);

      var items = formList.Items;
      if (items != null)
      {
        foreach (var itemLink in items)
        {
          var itemFormKey = itemLink.FormKeyNullable;
          if (!itemFormKey.HasValue || itemFormKey.Value.IsNull)
          {
            continue;
          }

          var childNode = BuildTreeNode(itemFormKey.Value, linkCache, visited);
          if (childNode != null)
          {
            node.Children.Add(childNode);
          }
        }
      }

      return node;
    }

    return null;
  }

  public static string RenderOutfitTreeAsText(OutfitTreeNode root)
  {
    var sb = new StringBuilder();
    sb.AppendLine($"Outfit: {root.Name}");
    sb.AppendLine();

    if (root.Children.Count == 0)
    {
      sb.AppendLine("(No items)");
      return sb.ToString();
    }

    foreach (var child in root.Children)
    {
      RenderNodeAsText(child, sb, 0);
    }

    return sb.ToString();
  }

  private static void RenderNodeAsText(OutfitTreeNode node, StringBuilder sb, int depth)
  {
    var indent = new string(' ', depth * 2);
    var typeIndicator = node.NodeType switch
    {
      OutfitTreeNodeType.Armor => "",
      OutfitTreeNodeType.LeveledList => $" (LL{(string.IsNullOrEmpty(node.Flags) ? "" : $", {node.Flags}")})",
      OutfitTreeNodeType.FormList => " (FL)",
      _ => ""
    };

    sb.AppendLine($"{indent}- {node.Name}{typeIndicator}");

    foreach (var child in node.Children)
    {
      RenderNodeAsText(child, sb, depth + 1);
    }
  }

  private static string BuildLeveledItemFlagsString(LeveledItem.Flag flags)
  {
    var parts = new List<string>();

    if (flags.HasFlag(LeveledItem.Flag.UseAll))
    {
      parts.Add("Use All");
    }
    else
    {
      parts.Add("Random");
    }

    if (flags.HasFlag(LeveledItem.Flag.CalculateForEachItemInCount))
    {
      parts.Add("Each");
    }

    if (flags.HasFlag(LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer))
    {
      parts.Add("All Levels");
    }

    if (flags.HasFlag(LeveledItem.Flag.SpecialLoot))
    {
      parts.Add("Special");
    }

    return string.Join(", ", parts);
  }
}

public enum OutfitTreeNodeType
{
  Outfit,
  Armor,
  LeveledList,
  FormList
}

public sealed class OutfitTreeNode
{
  public OutfitTreeNode(string name, OutfitTreeNodeType nodeType, FormKey formKey, string? flags = null)
  {
    Name = name;
    NodeType = nodeType;
    FormKey = formKey;
    Flags = flags;
  }

  public string Name { get; }
  public OutfitTreeNodeType NodeType { get; }
  public FormKey FormKey { get; }
  public string? Flags { get; }
  public List<OutfitTreeNode> Children { get; } = [];
}

public sealed record OutfitArmorResult(
  List<ArmorRecordViewModel> ArmorPieces,
  bool ContainsLeveledItems);
