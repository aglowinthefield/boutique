using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

public static class DistributionLineParser
{
  public static List<FormKey> ExtractNpcFormKeysFromLine(
    DistributionFileViewModel file,
    DistributionLine line,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Dictionary<string, INpcGetter>? npcByEditorId = null,
    Dictionary<string, INpcGetter>? npcByName = null)
  {
    return file.TypeDisplay switch
    {
      "SkyPatcher" => ExtractNpcFormKeysFromSkyPatcherLine(line.RawText),
      "SPID"       => ExtractNpcFormKeysFromSpidLine(line.RawText, linkCache, npcByEditorId, npcByName),
      _            => []
    };
  }

  private static List<FormKey> ExtractNpcFormKeysFromSkyPatcherLine(string rawText)
  {
    var results = SkyPatcherSyntax.ParseFormKeys(rawText, "filterByNpcs");
    results.AddRange(SkyPatcherSyntax.ParseFormKeys(rawText, "filterByNpcsOr"));
    return results;
  }

  private static List<FormKey> ExtractNpcFormKeysFromSpidLine(
    string rawText,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Dictionary<string, INpcGetter>? npcByEditorId,
    Dictionary<string, INpcGetter>? npcByName)
  {
    var results = new List<FormKey>();

    // Use SpidLineParser for robust SPID parsing
    if (!SpidLineParser.TryParse(rawText, out var filter) || filter == null)
    {
      return results;
    }

    var npcIdentifiers = SpidLineParser.GetSpecificNpcIdentifiers(filter, linkCache);
    if (npcIdentifiers.Count == 0)
    {
      return results;
    }

    // Build lookup dictionaries if not provided
    if (npcByEditorId == null || npcByName == null)
    {
      var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
      npcByEditorId ??= allNpcs
                        .Where(n => !string.IsNullOrWhiteSpace(n.EditorID))
                        .GroupBy(n => n.EditorID!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
      npcByName ??= allNpcs
                    .Where(n => !string.IsNullOrWhiteSpace(n.Name?.String))
                    .GroupBy(n => n.Name!.String!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    // Resolve each NPC identifier to a FormKey
    foreach (var identifier in npcIdentifiers)
    {
      INpcGetter? npc = null;
      if (npcByEditorId.TryGetValue(identifier, out var npcById))
      {
        npc = npcById;
      }
      else if (npcByName.TryGetValue(identifier, out var npcByNameMatch))
      {
        npc = npcByNameMatch;
      }

      if (npc != null)
      {
        results.Add(npc.FormKey);
      }
    }

    return results;
  }

  public static bool LineTargetsAllNpcs(DistributionFileViewModel file, DistributionLine line)
  {
    return file.TypeDisplay switch
    {
      "SPID"       => SpidLineTargetsAllNpcs(line.RawText),
      "SkyPatcher" => SkyPatcherLineTargetsAllNpcs(line.RawText),
      _            => false
    };
  }

  private static bool SpidLineTargetsAllNpcs(string rawText)
  {
    if (!SpidLineParser.TryParse(rawText, out var filter) || filter == null)
    {
      return false;
    }

    return filter.TargetsAllNpcs;
  }

  private static bool SkyPatcherLineTargetsAllNpcs(string rawText)
  {
    var hasOutfitAssignment = SkyPatcherSyntax.HasFilter(rawText, "outfitDefault") ||
                              SkyPatcherSyntax.HasFilter(rawText, "outfitSleep");
    if (!hasOutfitAssignment)
    {
      return false;
    }

    var hasAnyNpcFilter =
      SkyPatcherSyntax.HasAnyVariant(rawText, "filterByNpcs") ||
      SkyPatcherSyntax.HasAnyVariant(rawText, "filterByFactions") ||
      SkyPatcherSyntax.HasAnyVariant(rawText, "filterByRaces") ||
      SkyPatcherSyntax.HasAnyVariant(rawText, "filterByKeywords") ||
      SkyPatcherSyntax.HasFilter(rawText, "filterByClass") ||
      SkyPatcherSyntax.HasAnyVariant(rawText, "filterByEditorIdContains") ||
      SkyPatcherSyntax.HasFilter(rawText, "filterByGender") ||
      SkyPatcherSyntax.HasFilter(rawText, "filterByDefaultOutfits") ||
      SkyPatcherSyntax.HasFilter(rawText, "filterByModNames");

    return !hasAnyNpcFilter;
  }

  public static string? ExtractOutfitNameFromLine(
    DistributionLine line,
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
  {
    foreach (var formKeyString in line.OutfitFormKeys)
    {
      var formKey = TryParseFormKey(formKeyString);
      if (formKey.HasValue && linkCache.TryResolve<IOutfitGetter>(formKey.Value, out var outfit))
      {
        return outfit.EditorID ?? outfit.FormKey.ToString();
      }
    }

    return null;
  }

  public static int ExtractChanceFromLine(DistributionFileViewModel file, DistributionLine line)
  {
    return file.TypeDisplay switch
    {
      "SPID" => ExtractChanceFromSpidLine(line.RawText),
      _      => 100
    };
  }

  private static int ExtractChanceFromSpidLine(string rawText)
  {
    if (!SpidLineParser.TryParse(rawText, out var filter) || filter == null)
    {
      return 100;
    }

    return filter.Chance;
  }

  private static FormKey? TryParseFormKey(string text) =>
    FormKeyHelper.TryParse(text, out var formKey) ? formKey : null;
}
