using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Utilities;

public static class RecordLoader
{
  /// <summary>
  ///   Loads the winning override of every record together with the mod supplying that override,
  ///   so view models attribute records to the plugin users see (e.g. an overhaul ESP overriding
  ///   a vanilla record) rather than the mod that first defined the FormKey.
  /// </summary>
  /// <typeparam name="TSetter">The mutable record type (e.g. <c>IFaction</c>).</typeparam>
  /// <typeparam name="TRecord">The getter record type (e.g. <c>IFactionGetter</c>).</typeparam>
  /// <typeparam name="TViewModel">The view model produced for each record.</typeparam>
  public static List<TViewModel> LoadRecords<TSetter, TRecord, TViewModel>(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<TRecord, ModKey, TViewModel> createViewModel,
    Func<TViewModel, string> getDisplayName,
    Func<ModKey, bool> isBlacklisted,
    bool requireEditorId = true)
    where TSetter : class, TRecord, ISkyrimMajorRecord
    where TRecord : class, ISkyrimMajorRecordGetter
    where TViewModel : class
  {
    List<TViewModel> results;
    var query = WinningOverridesWithContext<TSetter, TRecord>(linkCache, isBlacklisted);

    if (requireEditorId)
    {
      query = query.Where(r => !string.IsNullOrWhiteSpace(r.Record.EditorID));
    }

    try
    {
      results = query
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .Select(r => createViewModel(r.Record, r.SourceMod))
                .OrderBy(getDisplayName)
                .ToList();
    }
    catch (AggregateException ex)
    {
      Log.Warning(
        ex,
        "Encountered errors while loading {RecordType} records. Processing non-corrupt records only.",
        typeof(TRecord).Name);

      foreach (var inner in ex.InnerExceptions)
      {
        if (inner is RecordException recEx)
        {
          Log.Error(
            "Skipping corrupted plugin {PluginName}: {ErrorMessage}",
            recEx.ModKey?.FileName ?? "Unknown",
            recEx.Message);
        }
      }

      results = SafeLoadRecords(query, createViewModel, getDisplayName);
    }

    return results;
  }

  /// <summary>
  ///   Loads the winning override of every record together with the mod supplying that override.
  /// </summary>
  /// <typeparam name="TSetter">The mutable record type (e.g. <c>IOutfit</c>).</typeparam>
  /// <typeparam name="TRecord">The getter record type (e.g. <c>IOutfitGetter</c>).</typeparam>
  public static List<(TRecord Record, ModKey SourceMod)> LoadRawRecords<TSetter, TRecord>(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted,
    bool requireEditorId = false)
    where TSetter : class, TRecord, ISkyrimMajorRecord
    where TRecord : class, ISkyrimMajorRecordGetter
  {
    List<(TRecord Record, ModKey SourceMod)> results;
    var query = WinningOverridesWithContext<TSetter, TRecord>(linkCache, isBlacklisted);

    if (requireEditorId)
    {
      query = query.Where(r => !string.IsNullOrWhiteSpace(r.Record.EditorID));
    }

    try
    {
      results = query
                .AsParallel()
                .WithDegreeOfParallelism(Environment.ProcessorCount)
                .ToList();
    }
    catch (AggregateException ex)
    {
      Log.Warning(
        ex,
        "Encountered errors while loading {RecordType} records. Processing non-corrupt records only.",
        typeof(TRecord).Name);

      foreach (var inner in ex.InnerExceptions)
      {
        if (inner is RecordException recEx)
        {
          Log.Error(
            "Skipping corrupted plugin {PluginName}: {ErrorMessage}",
            recEx.ModKey?.FileName ?? "Unknown",
            recEx.Message);
        }
      }

      results = SafeLoadRawRecords(query);
    }

    return results;
  }

  private static IEnumerable<(TRecord Record, ModKey SourceMod)> WinningOverridesWithContext<TSetter, TRecord>(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted)
    where TSetter : class, TRecord, ISkyrimMajorRecord
    where TRecord : class, ISkyrimMajorRecordGetter =>
    linkCache.WinningContextOverrides<TSetter, TRecord>(linkCache)
             .Where(ctx => !isBlacklisted(ctx.Record.FormKey.ModKey) && !isBlacklisted(ctx.ModKey))
             .Select(ctx => (ctx.Record, ctx.ModKey));

  private static List<TViewModel> SafeLoadRecords<TRecord, TViewModel>(
    IEnumerable<(TRecord Record, ModKey SourceMod)> query,
    Func<TRecord, ModKey, TViewModel> createViewModel,
    Func<TViewModel, string> getDisplayName)
    where TRecord : class, ISkyrimMajorRecordGetter
    where TViewModel : class
  {
    var results = new List<TViewModel>();

    foreach (var (record, sourceMod) in query)
    {
      try
      {
        var viewModel = createViewModel(record, sourceMod);
        results.Add(viewModel);
      }
      catch (Exception ex)
      {
        Log.Warning(
          ex,
          "Failed to load record {EditorID} from {Plugin}",
          record.EditorID ?? "Unknown",
          sourceMod.FileName);
      }
    }

    return [.. results.OrderBy(getDisplayName)];
  }

  private static List<(TRecord Record, ModKey SourceMod)> SafeLoadRawRecords<TRecord>(
    IEnumerable<(TRecord Record, ModKey SourceMod)> query)
    where TRecord : class, ISkyrimMajorRecordGetter
  {
    var results = new List<(TRecord Record, ModKey SourceMod)>();

    foreach (var (record, sourceMod) in query)
    {
      try
      {
        _ = record.EditorID;
        results.Add((record, sourceMod));
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Failed to load record from {Plugin}", sourceMod.FileName);
      }
    }

    return results;
  }
}
