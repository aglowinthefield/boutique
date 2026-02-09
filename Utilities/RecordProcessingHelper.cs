using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Utilities;

public static class RecordProcessingHelper
{
  public static T? TryProcessRecord<T>(
    ILogger logger,
    ISkyrimMajorRecordGetter record,
    Func<T> processor,
    string recordType) where T : class
  {
    try
    {
      return processor();
    }
    catch (Exception ex)
    {
      logger.Warning(
        ex,
        "Failed to process {RecordType} {EditorID} from {Plugin}",
        recordType,
        record.EditorID ?? "Unknown",
        record.FormKey.ModKey.FileName);
      return null;
    }
  }

  public static T? TryProcessRecord<T>(
    ILogger logger,
    ISkyrimMajorRecordGetter record,
    Func<T?> processor,
    string recordType) where T : struct
  {
    try
    {
      return processor();
    }
    catch (Exception ex)
    {
      logger.Warning(
        ex,
        "Failed to process {RecordType} {EditorID} from {Plugin}",
        recordType,
        record.EditorID ?? "Unknown",
        record.FormKey.ModKey.FileName);
      return null;
    }
  }

  public static void TryProcessRecord(
    ILogger logger,
    ISkyrimMajorRecordGetter record,
    Action processor,
    string recordType)
  {
    try
    {
      processor();
    }
    catch (Exception ex)
    {
      logger.Warning(
        ex,
        "Failed to process {RecordType} {EditorID} from {Plugin}",
        recordType,
        record.EditorID ?? "Unknown",
        record.FormKey.ModKey.FileName);
    }
  }
}
