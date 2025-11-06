using System.Collections.Concurrent;
using System.IO;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Serilog;

namespace Boutique.Services;

public class GameAssetLocator : IGameAssetLocator
{
    private readonly IMutagenService _mutagenService;
    private readonly ILogger _logger;
    private readonly IFileSystem _fileSystem;

    private readonly object _sync = new();
    private string? _currentDataPath;
    private string _extractionRoot;

    private readonly Dictionary<ModKey, IReadOnlyList<CachedArchive>> _archivesByMod = new();
    private readonly ConcurrentDictionary<string, string> _extractedAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ModKey, IReadOnlyList<ModKey>> _mastersByMod = new();

    private static readonly GameRelease Release = GameRelease.SkyrimSE;
    private static readonly ModKey[] FallbackModKeys =
    {
        ModKey.FromNameAndExtension("Skyrim.esm"),
        ModKey.FromNameAndExtension("Update.esm"),
        ModKey.FromNameAndExtension("Dawnguard.esm"),
        ModKey.FromNameAndExtension("HearthFires.esm"),
        ModKey.FromNameAndExtension("Dragonborn.esm")
    };

    public GameAssetLocator(IMutagenService mutagenService, ILogger logger)
    {
        _mutagenService = mutagenService;
        _logger = logger.ForContext<GameAssetLocator>();
        _fileSystem = new FileSystem();

        _extractionRoot = Path.Combine(Path.GetTempPath(), "Boutique", "ExtractedAssets", "default");
        EnsureDirectoryExists(_extractionRoot);
    }

    public string? ResolveAssetPath(string relativePath, ModKey? modKeyHint = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        if (Path.IsPathRooted(relativePath))
            return File.Exists(relativePath) ? relativePath : null;

        var normalized = NormalizeAssetPath(relativePath);
        var dataPath = _mutagenService.DataFolderPath;

        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
        {
            _logger.Debug("ResolveAssetPath skipped because data path is unavailable. Requested asset: {Asset}", normalized);
            return null;
        }

        EnsureDataPath(dataPath);

        if (_extractedAssets.TryGetValue(normalized, out var cached) && File.Exists(cached))
            return cached;

        var systemRelative = ToSystemPath(normalized);
        var looseCandidate = Path.Combine(dataPath, systemRelative);

        if (File.Exists(looseCandidate))
            return looseCandidate;

        foreach (var archive in EnumerateCandidateArchives(modKeyHint))
        {
            if (TryExtractFromArchive(archive, normalized, out var extracted))
                return extracted;
        }

        foreach (var fallback in EnumerateFallbackArchives(modKeyHint))
        {
            if (TryExtractFromArchive(fallback, normalized, out var extracted))
                return extracted;
        }

        _logger.Debug("Asset {Asset} could not be resolved from loose files or archives.", normalized);
        return null;
    }

    private void EnsureDataPath(string dataPath)
    {
        lock (_sync)
        {
            if (_currentDataPath != null && string.Equals(_currentDataPath, dataPath, StringComparison.OrdinalIgnoreCase))
                return;

            _currentDataPath = dataPath;
            _archivesByMod.Clear();
            _mastersByMod.Clear();
            _extractedAssets.Clear();

            var hash = ComputePathHash(dataPath);
            _extractionRoot = Path.Combine(Path.GetTempPath(), "Boutique", "ExtractedAssets", hash);
            EnsureDirectoryExists(_extractionRoot, recreate: true);
        }
    }

    private IEnumerable<CachedArchive> EnumerateCandidateArchives(ModKey? modKey)
    {
        if (modKey == null)
            yield break;

        IReadOnlyList<CachedArchive> archives;
        lock (_sync)
        {
            if (!_archivesByMod.TryGetValue(modKey.Value, out archives!))
            {
                archives = LoadArchivesForMod(modKey.Value);
                _archivesByMod[modKey.Value] = archives;
            }
        }

        foreach (var archive in archives)
            yield return archive;
    }

    private IEnumerable<CachedArchive> EnumerateFallbackArchives(ModKey? originalModKey)
    {
        var seen = new HashSet<ModKey>();
        foreach (var fallbackKey in GetFallbackKeys(originalModKey))
        {
            if (!fallbackKey.HasValue || !seen.Add(fallbackKey.Value))
                continue;

            foreach (var archive in EnumerateCandidateArchives(fallbackKey))
                yield return archive;
        }
    }

