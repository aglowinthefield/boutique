using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using Boutique.Models;
using Serilog;

namespace Boutique.Services;

public class DistributionDiscoveryService(ILogger logger) : IDistributionDiscoveryService
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

        try
        {
            foreach (var spidFile in Directory.EnumerateFiles(dataFolderPath, "*_DISTR.ini", enumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryParse(spidFile, DistributionFileType.Spid);
            }

            var skyPatcherRoot = Path.Combine(dataFolderPath, "skse", "plugins", "SkyPatcher");
            if (Directory.Exists(skyPatcherRoot))
            {
                foreach (var iniFile in Directory.EnumerateFiles(skyPatcherRoot, "*.ini", enumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (seenPaths.Contains(iniFile))
                        continue;

                    if (IsSkyPatcherIni(dataFolderPath, iniFile)) TryParse(iniFile, DistributionFileType.SkyPatcher);
                }
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

        return files
            .OrderBy(f => f.Type)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        void TryParse(string path, DistributionFileType type)
        {
            if (!seenPaths.Add(path))
                return;

            var parsed = ParseDistributionFile(path, dataFolderPath, type);
            if (parsed != null) files.Add(parsed);
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
            var lines = new List<DistributionLine>();
            var currentSection = string.Empty;
            var lineNumber = 0;

            var outfitCount = 0;

            foreach (var raw in File.ReadLines(filePath, Encoding.UTF8))
            {
                lineNumber++;
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
                }

                lines.Add(new DistributionLine(lineNumber, raw, kind, sectionName, key, value, isOutfitDistribution,
                    outfitFormKeys));
            }

            var relativePath = Path.GetRelativePath(dataFolderPath, filePath);

            if (outfitCount == 0)
                return null;

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
        return trimmed.IndexOf("filterByOutfits=", StringComparison.OrdinalIgnoreCase) >= 0;
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

    private static IReadOnlyList<string> ExtractSpidOutfitKeys(string trimmed)
    {
        var equalsIndex = trimmed.IndexOf('=');
        if (equalsIndex < 0)
            return Array.Empty<string>();

        var valuePortion = trimmed[(equalsIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(valuePortion))
            return Array.Empty<string>();

        var tokens = valuePortion.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        return NormalizeFormKeyTokens(tokens);
    }

    private static IReadOnlyList<string> ExtractSkyPatcherOutfitKeys(string trimmed)
    {
        var keys = new List<string>();
        const string marker = "filterByOutfits=";
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

            var tokens = segment.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            keys.AddRange(NormalizeFormKeyTokens(tokens));
        }

        return keys;
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
