using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Services;

public interface IMutagenService
{
    /// <summary>
    ///     Raised when the plugin list may have changed (e.g., after writing a new patch).
    ///     ViewModels should subscribe to refresh their plugin lists.
    /// </summary>
    event EventHandler? PluginsChanged;
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

    /// <summary>
    ///     Loads outfit records from a specific plugin
    /// </summary>
    Task<IEnumerable<IOutfitGetter>> LoadOutfitsFromPluginAsync(string pluginFileName);

    /// <summary>
    ///     Refreshes the LinkCache to pick up newly created or modified plugins.
    ///     Call this after writing a patch to ensure subsequent operations can read it.
    /// </summary>
    /// <param name="expectedPlugin">Optional plugin filename to verify is present after refresh.</param>
    Task RefreshLinkCacheAsync(string? expectedPlugin = null);

    /// <summary>
    ///     Releases file handles held by the environment/LinkCache.
    ///     Call this before writing to a plugin that may be held open by the environment.
    /// </summary>
    void ReleaseLinkCache();
}
