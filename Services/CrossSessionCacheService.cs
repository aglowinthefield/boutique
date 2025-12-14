using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Boutique.Models;
using MessagePack;
using Serilog;

namespace Boutique.Services;

/// <summary>
/// Handles cross-session caching of expensive game data using MessagePack serialization.
/// Provides automatic invalidation based on load order and distribution file changes.
/// </summary>
public class CrossSessionCacheService
{
    private readonly ILogger _logger;
    private readonly string _cacheDirectory;
    private const string CacheFileName = "game_data_cache.msgpack";

    public CrossSessionCacheService(ILogger logger)
    {
        _logger = logger.ForContext<CrossSessionCacheService>();

        // Store cache in %LOCALAPPDATA%\Boutique\cache\
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _cacheDirectory = Path.Combine(localAppData, "Boutique", "cache");
    }

    /// <summary>
    /// Gets the full path to the cache file.
    /// </summary>
    public string CacheFilePath => Path.Combine(_cacheDirectory, CacheFileName);

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    public static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>
    /// Attempts to load cached game data. Returns null if cache is invalid or doesn't exist.
    /// </summary>
    /// <param name="dataPath">Current Skyrim data path for validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cached data if valid, null otherwise.</returns>
    public async Task<GameDataCache?> TryLoadCacheAsync(string dataPath, CancellationToken cancellationToken = default)
    {
        var cacheFile = CacheFilePath;

        if (!File.Exists(cacheFile))
        {
            _logger.Debug("Cache file does not exist: {Path}", cacheFile);
            return null;
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Read the cache file
            var bytes = await File.ReadAllBytesAsync(cacheFile, cancellationToken);
            var cache = MessagePackSerializer.Deserialize<GameDataCache>(bytes, cancellationToken: cancellationToken);

            stopwatch.Stop();
            _logger.Information("[PERF] Cache file loaded and deserialized in {ElapsedMs}ms ({Size:N0} bytes)",
                stopwatch.ElapsedMilliseconds, bytes.Length);

            // Validate the cache
            if (!ValidateCache(cache, dataPath))
            {
                _logger.Information("Cache validation failed, will reload data.");
                return null;
            }

            _logger.Information("Cache is valid. Loaded {NpcCount} NPCs and {FileCount} distribution files from cache.",
                cache.NpcFilterData.Count, cache.DistributionFiles.Count);

            return cache;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load cache file, will reload data.");
            return null;
        }
    }

    /// <summary>
    /// Saves game data to the cache.
    /// </summary>
    /// <param name="npcData">NPC filter data to cache.</param>
    /// <param name="distributionFiles">Distribution files to cache.</param>
    /// <param name="dataPath">Skyrim data path for signature computation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveCacheAsync(
        IReadOnlyList<NpcFilterData> npcData,
        IReadOnlyList<DistributionFile> distributionFiles,
        string dataPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Ensure directory exists
            Directory.CreateDirectory(_cacheDirectory);

            // Create metadata
            var metadata = new CacheMetadata
            {
                AppVersion = AppVersion,
                DataPathHash = ComputeHash(dataPath),
                LoadOrderSignature = ComputeLoadOrderSignature(dataPath),
                DistributionFilesSignature = ComputeDistributionFilesSignature(dataPath),
                CreatedAt = DateTime.UtcNow
            };

            // Create cache object
            var cache = new GameDataCache
            {
                Metadata = metadata,
                NpcFilterData = npcData.Select(n => n.ToDto()).ToList(),
                DistributionFiles = distributionFiles.Select(f => f.ToDto()).ToList()
            };

            // Serialize and write
            var bytes = MessagePackSerializer.Serialize(cache, cancellationToken: cancellationToken);
            await File.WriteAllBytesAsync(CacheFilePath, bytes, cancellationToken);

