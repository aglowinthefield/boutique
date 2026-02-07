using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Utilities;

public static class RecordLoader
{
  public static List<TViewModel> LoadRecords<TRecord, TViewModel>(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<TRecord, TViewModel> createViewModel,
    Func<TViewModel, string> getDisplayName,
    Func<ModKey, bool> isBlacklisted,
    bool requireEditorId = true)
    where TRecord : class, ISkyrimMajorRecordGetter
    where TViewModel : class
  {
    var results = new List<TViewModel>();
    var query = linkCache.WinningOverrides<TRecord>()
      .Where(r => !isBlacklisted(r.FormKey.ModKey));

    if (requireEditorId)
    {
      query = query.Where(r => !string.IsNullOrWhiteSpace(r.EditorID));
    }

    try
    {
      results = query
        .AsParallel()
        .WithDegreeOfParallelism(Environment.ProcessorCount)
        .Select(createViewModel)
        .OrderBy(getDisplayName)
        .ToList();
    }
    catch (AggregateException ex)
    {
      Log.Warning(ex,
        "Encountered errors while loading {RecordType} records. Processing non-corrupt records only.",
        typeof(TRecord).Name);

      foreach (var inner in ex.InnerExceptions)
      {
        if (inner is RecordException recEx)
        {
          Log.Error("Skipping corrupted plugin {PluginName}: {ErrorMessage}",
            recEx.ModKey?.FileName ?? "Unknown",
            recEx.Message);
        }
      }

      results = SafeLoadRecords(query, createViewModel, getDisplayName);
    }

    return results;
  }

  public static List<TRecord> LoadRawRecords<TRecord>(
    ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
    Func<ModKey, bool> isBlacklisted,
    bool requireEditorId = false)
    where TRecord : class, ISkyrimMajorRecordGetter
  {
    var results = new List<TRecord>();
    var query = linkCache.WinningOverrides<TRecord>()
      .Where(r => !isBlacklisted(r.FormKey.ModKey));

    if (requireEditorId)
    {
      query = query.Where(r => !string.IsNullOrWhiteSpace(r.EditorID));
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
      Log.Warning(ex,
        "Encountered errors while loading {RecordType} records. Processing non-corrupt records only.",
        typeof(TRecord).Name);

      foreach (var inner in ex.InnerExceptions)
      {
        if (inner is RecordException recEx)
        {
          Log.Error("Skipping corrupted plugin {PluginName}: {ErrorMessage}",
            recEx.ModKey?.FileName ?? "Unknown",
            recEx.Message);
        }
      }

      results = SafeLoadRawRecords(query);
    }

    return results;
  }

  private static List<TViewModel> SafeLoadRecords<TRecord, TViewModel>(
    IEnumerable<TRecord> query,
    Func<TRecord, TViewModel> createViewModel,
    Func<TViewModel, string> getDisplayName)
    where TRecord : class, ISkyrimMajorRecordGetter
    where TViewModel : class
  {
    var results = new List<TViewModel>();

    foreach (var record in query)
    {
      try
      {
        var viewModel = createViewModel(record);
        results.Add(viewModel);
      }
      catch (Exception ex)
      {
        Log.Warning(ex,
          "Failed to load record {EditorID} from {Plugin}",
          record.EditorID ?? "Unknown",
          record.FormKey.ModKey.FileName);
      }
    }

    return results.OrderBy(getDisplayName).ToList();
  }

  private static List<TRecord> SafeLoadRawRecords<TRecord>(IEnumerable<TRecord> query)
    where TRecord : class, ISkyrimMajorRecordGetter
  {
    var results = new List<TRecord>();

    foreach (var record in query)
    {
      try
      {
        var _ = record.EditorID;
        results.Add(record);
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Failed to load record from {Plugin}", record.FormKey.ModKey.FileName);
      }
    }

    return results;
  }
}
