using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;
using Serilog;

namespace Boutique.Utilities;

/// <summary>
/// Helpers for reading localized strings without crashing when a plugin ships a corrupt or
/// mismatched STRINGS file. A single failure is logged per ModKey; further failures from that
/// plugin are silenced to keep the log readable.
/// </summary>
public static class LocalizedStringExtensions
{
  private static readonly HashSet<ModKey> _reportedModKeys = [];
  private static readonly object _gate = new();
  private static readonly ILogger _log = Log.ForContext(typeof(LocalizedStringExtensions));

  public static string? SafeString(this ITranslatedStringGetter? localized, IMajorRecordGetter? record = null)
  {
    if (localized is null)
    {
      return null;
    }

    try
    {
      return localized.String;
    }
    catch (Exception ex)
    {
      var modKey = record?.FormKey.ModKey;
      if (modKey is { } key)
      {
        bool firstTime;
        lock (_gate)
        {
          firstTime = _reportedModKeys.Add(key);
        }

        if (firstTime)
        {
          _log.Warning(
            ex,
            "Failed to resolve localized string from {Plugin} (likely corrupt or mismatched STRINGS file). Further failures from this plugin will be silenced.",
            key.FileName);
        }
      }
      else
      {
        _log.Debug(ex, "Failed to resolve localized string (no record context available).");
      }

      return null;
    }
  }
}
