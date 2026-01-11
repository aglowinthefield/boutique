using System.IO;

namespace Boutique.Utilities;

public static class PathUtilities
{
    private static readonly string[] PluginExtensions = ["*.esp", "*.esm", "*.esl"];

    public static string NormalizeAssetPath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith('/'))
            normalized = normalized[1..];
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
        foreach (var ext in PluginExtensions)
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
                yield return file;
        }
    }

    public static bool HasPluginFiles(string dataPath)
    {
        if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
            return false;

        return EnumeratePluginFiles(dataPath).Any();
    }
}
