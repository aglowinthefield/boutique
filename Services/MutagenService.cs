using System.IO;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public class MutagenService : IMutagenService
{
    private IGameEnvironment<ISkyrimMod, ISkyrimModGetter>? _environment;

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
                .OrderBy(path => Path.GetFileName(path))
                .ToList();

            var armorPlugins = new List<string>();

            foreach (var pluginPath in pluginFiles)
                try
                {
                    using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, SkyrimRelease.SkyrimSE);

                    if (mod.Armors.Count > 0)
                    {
                        var name = Path.GetFileName(pluginPath);
                        if (!string.IsNullOrEmpty(name)) armorPlugins.Add(name);
                    }
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
                return Enumerable.Empty<IArmorGetter>();

            var pluginPath = Path.Combine(DataFolderPath, pluginFileName);

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