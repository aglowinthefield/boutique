using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Boutique.Models;
using Serilog;

namespace Boutique.Services;

public class DistributionDiscoveryService(ILogger logger)
{
    private readonly ILogger _logger = logger.ForContext<DistributionDiscoveryService>();

    public async Task<IReadOnlyList<DistributionFile>> DiscoverAsync(string dataFolderPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath) || !Directory.Exists(dataFolderPath))
        {
            _logger.Warning("Distribution discovery skipped because data path is invalid: {DataPath}", dataFolderPath);
            return [];
        }

        return await Task.Run(() => DiscoverInternal(dataFolderPath, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<DistributionFile> DiscoverInternal(string dataFolderPath, CancellationToken cancellationToken)
    {
        var files = new ConcurrentBag<DistributionFile>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        var spidFileCount = 0;
        var skyPatcherFileCount = 0;
        var parsedCount = 0;
        var skippedCount = 0;

        try
        {
            _logger.Debug("Starting distribution file discovery in {DataPath}", dataFolderPath);

            foreach (var spidFile in Directory.EnumerateFiles(dataFolderPath, "*_DISTR.ini", enumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                spidFileCount++;
                TryParse(spidFile, DistributionFileType.Spid);
            }

            _logger.Debug("Found {Count} SPID distribution files (*_DISTR.ini)", spidFileCount);

            var skyPatcherRoot = Path.Combine(dataFolderPath, "skse", "plugins", "SkyPatcher");
            if (Directory.Exists(skyPatcherRoot))
            {
                _logger.Debug("SkyPatcher directory exists: {Path}", skyPatcherRoot);
                foreach (var iniFile in Directory.EnumerateFiles(skyPatcherRoot, "*.ini*", enumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (seenPaths.Contains(iniFile))
                        continue;

                    if (IsSkyPatcherIni(dataFolderPath, iniFile))
                    {
                        skyPatcherFileCount++;
                        TryParse(iniFile, DistributionFileType.SkyPatcher);
                    }
                }
                _logger.Debug("Found {Count} SkyPatcher distribution files", skyPatcherFileCount);
            }
            else
            {
                _logger.Debug("SkyPatcher directory does not exist: {Path}", skyPatcherRoot);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Distribution discovery cancelled.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed while discovering distribution files.");
        }

        var result = files
            .OrderBy(f => f.Type)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.Information(
            "Distribution discovery complete: {TotalFound} files found ({SpidCount} SPID, {SkyPatcherCount} SkyPatcher), {ParsedCount} parsed successfully, {SkippedCount} skipped (no outfit distributions), {ResultCount} returned",
            spidFileCount + skyPatcherFileCount,
            spidFileCount,
            skyPatcherFileCount,
            parsedCount,
            skippedCount,
            result.Count);

        return result;

        void TryParse(string path, DistributionFileType type)
        {
            if (!seenPaths.Add(path))
                return;

            var parsed = ParseDistributionFile(path, dataFolderPath, type);
            if (parsed != null)
            {
                files.Add(parsed);
                parsedCount++;
                _logger.Debug("Successfully parsed {Type} file: {Path} ({OutfitCount} outfit distributions)", type, path, parsed.OutfitDistributionCount);
            }
            else
            {
                skippedCount++;
                _logger.Debug("Skipped {Type} file (no outfit distributions): {Path}", type, path);
            }
        }
    }

    private static bool IsSkyPatcherIni(string dataFolderPath, string iniFile)
    {
        var relativePath = Path.GetRelativePath(dataFolderPath, iniFile);
        var normalized = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToLowerInvariant();

        var skyPatcherPath = Path.Combine("skse", "plugins", "skypatcher")
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToLowerInvariant();

        if (!normalized.Contains(skyPatcherPath)) return false;

        var fileName = Path.GetFileName(iniFile);
        return !string.Equals(fileName, "SkyPatcher.ini", StringComparison.OrdinalIgnoreCase);
    }

    private DistributionFile? ParseDistributionFile(string filePath, string dataFolderPath, DistributionFileType type)
    {
        try
        {
            _logger.Debug("Parsing {Type} distribution file: {Path}", type, filePath);
            var lines = new List<DistributionLine>();
            var currentSection = string.Empty;
            var lineNumber = 0;

            var outfitCount = 0;
            var totalLines = 0;

            foreach (var raw in File.ReadLines(filePath, Encoding.UTF8))
            {
                lineNumber++;
                totalLines++;
                var trimmed = raw.Trim();
                DistributionLineKind kind;
                var sectionName = currentSection;
                string? key = null;
                string? value = null;

                if (string.IsNullOrEmpty(trimmed))
                {
                    kind = DistributionLineKind.Blank;
                }
                else if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                {
                    kind = DistributionLineKind.Comment;
                }
                else if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
                {
                    kind = DistributionLineKind.Section;
                    currentSection = trimmed[1..^1].Trim();
                    sectionName = currentSection;
                }
                else
                {
                    var equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex >= 0)
                    {
                        kind = DistributionLineKind.KeyValue;
                        key = trimmed[..equalsIndex].Trim();
                        value = trimmed[(equalsIndex + 1)..].Trim();
                    }
                    else
                    {
                        kind = DistributionLineKind.Other;
                    }
                }

                var isOutfitDistribution = IsOutfitDistributionLine(type, kind, trimmed);
                IReadOnlyList<string> outfitFormKeys = Array.Empty<string>();

                if (isOutfitDistribution)
                {
                    outfitCount++;
                    outfitFormKeys = ExtractOutfitFormKeys(type, trimmed);
                    _logger.Debug("Found outfit distribution line {LineNumber} in {Path}: {Line}", lineNumber, filePath, trimmed);
                }

                lines.Add(new DistributionLine(lineNumber, raw, kind, sectionName, key, value, isOutfitDistribution,
                    outfitFormKeys));
            }

            var relativePath = Path.GetRelativePath(dataFolderPath, filePath);

            _logger.Debug("Parsed {Type} file {Path}: {TotalLines} total lines, {OutfitCount} outfit distributions", 
                type, filePath, totalLines, outfitCount);

            if (outfitCount == 0)
            {
                _logger.Debug("Skipping {Type} file {Path} - no outfit distributions found", type, filePath);
                return null;
            }

            return new DistributionFile(
                Path.GetFileName(filePath),
                filePath,
                relativePath,
                type,
                lines,
                outfitCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse distribution file {FilePath}", filePath);
            return null;
        }
    }

    private static bool IsOutfitDistributionLine(DistributionFileType type, DistributionLineKind kind, string trimmed)
    {
        if (kind is DistributionLineKind.Comment or DistributionLineKind.Blank)
            return false;

        return type switch
        {
            DistributionFileType.Spid => IsSpidOutfitLine(trimmed),
            DistributionFileType.SkyPatcher => IsSkyPatcherOutfitLine(trimmed),
            _ => false
        };
    }

    private static bool IsSpidOutfitLine(string trimmed)
    {
        if (!trimmed.StartsWith("Outfit", StringComparison.OrdinalIgnoreCase) || trimmed.Length <= 6)
            return false;

        var remainder = trimmed[6..].TrimStart();
        return remainder.Length > 0 && remainder[0] == '=';
    }

    private static bool IsSkyPatcherOutfitLine(string trimmed)
    {
        return trimmed.IndexOf("filterByOutfits=", StringComparison.OrdinalIgnoreCase) >= 0 ||
               trimmed.IndexOf("outfitDefault=", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static IReadOnlyList<string> ExtractOutfitFormKeys(DistributionFileType type, string trimmed)
    {
        return type switch
        {
            DistributionFileType.Spid => ExtractSpidOutfitKeys(trimmed),
            DistributionFileType.SkyPatcher => ExtractSkyPatcherOutfitKeys(trimmed),
            _ => Array.Empty<string>()
        };
    }

    internal static IReadOnlyList<string> ExtractSpidOutfitKeys(string trimmed)
    {
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex < 0)
            return Array.Empty<string>();

        var valuePortion = trimmed[(equalsIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(valuePortion))
            return Array.Empty<string>();

        // SPID format: OutfitIdentifier|StringFilters|FormFilters|LevelFilters|Traits|IdxOrCount|Chance
        // The identifier can be:
        // - EditorID: 1_Obi_Druchii
        // - FormKey with tilde: 0x12345~Plugin.esp
        // - FormKey with pipe: Plugin.esp|0x12345
        // We need to extract just the outfit identifier(s), not the filter parameters.
        
        // Handle comma-separated multiple outfit identifiers
        var tokens = valuePortion.Split([','], StringSplitOptions.RemoveEmptyEntries);
        var results = new List<string>();
        
        foreach (var token in tokens)
        {
            var outfitId = ExtractSpidOutfitIdentifier(token.Trim());
            if (!string.IsNullOrWhiteSpace(outfitId))
                results.Add(outfitId);
        }
        
        return results;
    }

    /// <summary>
    /// Extracts the outfit identifier from a SPID value token, stripping any filter parameters.
    /// </summary>
    internal static string ExtractSpidOutfitIdentifier(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        var cleaned = RemoveInlineComment(token);
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        // Check for tilde-format FormKey: 0x12345~Plugin.esp or 0x12345~Plugin.esp|filters
        var tildeIndex = cleaned.IndexOf('~');
        if (tildeIndex >= 0)
        {
            // Format: FormID~ModKey|filters
            // We need FormID~ModKey
            var afterTilde = cleaned[(tildeIndex + 1)..];
            var pipeAfterMod = afterTilde.IndexOf('|');
            
            if (pipeAfterMod >= 0)
            {
                // Check if there's a valid mod extension before the pipe
                var modPart = afterTilde[..pipeAfterMod];
                if (IsModKeyFileName(modPart))
                {
                    // This is FormID~ModKey|filters, extract FormID~ModKey
                    return cleaned[..(tildeIndex + 1 + pipeAfterMod)];
                }
            }
            
            // No pipe after mod, or the part after tilde to pipe isn't a valid mod
            // Check if the whole part after tilde is a mod key
            var endOfModKey = FindEndOfModKey(afterTilde);
            if (endOfModKey > 0)
                return cleaned[..(tildeIndex + 1 + endOfModKey)];
            
            // Fallback: return everything up to first pipe after tilde
            return pipeAfterMod >= 0 ? cleaned[..(tildeIndex + 1 + pipeAfterMod)] : cleaned;
        }

        // Check for pipe-format FormKey: Plugin.esp|0x12345|filters
        var pipeIndex = cleaned.IndexOf('|');
        if (pipeIndex >= 0)
        {
            var firstPart = cleaned[..pipeIndex];
            
            // If first part is a mod key (ends with .esp/.esm/.esl), then second part is FormID
            if (IsModKeyFileName(firstPart))
            {
                var afterFirstPipe = cleaned[(pipeIndex + 1)..];
                var secondPipe = afterFirstPipe.IndexOf('|');
                
                if (secondPipe >= 0)
                {
                    // Check if the part between pipes looks like a FormID (hex number)
                    var potentialFormId = afterFirstPipe[..secondPipe];
                    if (LooksLikeFormId(potentialFormId))
                    {
                        // This is ModKey|FormID|filters
                        return cleaned[..(pipeIndex + 1 + secondPipe)];
                    }
                }
                else
                {
                    // Only one pipe: ModKey|FormID (no filters)
                    return cleaned;
                }
            }
            
            // First part is not a mod key, so this is EditorID|filters format
            // Return just the EditorID
            return firstPart;
        }

        // No tilde or pipe - just an EditorID
        return cleaned;
    }

    /// <summary>
    /// Checks if a string looks like a mod key file name (ends with .esp, .esm, or .esl).
    /// </summary>
    internal static bool IsModKeyFileName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        
        return text.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
               text.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
               text.EndsWith(".esl", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the end index of a mod key in a string (looking for .esp/.esm/.esl).
    /// </summary>
    private static int FindEndOfModKey(string text)
    {
        var extensions = new[] { ".esp", ".esm", ".esl" };
        foreach (var ext in extensions)
        {
            var idx = text.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return idx + ext.Length;
        }
        return -1;
    }

    /// <summary>
    /// Checks if a string looks like a FormID (hex number with optional 0x prefix).
    /// </summary>
    internal static bool LooksLikeFormId(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[2..];
        
        // FormIDs are hex numbers, typically 6-8 characters
        return trimmed.Length >= 1 && trimmed.Length <= 8 && 
               trimmed.All(c => char.IsAsciiHexDigit(c));
    }

    internal static IReadOnlyList<string> ExtractSkyPatcherOutfitKeys(string trimmed)
    {
        var keys = new List<string>();
        
        // Extract from filterByOutfits= syntax
        ExtractFromMarker(trimmed, "filterByOutfits=", keys);
        
        // Extract from outfitDefault= syntax
        ExtractFromMarker(trimmed, "outfitDefault=", keys);

        return keys;
    }

    private static void ExtractFromMarker(string trimmed, string marker, List<string> keys)
    {
        var startIndex = 0;

        while (true)
        {
            var index = trimmed.IndexOf(marker, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            index += marker.Length;
            var endIndex = trimmed.IndexOf(':', index);
            string segment = endIndex >= 0 ? trimmed[index..endIndex] : trimmed[index..];
            startIndex = endIndex >= 0 ? endIndex + 1 : trimmed.Length;

            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var tokens = segment.Split([','], StringSplitOptions.RemoveEmptyEntries);
            keys.AddRange(NormalizeFormKeyTokens(tokens));
        }
    }

    private static IReadOnlyList<string> NormalizeFormKeyTokens(IEnumerable<string> tokens)
    {
        var results = new List<string>();

        foreach (var token in tokens)
        {
            if (!TryNormalizeFormKeyToken(token, out var normalized))
                continue;

            results.Add(normalized);
        }

        return results;
    }

    private static bool TryNormalizeFormKeyToken(string token, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var cleaned = RemoveInlineComment(token.Trim());
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        if (TryExtractFormKeyParts(cleaned, out var modPart, out var formIdPart) &&
            !string.IsNullOrWhiteSpace(modPart) &&
            !string.IsNullOrWhiteSpace(formIdPart))
        {
            normalized = $"{modPart}|{formIdPart}";
            return true;
        }

        normalized = cleaned;
        return true;
    }

    private static bool TryExtractFormKeyParts(string text, out string modPart, out string formIdPart)
    {
        modPart = string.Empty;
        formIdPart = string.Empty;

        var tildeIndex = text.IndexOf('~');
        if (tildeIndex >= 0)
        {
            formIdPart = text[..tildeIndex].Trim();
            var remainder = text[(tildeIndex + 1)..].Trim();
            if (string.IsNullOrEmpty(remainder) || string.IsNullOrEmpty(formIdPart))
                return false;

            var pipeIndex = remainder.IndexOf('|');
            modPart = pipeIndex >= 0 ? remainder[..pipeIndex].Trim() : remainder;
            formIdPart = formIdPart.Trim();
            modPart = modPart.Trim();

            return !string.IsNullOrEmpty(modPart) && !string.IsNullOrEmpty(formIdPart);
        }

        var firstPipe = text.IndexOf('|');
        if (firstPipe < 0)
            return false;

        modPart = text[..firstPipe].Trim();
        var remainderPart = text[(firstPipe + 1)..].Trim();
        if (string.IsNullOrEmpty(modPart) || string.IsNullOrEmpty(remainderPart))
            return false;

        var secondPipe = remainderPart.IndexOf('|');
        formIdPart = secondPipe >= 0 ? remainderPart[..secondPipe].Trim() : remainderPart;

        formIdPart = formIdPart.Trim();

        return !string.IsNullOrEmpty(modPart) && !string.IsNullOrEmpty(formIdPart);
    }

    private static string RemoveInlineComment(string text)
    {
        var commentIndex = text.IndexOfAny(new[] { ';', '#' });
        if (commentIndex >= 0)
            text = text[..commentIndex];

        return text.Trim();
    }
}