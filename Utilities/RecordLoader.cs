using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

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
        var query = linkCache.WinningOverrides<TRecord>()
            .AsParallel()
            .WithDegreeOfParallelism(Environment.ProcessorCount)
            .Where(r => !isBlacklisted(r.FormKey.ModKey));

        if (requireEditorId)
        {
            query = query.Where(r => !string.IsNullOrWhiteSpace(r.EditorID));
        }

        return query
            .Select(createViewModel)
            .OrderBy(getDisplayName)
            .ToList();
    }

    public static List<TRecord> LoadRawRecords<TRecord>(
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache,
        Func<ModKey, bool> isBlacklisted,
        bool requireEditorId = false)
        where TRecord : class, ISkyrimMajorRecordGetter
    {
        var query = linkCache.WinningOverrides<TRecord>()
            .AsParallel()
            .WithDegreeOfParallelism(Environment.ProcessorCount)
            .Where(r => !isBlacklisted(r.FormKey.ModKey));

        if (requireEditorId)
        {
            query = query.Where(r => !string.IsNullOrWhiteSpace(r.EditorID));
        }

        return query.ToList();
    }
}