            stopwatch.Stop();
            _logger.Information("[PERF] Cache saved in {ElapsedMs}ms ({Size:N0} bytes, {NpcCount} NPCs, {FileCount} distribution files)",
                stopwatch.ElapsedMilliseconds, bytes.Length, npcData.Count, distributionFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to save cache file. This is non-fatal.");
        }
    }

    /// <summary>
    /// Invalidates (deletes) the cache file.
    /// </summary>
    public void InvalidateCache()
    {
        try
        {
            var cacheFile = CacheFilePath;
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
                _logger.Information("Cross-session cache invalidated and deleted.");
            }
            else
            {
                _logger.Debug("No cache file to delete.");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to delete cache file.");
        }
    }

    /// <summary>
    /// Gets information about the current cache file.
    /// </summary>
    /// <returns>Cache info or null if no cache exists.</returns>
    public CacheInfo? GetCacheInfo()
    {
        var cacheFile = CacheFilePath;
        if (!File.Exists(cacheFile))
            return null;

        try
        {
            var fileInfo = new FileInfo(cacheFile);
            return new CacheInfo
            {
                FilePath = cacheFile,
                FileSizeBytes = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validates the cache against the current environment.
    /// </summary>
    private bool ValidateCache(GameDataCache cache, string dataPath)
    {
        var metadata = cache.Metadata;

        // Check app version
        if (metadata.AppVersion != AppVersion)
        {
            _logger.Information("Cross-session cache INVALIDATED: app version changed ({CacheVersion} -> {CurrentVersion})",
                metadata.AppVersion, AppVersion);
            return false;
        }

        // Check data path
        var currentDataPathHash = ComputeHash(dataPath);
        if (metadata.DataPathHash != currentDataPathHash)
        {
            _logger.Information("Cross-session cache INVALIDATED: data path changed");
            return false;
        }

        // Check load order signature
        var currentLoadOrderSig = ComputeLoadOrderSignature(dataPath);
        if (metadata.LoadOrderSignature != currentLoadOrderSig)
        {
            _logger.Information("Cross-session cache INVALIDATED: load order changed (plugins.txt or plugin files modified)");
            return false;
        }

        // Check distribution files signature
        var currentDistSig = ComputeDistributionFilesSignature(dataPath);
        if (metadata.DistributionFilesSignature != currentDistSig)
        {
            _logger.Information("Cross-session cache INVALIDATED: distribution INI files changed");
            return false;
        }

        _logger.Information("Cross-session cache VALID (created {CreatedAt:g})", metadata.CreatedAt.ToLocalTime());
        return true;
    }

    /// <summary>
    /// Computes a signature for the load order based on plugins.txt and plugin file timestamps.
    /// </summary>
    public string ComputeLoadOrderSignature(string dataPath)
    {
        try
        {
            var sb = new StringBuilder();

            // Try to read plugins.txt
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pluginsPath = Path.Combine(localAppData, "Skyrim Special Edition", "plugins.txt");

            if (File.Exists(pluginsPath))
            {
                var content = File.ReadAllText(pluginsPath);
                sb.Append(content);
                var pluginsFileInfo = new FileInfo(pluginsPath);
                sb.Append(pluginsFileInfo.LastWriteTimeUtc.Ticks);
            }

            // Also include a hash of plugin files in the data folder (to catch MO2 virtual filesystem changes)
            var pluginFiles = GetPluginFiles(dataPath);
            foreach (var plugin in pluginFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(100)) // Limit to first 100 for performance
            {
                sb.Append(Path.GetFileName(plugin));
                try
                {
                    var info = new FileInfo(plugin);
                    sb.Append(info.LastWriteTimeUtc.Ticks);
                    sb.Append(info.Length);
                }
                catch
                {
                    // Skip files we can't access
                }
            }

            return ComputeHash(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to compute load order signature, using fallback.");
            return Guid.NewGuid().ToString(); // Force cache invalidation
        }
    }

    /// <summary>
    /// Computes a signature for distribution files based on paths and timestamps.
    /// </summary>
    public string ComputeDistributionFilesSignature(string dataPath)
    {
        try
        {
            var sb = new StringBuilder();

            // SPID files in root Data folder
            var spidFiles = Directory.EnumerateFiles(dataPath, "*_DISTR.ini", SearchOption.TopDirectoryOnly);
            foreach (var file in spidFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                AppendFileSignature(sb, file);
            }

            // SkyPatcher files
            var skyPatcherRoot = Path.Combine(dataPath, "skse", "plugins", "SkyPatcher");
            if (Directory.Exists(skyPatcherRoot))
            {
                var skyPatcherFiles = Directory.EnumerateFiles(skyPatcherRoot, "*.ini*", SearchOption.AllDirectories);
                foreach (var file in skyPatcherFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    AppendFileSignature(sb, file);
                }
            }

            return ComputeHash(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to compute distribution files signature, using fallback.");
            return Guid.NewGuid().ToString(); // Force cache invalidation
        }
    }

    private static void AppendFileSignature(StringBuilder sb, string filePath)
    {
        try
        {
            sb.Append(filePath);
            var info = new FileInfo(filePath);
            sb.Append(info.LastWriteTimeUtc.Ticks);
            sb.Append(info.Length);
        }
        catch
        {
            // Skip files we can't access
        }
    }

    private static IEnumerable<string> GetPluginFiles(string dataPath)
    {
        var extensions = new[] { "*.esp", "*.esm", "*.esl" };
        foreach (var ext in extensions)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dataPath, ext, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16]; // First 16 chars is enough for our purposes
    }
}

/// <summary>
/// Information about the cache file.
/// </summary>
public sealed class CacheInfo
{
    public required string FilePath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required DateTime LastModified { get; init; }

    public string FileSizeFormatted
    {
        get
        {
            const long kb = 1024;
            const long mb = kb * 1024;

            return FileSizeBytes switch
            {
                >= mb => $"{FileSizeBytes / (double)mb:F1} MB",
                >= kb => $"{FileSizeBytes / (double)kb:F1} KB",
                _ => $"{FileSizeBytes} bytes"
            };
        }
    }

    public string LastModifiedFormatted => LastModified.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
}
