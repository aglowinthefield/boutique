using System.IO;

namespace Boutique.Utilities;

public static class PathUtilities
{
    private static readonly string[] _pluginExtensions = ["*.esp", "*.esm", "*.esl"];

    public static string NormalizeAssetPath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith('/'))
        {
            normalized = normalized[1..];
        }

        return normalized;
    }

    public static string ToSystemPath(string normalized) =>
        normalized.Replace('/', Path.DirectorySeparatorChar);

    public static string GetBoutiqueAppDataPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boutique");

    public static string GetSkyPatcherRoot(string dataPath) =>
        Path.Combine(dataPath, "skse", "plugins", "SkyPatcher");

    public static string GetSkyPatcherNpcPath(string dataPath) =>
        Path.Combine(GetSkyPatcherRoot(dataPath), "npc");

    public static IEnumerable<string> EnumeratePluginFiles(string dataPath)
    {
        try
        {
            return Directory.EnumerateFiles(dataPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".esl", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return [];
        }
    }

    public static bool HasPluginFiles(string dataPath)
    {
        if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
        {
            return false;
        }

        return EnumeratePluginFiles(dataPath).Any();
    }
}
