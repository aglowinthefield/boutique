using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;

namespace RequiemGlamPatcher.Services;

public class MutagenService : IMutagenService
{
    private IGameEnvironment<ISkyrimMod, ISkyrimModGetter>? _environment;
    private ILinkCache<ISkyrimMod, ISkyrimModGetter>? _linkCache;
    private string? _dataFolderPath;

    public ILinkCache<ISkyrimMod, ISkyrimModGetter>? LinkCache => _linkCache;
    public string? DataFolderPath => _dataFolderPath;
    public bool IsInitialized => _environment != null;

    public async Task InitializeAsync(string dataFolderPath)
    {
        await Task.Run(() =>
        {
            _dataFolderPath = dataFolderPath;

            // Try to create game environment
            try
            {
                _environment = GameEnvironment.Typical.Skyrim(SkyrimRelease.SkyrimSE);
                _linkCache = _environment.LoadOrder.ToImmutableLinkCache();
            }
            catch (Exception)
            {
                // If automatic detection fails, path might not be set correctly
                throw new InvalidOperationException($"Could not initialize Skyrim environment. Ensure Skyrim SE is installed and the data path is correct: {dataFolderPath}");
            }
        });
    }

    public async Task<IEnumerable<string>> GetAvailablePluginsAsync()
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(_dataFolderPath))
                return Enumerable.Empty<string>();

            var pluginFiles = Directory.GetFiles(_dataFolderPath, "*.esp")
                .Concat(Directory.GetFiles(_dataFolderPath, "*.esm"))
                .Concat(Directory.GetFiles(_dataFolderPath, "*.esl"))
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Cast<string>()
                .OrderBy(name => name)
                .ToList();

            return pluginFiles;
        });
    }

    public async Task<IEnumerable<IArmorGetter>> LoadArmorsFromPluginAsync(string pluginFileName)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(_dataFolderPath))
                return Enumerable.Empty<IArmorGetter>();

            var pluginPath = Path.Combine(_dataFolderPath, pluginFileName);

            if (!File.Exists(pluginPath))
                return Enumerable.Empty<IArmorGetter>();

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
}
