using System.Diagnostics;
using System.Globalization;
using System.IO;
using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Noggog;
using Serilog;

namespace Boutique.Services;

/// <summary>
/// Initializes and manages the Mutagen game environment, link cache, and plugin loading.
/// Serves as the central gateway for reading and writing Bethesda plugin files across the load order.
/// </summary>
public sealed class MutagenService(ILoggingService loggingService, PatcherSettings settings, GuiSettingsService guiSettings)
  : IDisposable
{
  private readonly SemaphoreSlim _initLock = new(1, 1);
  private readonly ILogger _logger = loggingService.ForContext<MutagenService>();
  private          IGameEnvironment<ISkyrimMod, ISkyrimModGetter>? _environment;
  private          Task<IEnumerable<string>>? _pluginsWithArmorsOrOutfitsTask;

  public ILinkCache<ISkyrimMod, ISkyrimModGetter>? LinkCache { get; private set; }

  public string? DataFolderPath { get; private set; }

  public BinaryReadParameters Utf8ReadParameters { get; } = new()
                                                            {
                                                              StringsParam =
                                                                new StringsReadParameters
                                                                {
                                                                  NonLocalizedEncodingOverride = MutagenEncoding._utf8
                                                                }
                                                            };

  public bool IsInitialized => _environment != null;

  public SkyrimRelease SkyrimRelease => GetSkyrimRelease();

  public GameRelease GameRelease => GetGameRelease();

  public void Dispose()
  {
    _environment?.Dispose();
    _initLock.Dispose();
    GC.SuppressFinalize(this);
  }

  private bool IsBlacklisted(string pluginName) =>
    guiSettings.BlacklistedPlugins?.Contains(pluginName, StringComparer.OrdinalIgnoreCase) == true;

  public event EventHandler? PluginsChanged;

  public event EventHandler? Initialized;

  private GameRelease GetGameRelease() => GetSkyrimRelease() switch
  {
    SkyrimRelease.SkyrimSE    => GameRelease.SkyrimSE,
    SkyrimRelease.SkyrimVR    => GameRelease.SkyrimVR,
    SkyrimRelease.SkyrimSEGog => GameRelease.SkyrimSEGog,
    _                         => GameRelease.SkyrimSE
  };

  private SkyrimRelease GetSkyrimRelease() =>
    settings.SelectedSkyrimRelease != default ? settings.SelectedSkyrimRelease : SkyrimRelease.SkyrimSE;

  public async Task InitializeAsync(string dataFolderPath)
  {
    if (IsInitialized)
    {
      return;
    }

    await _initLock.WaitAsync();
    try
    {
      if (IsInitialized)
      {
        return;
      }

      await Task.Run(() =>
      {
        DataFolderPath = dataFolderPath;

        var useExplicitPath = !string.IsNullOrWhiteSpace(dataFolderPath) &&
                              PathUtilities.HasPluginFiles(dataFolderPath);

        if (useExplicitPath)
        {
          _logger.Information("Using explicit data path: {DataPath}", dataFolderPath);
          InitializeWithExplicitPath(dataFolderPath);
        }
        else
        {
          _logger.Information("Using auto-detection (no explicit path or path has no plugins)");
          InitializeWithAutoDetection(dataFolderPath);
        }
      });

      Initialized?.Invoke(this, EventArgs.Empty);
    }
    finally
    {
      _initLock.Release();
    }
  }

  private void InitializeWithExplicitPath(string dataFolderPath)
  {
    try
    {
      _logger.Information("Detected game release: {GameRelease}", GetGameRelease());
      BuildEnvironment(dataFolderPath);
    }
    catch (Exception explicitPathEx)
    {
      _logger.Warning(explicitPathEx, "Explicit path failed, falling back to auto-detection");
      InitializeWithAutoDetection(dataFolderPath);
    }
  }

  private void InitializeWithAutoDetection(string dataFolderPath)
  {
    try
    {
      _logger.Information("Detected Skyrim release: {SkyrimRelease}", GetSkyrimRelease());
      BuildEnvironment(null);
    }
    catch (Exception autoDetectEx)
    {
      _logger.Error(autoDetectEx, "Auto-detection failed for data path {DataPath}", dataFolderPath);
      throw new InvalidOperationException(
        $"Could not initialize Skyrim environment. Ensure Skyrim SE is installed and the data path is correct: {dataFolderPath}\n\n" +
        $"Error: {autoDetectEx.Message}\n\n" +
        "Try running SkyrimSELauncher.exe once to register the game path, then restart this application.",
        autoDetectEx);
    }
  }

  private void BuildEnvironment(string? explicitDataPath)
  {
    var sw = Stopwatch.StartNew();

    _environment = string.IsNullOrEmpty(explicitDataPath)
                     ? GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(GetGameRelease())
                                      .WithUtf8Encoding()
                                      .Build()
                     : GameEnvironment.Typical.Builder<ISkyrimMod, ISkyrimModGetter>(GetGameRelease())
                                      .WithTargetDataFolder(new DirectoryPath(explicitDataPath))
                                      .WithUtf8Encoding()
                                      .Build();

    LinkCache = _environment.LoadOrder.ToImmutableLinkCache();

    _logger.Information("BuildEnvironment completed in {Elapsed}ms.", sw.ElapsedMilliseconds);
  }

  public async Task<IEnumerable<string>> GetAvailablePluginsAsync(bool excludeBlacklisted = true) =>
    await Task.Run(() =>
    {
      if (string.IsNullOrEmpty(DataFolderPath))
      {
        return Enumerable.Empty<string>();
      }

      return
      [
        .. PathUtilities.EnumeratePluginFiles(DataFolderPath)
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrEmpty(name))
                        .Cast<string>()
                        .Where(name => !excludeBlacklisted || !IsBlacklisted(name))
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
      ];
    });

  public Task<IEnumerable<IArmorGetter>> LoadArmorsFromPluginAsync(string pluginFileName) =>
    LoadRecordsFromPluginAsync(pluginFileName, mod => mod.Armors);

  /// <summary>
  /// Loads every armor in the load order paired with the mod supplying its winning override,
  /// so callers can attribute the record to the plugin users actually see (an override ESP,
  /// not the mod that first defined the record).
  /// </summary>
  public Task<IReadOnlyList<(IArmorGetter Armor, ModKey SourceMod)>> LoadAllArmorsWithContextAsync() =>
    Task.Run<IReadOnlyList<(IArmorGetter, ModKey)>>(() =>
      LinkCache is null
        ? []
        : LinkCache.WinningContextOverrides<IArmor, IArmorGetter>(LinkCache)
                   .Where(ctx => !IsBlacklisted(ctx.Record.FormKey.ModKey.FileName) && !IsBlacklisted(ctx.ModKey.FileName))
                   .Select(ctx => (ctx.Record, ctx.ModKey))
                   .ToList());

  public Task<IEnumerable<IArmorGetter>> LoadAllArmorsAsync() =>
    Task.Run<IEnumerable<IArmorGetter>>(() =>
      LinkCache is null
        ? []
        : LinkCache.WinningOverrides<IArmorGetter>()
                   .Where(armor => !IsBlacklisted(armor.FormKey.ModKey.FileName))
                   .ToList());

  public Task<IEnumerable<IOutfitGetter>> LoadOutfitsFromPluginAsync(string pluginFileName) =>
    LoadRecordsFromPluginAsync(pluginFileName, mod => mod.Outfits);

  public Task<IEnumerable<ILeveledItemGetter>> LoadLeveledItemsFromPluginAsync(string pluginFileName) =>
    LoadRecordsFromPluginAsync(pluginFileName, mod => mod.LeveledItems);

  public Task<IEnumerable<ILeveledItemGetter>> LoadAllLeveledItemsAsync() =>
    Task.Run<IEnumerable<ILeveledItemGetter>>(() =>
      LinkCache is null
        ? []
        : LinkCache.WinningOverrides<ILeveledItemGetter>()
                   .Where(list => !IsBlacklisted(list.FormKey.ModKey.FileName))
                   .ToList());

  private async Task<IEnumerable<T>> LoadRecordsFromPluginAsync<T>(
    string pluginFileName,
    Func<ISkyrimModGetter, IEnumerable<T>> recordSelector)
  {
    if (string.IsNullOrEmpty(DataFolderPath) || IsBlacklisted(pluginFileName))
    {
      return [];
    }

    return await Task.Run(() =>
    {
      var pluginPath = Path.Combine(DataFolderPath, pluginFileName);
      if (!File.Exists(pluginPath))
      {
        return [];
      }

      try
      {
        using var mod = SkyrimMod.CreateFromBinaryOverlay(pluginPath, GetSkyrimRelease(), Utf8ReadParameters);
        return recordSelector(mod).ToList();
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Failed to load records from plugin {Plugin}.", pluginFileName);
        return [];
      }
    });
  }

  public Task<IEnumerable<string>> GetPluginsWithArmorsOrOutfitsAsync()
  {
    if (LinkCache is null)
    {
      return Task.FromResult<IEnumerable<string>>([]);
    }

    return _pluginsWithArmorsOrOutfitsTask ??= ScanPluginsWithArmorsOrOutfitsAsync();
  }

  private async Task<IEnumerable<string>> ScanPluginsWithArmorsOrOutfitsAsync()
  {
    var sw = Stopwatch.StartNew();

    var result = await Task.Run(() =>
    {
      var plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      // Attribute each winning override to the plugin providing it, so override-only
      // plugins (e.g. stat rebalances) also appear in the plugin lists.
      foreach (var armor in LinkCache!.WinningContextOverrides<IArmor, IArmorGetter>(LinkCache))
      {
        if (IsBlacklisted(armor.Record.FormKey.ModKey.FileName) || IsBlacklisted(armor.ModKey.FileName))
        {
          continue;
        }

        if (!string.IsNullOrWhiteSpace(armor.Record.Name.SafeString(armor.Record)))
        {
          plugins.Add(armor.ModKey.FileName);
        }
      }

      foreach (var fileName in LinkCache.WinningContextOverrides<IOutfit, IOutfitGetter>(LinkCache)
                     .Select(outfit => outfit.ModKey.FileName)
                     .Where(fn => !IsBlacklisted(fn)))
      {
        plugins.Add(fileName);
      }

      return plugins.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    });

    _logger.Information(
      "GetPluginsWithArmorsOrOutfitsAsync completed in {Elapsed}ms ({Count} plugins).",
      sw.ElapsedMilliseconds,
      result.Count);

    return result;
  }

  public async Task RefreshLinkCacheAsync(string? expectedPlugin = null)
  {
    if (string.IsNullOrEmpty(DataFolderPath))
    {
      return;
    }

    var previousCount = _environment?.LoadOrder.Count ?? 0;
    _logger.Information("Refreshing LinkCache (current load order: {PreviousCount} mod(s))...", previousCount);

    _pluginsWithArmorsOrOutfitsTask = null;

    await Task.Run(() =>
    {
      // Build the replacement environment first and swap it in before disposing the old one:
      // concurrent readers may still hold the previous LinkCache, and disposing it under them
      // would throw ObjectDisposedException mid-enumeration. The stale environment is disposed
      // after the swap (last reader to have captured it may briefly outlive this, which is safe
      // for read-only link caches as long as we don't dispose while it's being built).
      var useExplicitPath = PathUtilities.HasPluginFiles(DataFolderPath);
      var oldEnvironment  = _environment;
      try
      {
        BuildEnvironment(useExplicitPath ? DataFolderPath : null);
      }
      catch (Exception ex)
      {
        _logger.Warning(
          ex,
          "BuildEnvironment with explicit path {UseExplicit} failed during refresh, falling back to auto-detection.",
          useExplicitPath);
        BuildEnvironment(null);
      }
      finally
      {
        oldEnvironment?.Dispose();
      }
    });

    var newCount = _environment?.LoadOrder.Count ?? 0;
    var diff     = newCount - previousCount;
    var diffText = diff > 0 ? $"+{diff}" : diff.ToString(CultureInfo.InvariantCulture);

    _logger.Information(
      "LinkCache refreshed. Load order: {PreviousCount} → {NewCount} mod(s) ({Diff}).",
      previousCount,
      newCount,
      diffText);

    if (!string.IsNullOrEmpty(expectedPlugin) && !string.IsNullOrEmpty(DataFolderPath))
    {
      var pluginPath = Path.Combine(DataFolderPath, expectedPlugin);
      var fileExists = File.Exists(pluginPath);

      if (fileExists)
      {
        var fileInfo = new FileInfo(pluginPath);
        _logger.Information(
          "Confirmed {Plugin} exists on disk ({Size:N0} bytes, modified {Modified:HH:mm:ss}).",
          expectedPlugin,
          fileInfo.Length,
          fileInfo.LastWriteTime);

        var inLoadOrder = _environment?.LoadOrder
                                      .Any(entry =>
                                             string.Equals(
                                               entry.Key.FileName,
                                               expectedPlugin,
                                               StringComparison.OrdinalIgnoreCase)) ?? false;

        if (!inLoadOrder)
        {
          _logger.Debug(
            "{Plugin} is not in active load order (not enabled in plugins.txt) - this is normal for newly created patches.",
            expectedPlugin);
        }
      }
      else
      {
        _logger.Warning("Expected plugin {Plugin} was NOT found at {Path}!", expectedPlugin, pluginPath);
      }
    }

    PluginsChanged?.Invoke(this, EventArgs.Empty);
  }

  public void ReleaseLinkCache()
  {
    _logger.Debug("Releasing LinkCache file handles...");
    _pluginsWithArmorsOrOutfitsTask = null;
    _environment?.Dispose();
    _environment = null;
    LinkCache    = null;
  }

  public HashSet<ModKey> GetLoadOrderModKeys() =>
    _environment?.LoadOrder
                .Select(entry => entry.Key)
                .ToHashSet() ?? [];

  public bool IsPluginInLoadOrder(string pluginFileName) =>
    _environment?.LoadOrder
                .Any(entry => string.Equals(entry.Key.FileName, pluginFileName, StringComparison.OrdinalIgnoreCase)) ??
    false;
}
