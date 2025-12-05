using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class MutagenService(ILoggingService loggingService) : IMutagenService
{
    private readonly ILogger _logger = loggingService.ForContext<MutagenService>();
    private IGameEnvironment<ISkyrimMod, ISkyrimModGetter>? _environment;

    public event EventHandler? PluginsChanged;

    public ILinkCache<ISkyrimMod, ISkyrimModGetter>? LinkCache { get; private set; }

    public string? DataFolderPath { get; private set; }

    public bool IsInitialized => _environment != null;

    public async Task InitializeAsync(string dataFolderPath)
    {
        await Task.Run(() =>
        {
            DataFolderPath = dataFolderPath;

            // Try to create game environment
            try
            {
                _environment = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
                LinkCache = _environment.LoadOrder.ToImmutableLinkCache();
            }
            catch (Exception)
            {
                // If automatic detection fails, path might not be set correctly
                throw new InvalidOperationException(
                    $"Could not initialize Skyrim environment. Ensure Skyrim SE is installed and the data path is correct: {dataFolderPath}");
            }
        });
    }

    public async Task<IEnumerable<string>> GetAvailablePluginsAsync()
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(DataFolderPath))
                return Enumerable.Empty<string>();

            var pluginFiles = Directory.GetFiles(DataFolderPath, "*.esp")
                .Concat(Directory.GetFiles(DataFolderPath, "*.esm"))
                .Concat(Directory.GetFiles(DataFolderPath, "*.esl"))
                .OrderBy(Path.GetFileName)
                .ToList();

            var armorPlugins = new List<string>();

            foreach (var pluginPath in pluginFiles)
                try
                {
                    using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimSE);

                    if (mod.Armors.Count <= 0 && mod.Outfits.Count <= 0) continue;
                    var name = Path.GetFileName(pluginPath);
                    if (!string.IsNullOrEmpty(name)) armorPlugins.Add(name);
                }
                catch
                {
                    // Ignore plugins that cannot be read; they will be omitted from the picker.
                }

            armorPlugins.Sort(StringComparer.OrdinalIgnoreCase);
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
                // Use binary overlay for efficient read-only access
                using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimSE);

                // Convert to list to materialize before disposing
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
                using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimSE);
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
            // Dispose old environment before creating a new one
            _environment?.Dispose();

            _environment = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
            LinkCache = _environment.LoadOrder.ToImmutableLinkCache();
        });

        var newCount = _environment?.LoadOrder.Count ?? 0;
        var diff = newCount - previousCount;
        var diffText = diff > 0 ? $"+{diff}" : diff.ToString();

        _logger.Information("LinkCache refreshed. Load order: {PreviousCount} → {NewCount} mod(s) ({Diff}).",
            previousCount, newCount, diffText);

        // Verify the expected plugin exists on disk (it may not be in the load order if not enabled in plugins.txt)
        if (!string.IsNullOrEmpty(expectedPlugin) && !string.IsNullOrEmpty(DataFolderPath))
        {
            var pluginPath = Path.Combine(DataFolderPath, expectedPlugin);
            var fileExists = File.Exists(pluginPath);

            if (fileExists)
            {
                var fileInfo = new FileInfo(pluginPath);
                _logger.Information("Confirmed {Plugin} exists on disk ({Size:N0} bytes, modified {Modified:HH:mm:ss}).",
                    expectedPlugin, fileInfo.Length, fileInfo.LastWriteTime);

                // Check if it's also in the active load order (enabled in plugins.txt)
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

        // Notify subscribers that the plugin list may have changed
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
