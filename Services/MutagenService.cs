using System.Globalization;
using System.IO;
using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Serilog;

namespace Boutique.Services;

public class MutagenService(ILoggingService loggingService, PatcherSettings settings, GuiSettingsService guiSettings)
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly GuiSettingsService _guiSettings = guiSettings;
    private readonly ILogger _logger = loggingService.ForContext<MutagenService>();
    private readonly PatcherSettings _settings = settings;
    private IGameEnvironment<ISkyrimMod, ISkyrimModGetter>? _environment;

    private bool IsBlacklisted(string pluginName) =>
        _guiSettings.BlacklistedPlugins?.Contains(pluginName, StringComparer.OrdinalIgnoreCase) == true;

    public ILinkCache<ISkyrimMod, ISkyrimModGetter>? LinkCache { get; private set; }

    public string? DataFolderPath { get; private set; }

    public bool IsInitialized => _environment != null;

    public SkyrimRelease SkyrimRelease => GetSkyrimRelease();

    public GameRelease GameRelease => GetGameRelease();

    public event EventHandler? PluginsChanged;

    public event EventHandler? Initialized;

    private GameRelease GetGameRelease() => GetSkyrimRelease() switch
    {
        SkyrimRelease.SkyrimSE => GameRelease.SkyrimSE,
        SkyrimRelease.SkyrimVR => GameRelease.SkyrimVR,
        SkyrimRelease.SkyrimSEGog => GameRelease.SkyrimSEGog,
        _ => GameRelease.SkyrimSE
    };

    private SkyrimRelease GetSkyrimRelease() =>
        _settings.SelectedSkyrimRelease != default ? _settings.SelectedSkyrimRelease : SkyrimRelease.SkyrimSE;

    public async Task InitializeAsync(string dataFolderPath)
    {
        if (IsInitialized)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (IsInitialized)
            {
                return;
            }

            using (StartupProfiler.Instance.BeginOperation("MutagenService.Initialize"))
            {
                await Task.Run(() =>
                {
                    DataFolderPath = dataFolderPath;

                    var useExplicitPath = !string.IsNullOrWhiteSpace(dataFolderPath) &&
                                          PathUtilities.HasPluginFiles(dataFolderPath);

                    if (useExplicitPath)
                    {
                        _logger.Information("Using explicit data path: {DataPath}", dataFolderPath);
                        using (StartupProfiler.Instance.BeginOperation("BuildGameEnvironment", "MutagenService.Initialize"))
                        {
                            InitializeWithExplicitPath(dataFolderPath);
                        }
                    }
                    else
                    {
                        _logger.Information("Using auto-detection (no explicit path or path has no plugins)");
                        using (StartupProfiler.Instance.BeginOperation("BuildGameEnvironment", "MutagenService.Initialize"))
                        {
                            InitializeWithAutoDetection(dataFolderPath);
                        }
                    }
                });
            }

            Initialized?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void InitializeWithExplicitPath(string dataFolderPath)
    {
        try
        {
            var gameRelease = GetGameRelease();
            _logger.Information("Detected game release: {GameRelease}", gameRelease);

            _environment = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(gameRelease)
                .WithTargetDataFolder(new DirectoryPath(dataFolderPath))
                .Build();

            LinkCache = _environment.LoadOrder.ToImmutableLinkCache();
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
            var skyrimRelease = GetSkyrimRelease();
            _logger.Information("Detected Skyrim release: {SkyrimRelease}", skyrimRelease);

            _environment = GameEnvironment.Typical.Skyrim(skyrimRelease);

            LinkCache = _environment.LoadOrder.ToImmutableLinkCache();
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

    public async Task<IEnumerable<string>> GetAvailablePluginsAsync(bool excludeBlacklisted = true)
    {
        using var profilerScope = StartupProfiler.Instance.BeginOperation("ScanAvailablePlugins");
        return await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(DataFolderPath))
            {
                return Enumerable.Empty<string>();
            }

            return PathUtilities.EnumeratePluginFiles(DataFolderPath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .Where(name => !excludeBlacklisted || !IsBlacklisted(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        });
    }

    public async Task<IEnumerable<IArmorGetter>> LoadArmorsFromPluginAsync(string pluginFileName)
    {
        if (string.IsNullOrEmpty(DataFolderPath) || IsBlacklisted(pluginFileName))
        {
            return [];
        }

        return await Task.Run(() =>
        {
            var pluginPath = Path.Combine(DataFolderPath, pluginFileName);

            if (!File.Exists(pluginPath))
            {
                return [];
            }

            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, GetSkyrimRelease());
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
        if (string.IsNullOrEmpty(DataFolderPath) || IsBlacklisted(pluginFileName))
        {
            return [];
        }

        return await Task.Run(() =>
        {
            var pluginPath = Path.Combine(DataFolderPath, pluginFileName);

            if (!File.Exists(pluginPath))
            {
                return [];
            }

            try
            {
                using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, GetSkyrimRelease());
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
        {
            return;
        }

        var previousCount = _environment?.LoadOrder.Count ?? 0;
        _logger.Information("Refreshing LinkCache (current load order: {PreviousCount} mod(s))...", previousCount);

        await Task.Run(() =>
        {
            _environment?.Dispose();

            var useExplicitPath = PathUtilities.HasPluginFiles(DataFolderPath);

            if (useExplicitPath)
            {
                try
                {
                    var gameRelease = GetGameRelease();
                    _environment = GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(gameRelease)
                        .WithTargetDataFolder(new DirectoryPath(DataFolderPath!))
                        .Build();
                }
                catch
                {
                    var skyrimRelease = GetSkyrimRelease();
                    _environment = GameEnvironment.Typical.Skyrim(skyrimRelease);
                }
            }
            else
            {
                var skyrimRelease = GetSkyrimRelease();
                _environment = GameEnvironment.Typical.Skyrim(skyrimRelease);
            }

            LinkCache = _environment.LoadOrder.ToImmutableLinkCache();
        });

        var newCount = _environment?.LoadOrder.Count ?? 0;
        var diff = newCount - previousCount;
        var diffText = diff > 0 ? $"+{diff}" : diff.ToString(CultureInfo.InvariantCulture);

        _logger.Information(
            "LinkCache refreshed. Load order: {PreviousCount} → {NewCount} mod(s) ({Diff}).",
            previousCount,
            newCount,
            diffText);

        if (!string.IsNullOrEmpty(expectedPlugin) && !string.IsNullOrEmpty(DataFolderPath))
        {
            var pluginPath = Path.Combine(DataFolderPath, expectedPlugin);
            var fileExists = File.Exists(pluginPath);

            if (fileExists)
            {
                var fileInfo = new FileInfo(pluginPath);
                _logger.Information(
                    "Confirmed {Plugin} exists on disk ({Size:N0} bytes, modified {Modified:HH:mm:ss}).",
                    expectedPlugin,
                    fileInfo.Length,
                    fileInfo.LastWriteTime);

                var inLoadOrder = _environment?.LoadOrder
                    .Any(entry =>
                        string.Equals(entry.Key.FileName, expectedPlugin, StringComparison.OrdinalIgnoreCase)) ?? false;

                if (!inLoadOrder)
                {
                    _logger.Debug(
                        "{Plugin} is not in active load order (not enabled in plugins.txt) - this is normal for newly created patches.",
                        expectedPlugin);
                }
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

    public HashSet<ModKey> GetLoadOrderModKeys() =>
        _environment?.LoadOrder
            .Select(entry => entry.Key)
            .ToHashSet() ?? [];
}
