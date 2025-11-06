using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Boutique.Models;
using Serilog;

namespace Boutique.Services;

public class DistributionDiscoveryService : IDistributionDiscoveryService
{
    private readonly ILogger _logger;

    public DistributionDiscoveryService(ILogger logger)
    {
        _logger = logger.ForContext<DistributionDiscoveryService>();
    }

    public async Task<IReadOnlyList<DistributionFile>> DiscoverAsync(string dataFolderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath) || !Directory.Exists(dataFolderPath))
        {
            _logger.Warning("Distribution discovery skipped because data path is invalid: {DataPath}", dataFolderPath);
            return Array.Empty<DistributionFile>();
        }

        return await Task.Run(() => DiscoverInternal(dataFolderPath, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<DistributionFile> DiscoverInternal(string dataFolderPath, CancellationToken cancellationToken)
    {
        var files = new ConcurrentBag<DistributionFile>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryParse(string path, DistributionFileType type)
        {
            if (!seenPaths.Add(path))
                return;

            var parsed = ParseDistributionFile(path, dataFolderPath, type);
            if (parsed != null)
            {
                files.Add(parsed);
            }
        }

        try
        {
            foreach (var spidFile in Directory.EnumerateFiles(dataFolderPath, "*_DISTR.ini", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryParse(spidFile, DistributionFileType.Spid);
            }

            foreach (var iniFile in Directory.EnumerateFiles(dataFolderPath, "*.ini", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seenPaths.Contains(iniFile))
                    continue;

                if (IsSkyPatcherIni(dataFolderPath, iniFile))
                {
                    TryParse(iniFile, DistributionFileType.SkyPatcher);
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

        if (!normalized.Contains(skyPatcherPath))
        {
            return false;
        }

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

            foreach (var raw in File.ReadLines(filePath, Encoding.UTF8))
            {
                lineNumber++;
                var trimmed = raw.Trim();
                DistributionLineKind kind;
                string? sectionName = currentSection;
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

                lines.Add(new DistributionLine(lineNumber, raw, kind, sectionName, key, value));
            }

            var relativePath = Path.GetRelativePath(dataFolderPath, filePath);

            return new DistributionFile(
                Path.GetFileName(filePath),
                filePath,
                relativePath,
                type,
                lines);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse distribution file {FilePath}", filePath);
            return null;
        }
    }
}
