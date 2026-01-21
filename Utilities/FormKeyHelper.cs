using System.Globalization;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

public static class FormKeyHelper
{
    private static readonly string[] _modKeyExtensions = [".esp", ".esm", ".esl"];

    public static string Format(FormKey formKey) => $"{formKey.ModKey.FileName}|{formKey.ID:X8}";

    public static bool IsModKeyFileName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
               text.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
               text.EndsWith(".esl", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeFormId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return trimmed.Length is >= 1 and <= 8 && trimmed.All(char.IsAsciiHexDigit);
    }

    public static int FindModKeyEnd(string text)
    {
        foreach (var ext in _modKeyExtensions)
        {
            var idx = text.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return idx + ext.Length;
            }
        }

        return -1;
    }

    public static string StripHexPrefix(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var trimmed = text.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed[2..] : trimmed;
    }

    public static bool TryParse(string text, out FormKey formKey)
    {
        formKey = FormKey.Null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        string modPart;
        string formIdPart;

        if (trimmed.Contains('|'))
        {
            var parts = trimmed.Split('|', 2);
            modPart = parts[0].Trim();
            formIdPart = parts[1].Trim();
        }
        else if (trimmed.Contains('~'))
        {
            var parts = trimmed.Split('~', 2);
            formIdPart = parts[0].Trim();
            modPart = parts[1].Trim();
        }
        else
        {
            return false;
        }

        if (!TryParseModKey(modPart, out var modKey))
        {
            return false;
        }

        if (!TryParseFormId(formIdPart, out var id))
        {
            return false;
        }

        formKey = new FormKey(modKey, id);
        return true;
    }

    public static bool TryParseModKey(string input, out ModKey modKey)
    {
        try
        {
            modKey = ModKey.FromNameAndExtension(input);
            return true;
        }
        catch
        {
            modKey = ModKey.Null;
            return false;
        }
    }

    public static bool TryParseFormId(string text, out uint id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return uint.TryParse(trimmed, NumberStyles.HexNumber, null, out id);
    }

    public static bool TryParseEditorIdReference(string identifier, out ModKey? modKey, out string editorId)
    {
        modKey = null;
        editorId = string.Empty;

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        var trimmed = identifier.Trim();
        string? modCandidate = null;
        string? editorCandidate;

        var pipeIndex = trimmed.IndexOf('|');
        var tildeIndex = trimmed.IndexOf('~');

        if (pipeIndex >= 0)
        {
            var firstPart = trimmed[..pipeIndex].Trim();
            var secondPart = trimmed[(pipeIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(secondPart) && TryParseModKey(secondPart, out var modFromSecond))
            {
                modKey = modFromSecond;
                editorCandidate = firstPart;
            }
            else if (!string.IsNullOrWhiteSpace(firstPart) && TryParseModKey(firstPart, out var modFromFirst))
            {
                modKey = modFromFirst;
                editorCandidate = secondPart;
            }
            else
            {
                editorCandidate = firstPart;
                modCandidate = secondPart;
            }
        }
        else if (tildeIndex >= 0)
        {
            var firstPart = trimmed[..tildeIndex].Trim();
            var secondPart = trimmed[(tildeIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(secondPart) && TryParseModKey(secondPart, out var modFromSecond))
            {
                modKey = modFromSecond;
                editorCandidate = firstPart;
            }
            else if (!string.IsNullOrWhiteSpace(firstPart) && TryParseModKey(firstPart, out var modFromFirst))
            {
                modKey = modFromFirst;
                editorCandidate = secondPart;
            }
            else
            {
                editorCandidate = firstPart;
                modCandidate = secondPart;
            }
        }
        else
        {
            editorCandidate = trimmed;
        }

        if (!modKey.HasValue && !string.IsNullOrWhiteSpace(modCandidate) &&
            TryParseModKey(modCandidate, out var parsedMod))
        {
            modKey = parsedMod;
        }

        if (string.IsNullOrWhiteSpace(editorCandidate))
        {
            return false;
        }

        editorId = editorCandidate;
        return true;
    }

    public static string FormatForSpid(FormKey formKey) => $"0x{formKey.ID:X}~{formKey.ModKey.FileName}";

    public static FormKey? ResolveOutfit(string identifier, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        if (TryParse(identifier, out var formKey))
        {
            return formKey;
        }

        var outfit = linkCache.WinningOverrides<IOutfitGetter>()
            .FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.EditorID) &&
                                 o.EditorID.Equals(identifier, StringComparison.OrdinalIgnoreCase));

        return outfit?.FormKey;
    }

    public static FormKey? ResolveOutfit(
        string identifier,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> _,
        IReadOnlyDictionary<string, FormKey> outfitByEditorId)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        if (TryParse(identifier, out var formKey))
        {
            return formKey;
        }

        return outfitByEditorId.TryGetValue(identifier, out var resolvedFormKey)
            ? resolvedFormKey
            : null;
    }

    public static IReadOnlyDictionary<string, FormKey> BuildOutfitEditorIdLookup(
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache) =>
        linkCache.WinningOverrides<IOutfitGetter>()
            .Where(o => !string.IsNullOrWhiteSpace(o.EditorID))
            .GroupBy(o => o.EditorID!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().FormKey, StringComparer.OrdinalIgnoreCase);
}
