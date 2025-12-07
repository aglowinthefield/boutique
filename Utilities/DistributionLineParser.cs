using System.Collections.Generic;
using System.Linq;
using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

/// <summary>
/// Utility class for parsing distribution file lines to extract NPC FormKeys and outfit information.
/// </summary>
public static class DistributionLineParser
{
    /// <summary>
    /// Extracts NPC FormKeys from a distribution line.
    /// </summary>
    /// <param name="file">The distribution file containing the line</param>
    /// <param name="line">The distribution line to parse</param>
    /// <param name="linkCache">LinkCache for resolving NPCs by EditorID or name</param>
    /// <returns>List of NPC FormKeys found in the line</returns>
    public static List<FormKey> ExtractNpcFormKeysFromLine(
        DistributionFileViewModel file,
        DistributionLine line,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var results = new List<FormKey>();

        // Build lookup dictionaries for NPC resolution (only if needed for SPID)
        Dictionary<string, INpcGetter>? npcByEditorId = null;
        Dictionary<string, INpcGetter>? npcByName = null;

        if (file.TypeDisplay == "SPID")
        {
            var allNpcs = linkCache.WinningOverrides<INpcGetter>().ToList();
            npcByEditorId = allNpcs
                .Where(n => !string.IsNullOrWhiteSpace(n.EditorID))
                .GroupBy(n => n.EditorID!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            npcByName = allNpcs
                .Where(n => !string.IsNullOrWhiteSpace(n.Name?.String))
                .GroupBy(n => n.Name!.String!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        if (file.TypeDisplay == "SkyPatcher")
        {
            // SkyPatcher format: filterByNpcs=ModKey|FormID,ModKey|FormID:outfitDefault=ModKey|FormID
            var trimmed = line.RawText.Trim();
            var filterByNpcsIndex = trimmed.IndexOf("filterByNpcs=", StringComparison.OrdinalIgnoreCase);
            
            if (filterByNpcsIndex >= 0)
            {
                var npcStart = filterByNpcsIndex + "filterByNpcs=".Length;
                var npcEnd = trimmed.IndexOf(':', npcStart);
                
                if (npcEnd > npcStart)
                {
                    var npcString = trimmed.Substring(npcStart, npcEnd - npcStart);
                    
                    foreach (var npcPart in npcString.Split(','))
                    {
                        var formKey = TryParseFormKey(npcPart.Trim());
                        if (formKey.HasValue)
                        {
                            results.Add(formKey.Value);
                        }
                    }
                }
            }
        }
        else if (file.TypeDisplay == "SPID")
        {
            // SPID format: Outfit = 0x800~ModKey|EditorID[,EditorID,...]
            var trimmed = line.RawText.Trim();
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex < 0) return results;

            var valuePart = trimmed.Substring(equalsIndex + 1).Trim();
            var tildeIndex = valuePart.IndexOf('~');
            if (tildeIndex < 0) return results;

            var rest = valuePart.Substring(tildeIndex + 1).Trim();
            var pipeIndex = rest.IndexOf('|');
            if (pipeIndex < 0) return results;

            var editorIdsString = rest.Substring(pipeIndex + 1).Trim();
            var npcIdentifiers = editorIdsString
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            foreach (var identifier in npcIdentifiers)
            {
                INpcGetter? npc = null;
                if (npcByEditorId != null && npcByEditorId.TryGetValue(identifier, out var npcById))
                {
                    npc = npcById;
                }
                else if (npcByName != null && npcByName.TryGetValue(identifier, out var npcByNameMatch))
                {
                    npc = npcByNameMatch;
                }

                if (npc != null)
                {
                    results.Add(npc.FormKey);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Extracts outfit name from a distribution line.
    /// </summary>
    /// <param name="line">The distribution line to parse</param>
    /// <param name="linkCache">LinkCache for resolving outfit FormKeys</param>
    /// <returns>The outfit's EditorID or FormKey string, or null if not found</returns>
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

    /// <summary>
    /// Tries to parse a FormKey from a string in the format "ModKey|FormID" or "ModKey|0xFormID".
    /// </summary>
    /// <param name="text">The text to parse</param>
    /// <returns>The parsed FormKey, or null if parsing failed</returns>
    public static FormKey? TryParseFormKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        var pipeIndex = trimmed.IndexOf('|');
        if (pipeIndex < 0)
            return null;

        var modKeyString = trimmed.Substring(0, pipeIndex).Trim();
        var formIdString = trimmed.Substring(pipeIndex + 1).Trim();

        if (!ModKey.TryFromNameAndExtension(modKeyString, out var modKey))
            return null;

        formIdString = formIdString.Replace("0x", "").Replace("0X", "");
        if (!uint.TryParse(formIdString, System.Globalization.NumberStyles.HexNumber, null, out var formId))
            return null;

        return new FormKey(modKey, formId);
    }
}

