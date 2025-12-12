using Boutique.Models;
using Boutique.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

/// <summary>
/// Utility class for parsing distribution file lines to extract NPC FormKeys and outfit information.
/// Handles both SPID and SkyPatcher formats, delegating SPID parsing to SpidLineParser.
/// </summary>
public static class DistributionLineParser
{
    /// <summary>
    /// Extracts NPC FormKeys from a distribution line.
    /// </summary>
    /// <param name="file">The distribution file containing the line</param>
    /// <param name="line">The distribution line to parse</param>
    /// <param name="linkCache">LinkCache for resolving NPCs by EditorID or name</param>
    /// <param name="npcByEditorId">Optional pre-built dictionary of NPCs by EditorID (for SPID files)</param>
    /// <param name="npcByName">Optional pre-built dictionary of NPCs by name (for SPID files)</param>
    /// <returns>List of NPC FormKeys found in the line</returns>
    public static List<FormKey> ExtractNpcFormKeysFromLine(
        DistributionFileViewModel file,
        DistributionLine line,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Dictionary<string, INpcGetter>? npcByEditorId = null,
        Dictionary<string, INpcGetter>? npcByName = null)
    {
        if (file.TypeDisplay == "SkyPatcher")
        {
            return ExtractNpcFormKeysFromSkyPatcherLine(line.RawText);
        }
        
        if (file.TypeDisplay == "SPID")
        {
            return ExtractNpcFormKeysFromSpidLine(line.RawText, linkCache, npcByEditorId, npcByName);
        }

        return [];
    }
    
    /// <summary>
    /// Extracts NPC FormKeys from a SkyPatcher distribution line.
    /// </summary>
    private static List<FormKey> ExtractNpcFormKeysFromSkyPatcherLine(string rawText)
    {
        var results = new List<FormKey>();
        
        // SkyPatcher format: filterByNpcs=ModKey|FormID,ModKey|FormID:outfitDefault=ModKey|FormID
        var trimmed = rawText.Trim();
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
        
        return results;
    }
    
    /// <summary>
    /// Extracts NPC FormKeys from a SPID distribution line using SpidLineParser.
    /// </summary>
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
        
        // Get specific NPC identifiers from the parsed filter
        var npcIdentifiers = SpidLineParser.GetSpecificNpcIdentifiers(filter);
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

        var modKeyString = trimmed[..pipeIndex].Trim();
        var formIdString = trimmed[(pipeIndex + 1)..].Trim();

        if (!ModKey.TryFromNameAndExtension(modKeyString, out var modKey))
            return null;

        formIdString = formIdString.Replace("0x", "").Replace("0X", "");
        if (!uint.TryParse(formIdString, System.Globalization.NumberStyles.HexNumber, null, out var formId))
            return null;

        return new FormKey(modKey, formId);
    }
}

