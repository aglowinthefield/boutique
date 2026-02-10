using System.Collections;
using System.IO;
using System.Reflection;
using System.Resources;

namespace Boutique.Services;

public static class IconCacheService
{
  private static readonly Lazy<IReadOnlyList<string>> _lazyIcons = new(LoadIcons);
  private static readonly Random                      _rng       = new();

  public static IReadOnlyList<string> Icons => _lazyIcons.Value;

  public static string? GetRandomIcon()
  {
    var icons = Icons;
    return icons.Count > 0 ? icons[_rng.Next(icons.Count)] : null;
  }

  private static List<string> LoadIcons()
  {
    var icons = new List<string>();

    try
    {
      var       assembly      = Assembly.GetExecutingAssembly();
      var       resourcesName = assembly.GetName().Name + ".g.resources";
      using var stream        = assembly.GetManifestResourceStream(resourcesName);
      if (stream == null)
      {
        return icons;
      }

      using var reader = new ResourceReader(stream);
      icons.AddRange(
        from DictionaryEntry entry in reader
        select (string)entry.Key
        into key
        where IsIconFile(key)
        select Path.GetFileName(key));
    }
    catch
    {
      return icons;
    }

    icons.Sort(StringComparer.OrdinalIgnoreCase);
    return icons;
  }

  private static bool IsIconFile(string key)
  {
    return key.StartsWith("assets/sprites/", StringComparison.OrdinalIgnoreCase) &&
           key.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
  }
}