    private IEnumerable<ModKey?> GetFallbackKeys(ModKey? original)
    {
        if (original.HasValue)
        {
            // Allow resolving against masters of the requested plugin
            foreach (var master in GetMastersForMod(original.Value))
                yield return master;
        }

        foreach (var fallback in FallbackModKeys)
            yield return fallback;
    }

    private IReadOnlyList<CachedArchive> LoadArchivesForMod(ModKey modKey)
    {
        var dataPath = _currentDataPath!;
        var directoryPath = new DirectoryPath(dataPath);
        var results = new List<CachedArchive>();

        foreach (var filePath in Archive.GetApplicableArchivePaths(Release, directoryPath, modKey, _fileSystem, returnEmptyIfMissing: true))
        {
            TryAddArchive(results, filePath);
        }

        return results;
    }

    private IEnumerable<ModKey> GetMastersForMod(ModKey modKey)
    {
        if (_mastersByMod.TryGetValue(modKey, out var cachedMasters))
        {
            foreach (var master in cachedMasters)
                yield return master;
            yield break;
        }

        var dataPath = _currentDataPath!;
        var pluginPath = Path.Combine(dataPath, modKey.FileName);
        IReadOnlyList<ModKey> masters = Array.Empty<ModKey>();

        if (File.Exists(pluginPath))
        {
            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimSE);
                masters = mod.ModHeader.MasterReferences
                    .Select(m => m.Master)
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to read masters for mod {ModKey}", modKey);
            }
        }

        _mastersByMod[modKey] = masters;

        foreach (var master in masters)
            yield return master;
    }

    private void TryAddArchive(ICollection<CachedArchive> archives, FilePath filePath)
    {
        var path = filePath.Path;

        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!File.Exists(path))
            return;

        try
        {
            var reader = Archive.CreateReader(Release, filePath, _fileSystem);
            archives.Add(new CachedArchive(path, reader, _logger));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to open archive {ArchivePath}", path);
        }
    }

    private bool TryExtractFromArchive(CachedArchive archive, string assetKey, out string extractedPath)
    {
        extractedPath = string.Empty;
        var file = archive.FindFile(assetKey);
        if (file == null)
            return false;

        extractedPath = ExtractFile(assetKey, file);
        return extractedPath != null;
    }

    private string? ExtractFile(string assetKey, IArchiveFile file)
    {
        var targetPath = Path.Combine(_extractionRoot, ToSystemPath(assetKey));
        var directory = Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        try
        {
            using var source = file.AsStream();
            using var destination = File.Open(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            source.CopyTo(destination);
            destination.Flush();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to extract asset {AssetKey} from archive.", assetKey);
            return null;
        }

        _extractedAssets[assetKey] = targetPath;
        return targetPath;
    }

    private static void EnsureDirectoryExists(string path, bool recreate = false)
    {
        if (recreate && Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup. Swallow and continue if deletion fails.
            }
        }

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    private static string NormalizeAssetPath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("/"))
            normalized = normalized[1..];
        return normalized;
    }

    private static string ToSystemPath(string normalized)
    {
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string ComputePathHash(string dataPath)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(dataPath.ToLowerInvariant());
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private sealed class CachedArchive
    {
        private readonly IArchiveReader _reader;
        private readonly ILogger _logger;
        private readonly Lazy<Dictionary<string, IArchiveFile>> _files;

        public string ArchivePath { get; }

        public CachedArchive(string archivePath, IArchiveReader reader, ILogger logger)
        {
            ArchivePath = archivePath;
            _reader = reader;
            _logger = logger;
            _files = new Lazy<Dictionary<string, IArchiveFile>>(BuildLookup, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public IArchiveFile? FindFile(string assetKey)
        {
            var lookup = _files.Value;
            lookup.TryGetValue(assetKey, out var file);
            return file;
        }

        private Dictionary<string, IArchiveFile> BuildLookup()
        {
            var dict = new Dictionary<string, IArchiveFile>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var file in _reader.Files)
                {
                    var path = file.Path;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var key = NormalizeAssetPath(path);
                    dict[key] = file;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to enumerate files from archive {ArchivePath}", ArchivePath);
            }

            return dict;
        }
    }
}
