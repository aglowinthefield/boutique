using System.Globalization;
using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Serilog;

namespace Boutique.Services;

public class MutagenService(ILoggingService loggingService)
{
    private readonly ILogger _logger = loggingService.ForContext<MutagenService>();
    private IGameEnvironment<ISkyrimMod, ISkyrimModGetter>? _environment;

    public event EventHandler? PluginsChanged;

    public event EventHandler? Initialized;

    public ILinkCache<ISkyrimMod, ISkyrimModGetter>? LinkCache { get; private set; }

    public string? DataFolderPath { get; private set; }

    public bool IsInitialized => _environment != null;

    private bool IsSkyrimVR(string dataFolderPath)
    {
        if (string.IsNullOrWhiteSpace(dataFolderPath) || !Directory.Exists(dataFolderPath))
            return false;

        var skyrimVrEsm = Path.Combine(dataFolderPath, "SkyrimVR.esm");
        return File.Exists(skyrimVrEsm);
    }

    private GameRelease GetGameRelease(string dataFolderPath)
    {
        return IsSkyrimVR(dataFolderPath) ? GameRelease.SkyrimVR : GameRelease.SkyrimSE;
    }

    private SkyrimRelease GetSkyrimRelease(string dataFolderPath)
    {
        return IsSkyrimVR(dataFolderPath) ? SkyrimRelease.SkyrimVR : SkyrimRelease.SkyrimSE;
    }

    public async Task InitializeAsync(string dataFolderPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await Task.Run(() =>
        {
            DataFolderPath = dataFolderPath;

            // Determine if we should use explicit path or auto-detection
            // Use explicit path if: it's set, exists, and contains plugin files
            var useExplicitPath = !string.IsNullOrWhiteSpace(dataFolderPath) &&
                                  Directory.Exists(dataFolderPath) &&
                                  (Directory.EnumerateFiles(dataFolderPath, "*.esm").Any() ||
                                   Directory.EnumerateFiles(dataFolderPath, "*.esp").Any());

            if (useExplicitPath)
            {
                _logger.Information("Using explicit data path: {DataPath}", dataFolderPath);
                InitializeWithExplicitPath(dataFolderPath);
            }
            else
            {
                _logger.Information("Using auto-detection (no explicit path or path has no plugins)");
                InitializeWithAutoDetection(dataFolderPath);
            }
        });

        _logger.Information("[PERF] MutagenService.InitializeAsync total: {ElapsedMs}ms", sw.ElapsedMilliseconds);
        Initialized?.Invoke(this, EventArgs.Empty);
    }

    private void InitializeWithExplicitPath(string dataFolderPath)
    {
        try
        {
            var gameRelease = GetGameRelease(dataFolderPath);
            _logger.Information("Detected game release: {GameRelease}", gameRelease);

            var envSw = System.Diagnostics.Stopwatch.StartNew();
            _environment = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(gameRelease)
                .WithTargetDataFolder(new DirectoryPath(dataFolderPath))
                .Build();
            _logger.Information("[PERF] GameEnvironment built with explicit path: {ElapsedMs}ms", envSw.ElapsedMilliseconds);

            var cacheSw = System.Diagnostics.Stopwatch.StartNew();
            LinkCache = _environment.LoadOrder.ToImmutableLinkCache();
            _logger.Information("[PERF] ToImmutableLinkCache: {ElapsedMs}ms", cacheSw.ElapsedMilliseconds);
        }
        catch (Exception explicitPathEx)
        {
            _logger.Warning(explicitPathEx, "Explicit path failed, falling back to auto-detection");
            InitializeWithAutoDetection(dataFolderPath);
        }
    }

    private void InitializeWithAutoDetection(string dataFolderPath)
    {
        try
        {
            var skyrimRelease = GetSkyrimRelease(dataFolderPath);
            _logger.Information("Detected Skyrim release: {SkyrimRelease}", skyrimRelease);

            var envSw = System.Diagnostics.Stopwatch.StartNew();
            _environment = GameEnvironment.Typical.Skyrim(skyrimRelease);
            _logger.Information("[PERF] GameEnvironment.Typical.Skyrim (auto-detect): {ElapsedMs}ms", envSw.ElapsedMilliseconds);

            var cacheSw = System.Diagnostics.Stopwatch.StartNew();
            LinkCache = _environment.LoadOrder.ToImmutableLinkCache();
            _logger.Information("[PERF] ToImmutableLinkCache: {ElapsedMs}ms", cacheSw.ElapsedMilliseconds);
        }
        catch (Exception autoDetectEx)
        {
            _logger.Error(autoDetectEx, "Auto-detection failed for data path {DataPath}", dataFolderPath);
            throw new InvalidOperationException(
                $"Could not initialize Skyrim environment. Ensure Skyrim SE is installed and the data path is correct: {dataFolderPath}\n\n" +
                $"Error: {autoDetectEx.Message}\n\n" +
                "Try running SkyrimSELauncher.exe once to register the game path, then restart this application.",
                autoDetectEx);
        }
    }

