using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public interface IMutagenService
{
    /// <summary>
    ///     Gets the LinkCache for resolving FormLinks
    /// </summary>
    ILinkCache<ISkyrimMod, ISkyrimModGetter>? LinkCache { get; }

    /// <summary>
    ///     Gets the current data folder path
    /// </summary>
    string? DataFolderPath { get; }

    /// <summary>
    ///     Checks if Mutagen is initialized
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    ///     Initializes the Mutagen environment with the Skyrim data path
    /// </summary>
    Task InitializeAsync(string dataFolderPath);

    /// <summary>
    ///     Gets all available ESP/ESM files in the data folder
    /// </summary>
    Task<IEnumerable<string>> GetAvailablePluginsAsync();

    /// <summary>
    ///     Loads armor records from a specific plugin
    /// </summary>
    Task<IEnumerable<IArmorGetter>> LoadArmorsFromPluginAsync(string pluginFileName);
}