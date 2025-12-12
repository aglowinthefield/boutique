namespace Boutique.Utilities;

/// <summary>
/// Constants for Skyrim Special Edition vanilla plugin files.
/// </summary>
public static class SkyrimConstants
{
    /// <summary>
    /// Vanilla Skyrim Special Edition plugin file names (ESM/ESP).
    /// These are the base game and official DLC plugins.
    /// </summary>
    public static readonly HashSet<string> VanillaPluginNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Skyrim.esm",
        "Update.esm",
        "Dawnguard.esm",
        "HearthFires.esm",
        "Dragonborn.esm"
    };

    /// <summary>
    /// Checks if a plugin name is a vanilla Skyrim plugin.
    /// </summary>
    public static bool IsVanillaPlugin(string pluginName)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            return false;
        
        return VanillaPluginNames.Contains(pluginName);
    }
}