    public async Task<IEnumerable<string>> GetAvailablePluginsAsync()
    {
        return await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (string.IsNullOrEmpty(DataFolderPath))
                return Enumerable.Empty<string>();

            var pluginFiles = Directory.GetFiles(DataFolderPath, "*.esp")
                .Concat(Directory.GetFiles(DataFolderPath, "*.esm"))
                .Concat(Directory.GetFiles(DataFolderPath, "*.esl"))
                .OrderBy(Path.GetFileName)
                .ToList();

            _logger.Information("[PERF] GetAvailablePluginsAsync: Found {Count} plugin files to scan", pluginFiles.Count);

            var armorPlugins = new List<string>();
            var scannedCount = 0;

            var skyrimRelease = GetSkyrimRelease(DataFolderPath);

            foreach (var pluginPath in pluginFiles)
            {
                try
                {
                    using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, skyrimRelease);

                    if (mod.Armors.Count <= 0 && mod.Outfits.Count <= 0)
                        continue;
                    var name = Path.GetFileName(pluginPath);
                    if (!string.IsNullOrEmpty(name))
                        armorPlugins.Add(name);
                }
                catch
                {
                    // Ignore plugins that cannot be read; they will be omitted from the picker.
                }

                scannedCount++;
                if (scannedCount % 100 == 0)
                    _logger.Debug("[PERF] Scanned {Count}/{Total} plugins...", scannedCount, pluginFiles.Count);
            }

            armorPlugins.Sort(StringComparer.OrdinalIgnoreCase);

            _logger.Information("[PERF] GetAvailablePluginsAsync: Scanned {Total} plugins in {ElapsedMs}ms, found {ArmorCount} with armors/outfits",
                pluginFiles.Count, sw.ElapsedMilliseconds, armorPlugins.Count);

            return armorPlugins;
        });
    }

    public async Task<IEnumerable<IArmorGetter>> LoadArmorsFromPluginAsync(string pluginFileName)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(DataFolderPath))
                return [];

            var pluginPath = Path.Combine(DataFolderPath, pluginFileName);

            if (!File.Exists(pluginPath))
                return [];

            try
            {
                var skyrimRelease = GetSkyrimRelease(DataFolderPath);
                using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, skyrimRelease);
                return mod.Armors.ToList();
            }
            catch (Exception)
            {
                return Enumerable.Empty<IArmorGetter>();
            }
        });
    }

    public async Task<IEnumerable<IOutfitGetter>> LoadOutfitsFromPluginAsync(string pluginFileName)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(DataFolderPath))
                return [];

            var pluginPath = Path.Combine(DataFolderPath, pluginFileName);

            if (!File.Exists(pluginPath))
                return [];

            try
            {
                var skyrimRelease = GetSkyrimRelease(DataFolderPath);
                using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, skyrimRelease);
                return mod.Outfits.ToList();
            }
            catch (Exception)
            {
                return Enumerable.Empty<IOutfitGetter>();
            }
        });
    }

    public async Task RefreshLinkCacheAsync(string? expectedPlugin = null)
    {
        if (string.IsNullOrEmpty(DataFolderPath))
            return;

        var previousCount = _environment?.LoadOrder.Count ?? 0;
        _logger.Information("Refreshing LinkCache (current load order: {PreviousCount} mod(s))...", previousCount);

        await Task.Run(() =>
        {
            _environment?.Dispose();

            // Use explicit path if it exists and has plugins, otherwise auto-detect
            var useExplicitPath = Directory.Exists(DataFolderPath) &&
                                  (Directory.EnumerateFiles(DataFolderPath, "*.esm").Any() ||
                                   Directory.EnumerateFiles(DataFolderPath, "*.esp").Any());

            if (useExplicitPath)
            {
                try
                {
                    var gameRelease = GetGameRelease(DataFolderPath);
                    _environment = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(gameRelease)
                        .WithTargetDataFolder(new DirectoryPath(DataFolderPath!))
                        .Build();
                }
                catch
                {
                    var skyrimRelease = GetSkyrimRelease(DataFolderPath);
                    _environment = GameEnvironment.Typical.Skyrim(skyrimRelease);
                }
            }
            else
            {
                var skyrimRelease = GetSkyrimRelease(DataFolderPath);
                _environment = GameEnvironment.Typical.Skyrim(skyrimRelease);
            }

            LinkCache = _environment.LoadOrder.ToImmutableLinkCache();
        });

        var newCount = _environment?.LoadOrder.Count ?? 0;
        var diff = newCount - previousCount;
        var diffText = diff > 0 ? $"+{diff}" : diff.ToString(CultureInfo.InvariantCulture);

        _logger.Information("LinkCache refreshed. Load order: {PreviousCount} → {NewCount} mod(s) ({Diff}).",
            previousCount, newCount, diffText);

        if (!string.IsNullOrEmpty(expectedPlugin) && !string.IsNullOrEmpty(DataFolderPath))
        {
            var pluginPath = Path.Combine(DataFolderPath, expectedPlugin);
            var fileExists = File.Exists(pluginPath);

            if (fileExists)
            {
                var fileInfo = new FileInfo(pluginPath);
                _logger.Information("Confirmed {Plugin} exists on disk ({Size:N0} bytes, modified {Modified:HH:mm:ss}).",
                    expectedPlugin, fileInfo.Length, fileInfo.LastWriteTime);

                var inLoadOrder = _environment?.LoadOrder
                    .Any(entry => string.Equals(entry.Key.FileName, expectedPlugin, StringComparison.OrdinalIgnoreCase)) ?? false;

                if (!inLoadOrder)
                    _logger.Debug("{Plugin} is not in active load order (not enabled in plugins.txt) - this is normal for newly created patches.", expectedPlugin);
            }
            else
            {
                _logger.Warning("Expected plugin {Plugin} was NOT found at {Path}!", expectedPlugin, pluginPath);
            }
        }

        PluginsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReleaseLinkCache()
    {
        _logger.Debug("Releasing LinkCache file handles...");
        _environment?.Dispose();
        _environment = null;
        LinkCache = null;
    }
}
