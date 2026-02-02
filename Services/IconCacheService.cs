using System.Collections;
using System.IO;
using System.Reflection;
using System.Resources;

namespace Boutique.Services;

public static class IconCacheService
{
    private static readonly Lazy<IReadOnlyList<string>> LazyIcons = new(LoadIcons);
    private static readonly Random Rng = new();

    public static IReadOnlyList<string> Icons => LazyIcons.Value;

    public static string? GetRandomIcon()
    {
        var icons = Icons;
        return icons.Count > 0 ? icons[Rng.Next(icons.Count)] : null;
    }

    private static List<string> LoadIcons()
    {
        var icons = new List<string>();

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourcesName = assembly.GetName().Name + ".g.resources";
            using var stream = assembly.GetManifestResourceStream(resourcesName);
            if (stream == null)
            {
                return icons;
            }

            using var reader = new ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
            {
                var key = (string)entry.Key;
                if (key.StartsWith("assets/sprites/", StringComparison.OrdinalIgnoreCase) &&
                    key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    var filename = Path.GetFileName(key);
                    icons.Add(filename);
                }
            }
        }
        catch
        {
            return icons;
        }

        icons.Sort(StringComparer.OrdinalIgnoreCase);
        return icons;
    }
}
