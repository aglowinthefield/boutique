using System.IO;
using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

/// <summary>
/// Creates and modifies Skyrim patch plugins by copying armor stats, keywords, enchantments,
/// and tempering recipes from gameplay mods to cosmetic mods, and generates outfit records.
/// </summary>
public class PatchingService(MutagenService mutagenService, ILoggingService loggingService)
{
  private const    uint    MinimumFormId = 0x800;
  private static readonly ModKey SkyrimMaster = ModKey.FromFileName("Skyrim.esm");
  private readonly ILogger _logger       = loggingService.ForContext<PatchingService>();

  private void RequireInitialized()
  {
    if (!mutagenService.IsInitialized)
    {
      throw new InvalidOperationException("Mutagen service is not initialized. Please set the Skyrim data path first.");
    }
  }

  private (SkyrimMod patchMod, HashSet<ModKey> requiredMasters) LoadOrCreatePatch(
    string outputPath,
    string operationName)
  {
    var fileName = Path.GetFileName(outputPath);
    if (!ModKey.TryFromFileName(fileName, out var modKey))
    {
      throw new InvalidOperationException(
        $"'{fileName}' is not a valid plugin name. The patch file name must end in .esp, .esm, or .esl.");
    }

    SkyrimMod patchMod;

    if (File.Exists(outputPath))
    {
      _logger.Information("Loading existing patch at {OutputPath} for {Operation}.", outputPath, operationName);
      patchMod = SkyrimMod.CreateFromBinary(outputPath, mutagenService.SkyrimRelease);
    }
    else
    {
      patchMod = new SkyrimMod(modKey, mutagenService.SkyrimRelease);
    }

    EnsureMinimumFormId(patchMod);

    var requiredMasters = new HashSet<ModKey>();
    var existingMasters = patchMod.ModHeader.MasterReferences.Select(m => m.Master);
    requiredMasters.UnionWith(existingMasters);

    return (patchMod, requiredMasters);
  }

  private void FinalizePatch(
    SkyrimMod patchMod,
    HashSet<ModKey> requiredMasters,
    string outputPath,
    IProgress<(int current, int total, string message)>? progress)
  {
    requiredMasters.Add(SkyrimMaster);

    EnsureMasters(patchMod, requiredMasters);

    var actuallyRequiredMasters = CollectRequiredMasters(patchMod, []);
    actuallyRequiredMasters.Add(SkyrimMaster);
    CleanupMasterReferences(patchMod, actuallyRequiredMasters);

    TryApplyEslFlag(patchMod);

    progress?.Report((1, 1, "Writing patch file..."));
    mutagenService.ReleaseLinkCache();

    WritePatchWithRetry(patchMod, outputPath, actuallyRequiredMasters);
  }

  private async Task RefreshAfterWriteAsync(
    string outputPath,
    IProgress<(int current, int total, string message)>? progress)
  {
    progress?.Report((1, 1, "Refreshing load order..."));
    var pluginName = Path.GetFileName(outputPath);
    await mutagenService.RefreshLinkCacheAsync(pluginName);
  }

  public async Task<(bool success, string message)> CreatePatchAsync(
    IEnumerable<ArmorMatch> matches,
    string outputPath,
    IProgress<(int current, int total, string message)>? progress = null)
  {
    var result = await Task.Run(() =>
    {
      try
      {
        RequireInitialized();

        var validMatches = matches.Where(m => m.TargetArmor != null || m.IsGlamOnly).ToList();

        _logger.Information(
          "Beginning patch creation. Destination: {OutputPath}. Matches: {MatchCount}",
          outputPath,
          validMatches.Count);

        if (validMatches.Count == 0)
        {
          _logger.Warning("Patch creation aborted — no valid matches were provided.");
          return (false, "No valid matches to patch.");
        }

        var (patchMod, requiredMasters) = LoadOrCreatePatch(outputPath, "armor patch");
        _logger.Information("Loaded patch containing {ArmorCount} armor overrides.", patchMod.Armors.Count);

        var current = 0;
        var total   = validMatches.Count;

        foreach (var match in validMatches)
        {
          current++;
          var sourceName = match.SourceArmor.Name.SafeString(match.SourceArmor) ?? match.SourceArmor.EditorID ?? "Unknown";
          progress?.Report((current, total, $"Patching {sourceName}..."));

          var patchedArmor = patchMod.Armors.GetOrAddAsOverride(match.SourceArmor);

          requiredMasters.Add(match.SourceArmor.FormKey.ModKey);
          if (match.IsGlamOnly)
          {
            ApplyGlamOnlyAdjustments(patchedArmor);
            continue;
          }

          var targetArmor = match.TargetArmor!;
          requiredMasters.Add(targetArmor.FormKey.ModKey);

          CopyArmorStats(patchedArmor, targetArmor);
          CopyKeywords(patchedArmor, targetArmor);
          CopyEnchantment(patchedArmor, targetArmor);
        }

        FinalizePatch(patchMod, requiredMasters, outputPath, progress);

        _logger.Information("Patch successfully written to {OutputPath}", outputPath);

        return (true, $"Successfully created patch with {validMatches.Count} armor(s) at {outputPath}");
      }
      catch (InvalidOperationException ex)
      {
        return (false, ex.Message);
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error creating patch destined for {OutputPath}", outputPath);
        return (false, $"Error creating patch: {ex.Message}");
      }
    });

    if (result.Item1)
    {
      await RefreshAfterWriteAsync(outputPath, progress);
    }
    else
    {
      await mutagenService.RefreshLinkCacheAsync();
    }

    return result;
  }

  public async Task<(bool success, string message, IReadOnlyList<OutfitCreationResult> results)>
    CreateOrUpdateOutfitsAsync(
      IEnumerable<OutfitCreationRequest> outfits,
      string outputPath,
      IProgress<(int current, int total, string message)>? progress = null)
  {
    var (success, message, outfitResults, _) =
      await SaveOutfitsAndLeveledListsAsync(outfits, [], outputPath, progress);
    return (success, message, outfitResults);
  }

  public async Task<(bool success, string message, IReadOnlyList<LeveledListCreationResult> results)>
    CreateOrUpdateLeveledListsAsync(
      IEnumerable<LeveledListCreationRequest> lists,
      string outputPath,
      IProgress<(int current, int total, string message)>? progress = null)
  {
    var (success, message, _, listResults) =
      await SaveOutfitsAndLeveledListsAsync([], lists, outputPath, progress);
    return (success, message, listResults);
  }

  /// <summary>
  /// Writes outfits and leveled lists into a single patch. Leveled lists are written first so that
  /// outfits (and other leveled lists) can reference lists created in the same batch via their draft id.
  /// </summary>
  public async Task<(bool success, string message,
      IReadOnlyList<OutfitCreationResult> outfitResults,
      IReadOnlyList<LeveledListCreationResult> listResults)>
    SaveOutfitsAndLeveledListsAsync(
      IEnumerable<OutfitCreationRequest> outfits,
      IEnumerable<LeveledListCreationRequest> lists,
      string outputPath,
      IProgress<(int current, int total, string message)>? progress = null)
  {
    var result = await Task.Run(() =>
    {
      try
      {
        RequireInitialized();

        var outfitList = outfits.ToList();
        var listList   = lists.ToList();
        if (outfitList.Count == 0 && listList.Count == 0)
        {
          return (false, "Nothing to save.",
                  (IReadOnlyList<OutfitCreationResult>)[],
                  (IReadOnlyList<LeveledListCreationResult>)[]);
        }

        _logger.Information(
          "Beginning save. Destination: {OutputPath}. Outfits={OutfitCount}, LeveledLists={ListCount}",
          outputPath,
          outfitList.Count,
          listList.Count);

        var (patchMod, requiredMasters) = LoadOrCreatePatch(outputPath, "outfit/leveled list save");

        var draftMap    = new Dictionary<Guid, FormKey>();
        var listResults = WriteLeveledLists(patchMod, requiredMasters, listList, draftMap, progress);
        var outfitResults = WriteOutfits(patchMod, requiredMasters, outfitList, draftMap, progress);

        FinalizePatch(patchMod, requiredMasters, outputPath, progress);

        _logger.Information("Save completed successfully. File: {OutputPath}", outputPath);

        var savedParts = new List<string>();
        if (outfitResults.Count > 0)
        {
          savedParts.Add($"{outfitResults.Count} outfit(s)");
        }

        if (listResults.Count > 0)
        {
          savedParts.Add($"{listResults.Count} leveled list(s)");
        }

        var summary = savedParts.Count > 0 ? string.Join(" and ", savedParts) : "0 records";
        return (true, $"Saved {summary} to {outputPath}",
                (IReadOnlyList<OutfitCreationResult>)outfitResults,
                (IReadOnlyList<LeveledListCreationResult>)listResults);
      }
      catch (InvalidOperationException ex)
      {
        return (false, ex.Message,
                (IReadOnlyList<OutfitCreationResult>)[],
                (IReadOnlyList<LeveledListCreationResult>)[]);
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error saving outfits/leveled lists destined for {OutputPath}", outputPath);
        return (false, $"Error saving: {ex.Message}",
                (IReadOnlyList<OutfitCreationResult>)[],
                (IReadOnlyList<LeveledListCreationResult>)[]);
      }
    });

    if (result.Item1)
    {
      await RefreshAfterWriteAsync(outputPath, progress);
    }
    else
    {
      await mutagenService.RefreshLinkCacheAsync();
    }

    return result;
  }

  private List<OutfitCreationResult> WriteOutfits(
    SkyrimMod patchMod,
    HashSet<ModKey> requiredMasters,
    IReadOnlyList<OutfitCreationRequest> outfitList,
    Dictionary<Guid, FormKey> draftMap,
    IProgress<(int current, int total, string message)>? progress)
  {
    var results = new List<OutfitCreationResult>();
    var total   = outfitList.Count;
    var current = 0;

    foreach (var request in outfitList)
    {
      current++;
      progress?.Report((current, total, $"Writing outfit {request.Name}..."));

      var contentCount = request.Pieces.Count + (request.LeveledLists?.Count ?? 0);

      if (request.IsOverride && request.ExistingFormKey.HasValue)
      {
        if (!mutagenService.LinkCache!.TryResolve<IOutfitGetter>(
              request.ExistingFormKey.Value,
              out var sourceOutfit))
        {
          _logger.Warning(
            "Override outfit {FormKey} not found in LinkCache, skipping.",
            request.ExistingFormKey.Value);
          continue;
        }

        var overrideOutfit = patchMod.Outfits.GetOrAddAsOverride(sourceOutfit);
        requiredMasters.Add(sourceOutfit.FormKey.ModKey);

        if (request.OverrideSourceMod.HasValue)
        {
          requiredMasters.Add(request.OverrideSourceMod.Value);
        }

        PopulateOutfitItems(overrideOutfit.Items ??= [], request, requiredMasters, draftMap);

        results.Add(new OutfitCreationResult(request.EditorId, overrideOutfit.FormKey));
        _logger.Information(
          "Created override for outfit {EditorId} ({FormKey}) with {Count} item(s).",
          request.EditorId,
          request.ExistingFormKey.Value,
          contentCount);
        continue;
      }

      Outfit? existing = null;
      if (request.ExistingFormKey.HasValue)
      {
        existing = patchMod.Outfits.FirstOrDefault(o => o.FormKey == request.ExistingFormKey.Value);
      }

      existing ??= patchMod.Outfits
                           .FirstOrDefault(o =>
                                             string.Equals(
                                               o.EditorID,
                                               request.EditorId,
                                               StringComparison.OrdinalIgnoreCase));

      if (contentCount == 0)
      {
        if (existing != null)
        {
          patchMod.Outfits.Remove(existing);
          _logger.Information("Deleted outfit {EditorId}.", request.EditorId);
          results.Add(new OutfitCreationResult(request.EditorId, existing.FormKey));
        }
        else
        {
          _logger.Debug("Skipping deletion of {EditorId} — not in patch.", request.EditorId);
        }

        continue;
      }

      Outfit outfit;
      if (existing != null)
      {
        outfit = existing;
        if (!string.Equals(outfit.EditorID, request.EditorId, StringComparison.OrdinalIgnoreCase))
        {
          _logger.Information(
            "Renaming outfit {OldEditorId} to {NewEditorId}.",
            outfit.EditorID,
            request.EditorId);
          outfit.EditorID = request.EditorId;
        }

        _logger.Information(
          "Updating existing outfit {EditorId} with {Count} item(s).",
          request.EditorId,
          contentCount);
      }
      else
      {
        outfit          = patchMod.Outfits.AddNew();
        outfit.EditorID = request.EditorId;
        _logger.Information(
          "Creating new outfit {EditorId} with {Count} item(s).",
          request.EditorId,
          contentCount);
      }

      PopulateOutfitItems(outfit.Items ??= [], request, requiredMasters, draftMap);

      results.Add(new OutfitCreationResult(request.EditorId, outfit.FormKey));
    }

    return results;
  }

  private void PopulateOutfitItems(
    IList<IFormLinkGetter<IOutfitTargetGetter>> items,
    OutfitCreationRequest request,
    HashSet<ModKey> requiredMasters,
    Dictionary<Guid, FormKey> draftMap)
  {
    items.Clear();

    foreach (var armor in request.Pieces)
    {
      items.Add(armor.ToLink());
      requiredMasters.Add(armor.FormKey.ModKey);
    }

    if (request.LeveledLists == null)
    {
      return;
    }

    foreach (var listRef in request.LeveledLists)
    {
      var formKey = ResolveItemRef(listRef.ExistingFormKey, listRef.DraftId, draftMap);
      if (formKey == null)
      {
        _logger.Warning("Outfit {EditorId} references an unresolved leveled list; skipping.", request.EditorId);
        continue;
      }

      items.Add(formKey.Value.ToLink<IOutfitTargetGetter>());
      requiredMasters.Add(formKey.Value.ModKey);
    }
  }

  private List<LeveledListCreationResult> WriteLeveledLists(
    SkyrimMod patchMod,
    HashSet<ModKey> requiredMasters,
    IReadOnlyList<LeveledListCreationRequest> listRequests,
    Dictionary<Guid, FormKey> draftMap,
    IProgress<(int current, int total, string message)>? progress)
  {
    var results = new List<LeveledListCreationResult>();
    var pending = new List<(LeveledListCreationRequest request, LeveledItem item)>();

    foreach (var request in listRequests)
    {
      var existing = ResolveExistingLeveledItem(patchMod, request, requiredMasters);

      if (request.Entries.Count == 0)
      {
        if (existing != null)
        {
          patchMod.LeveledItems.Remove(existing);
          _logger.Information("Deleted leveled list {EditorId}.", request.EditorId);
          results.Add(new LeveledListCreationResult(request.EditorId, existing.FormKey));
        }
        else
        {
          _logger.Debug("Skipping deletion of {EditorId} — not in patch.", request.EditorId);
        }

        continue;
      }

      LeveledItem item;
      if (existing != null)
      {
        item = existing;
        if (!string.Equals(item.EditorID, request.EditorId, StringComparison.OrdinalIgnoreCase))
        {
          _logger.Information(
            "Renaming leveled list {OldEditorId} to {NewEditorId}.",
            item.EditorID,
            request.EditorId);
          item.EditorID = request.EditorId;
        }
      }
      else
      {
        item          = patchMod.LeveledItems.AddNew();
        item.EditorID = request.EditorId;
      }

      if (request.DraftId.HasValue)
      {
        draftMap[request.DraftId.Value] = item.FormKey;
      }

      pending.Add((request, item));
    }

    var total   = pending.Count;
    var current = 0;
    foreach (var (request, item) in pending)
    {
      current++;
      progress?.Report((current, total, $"Writing leveled list {request.EditorId}..."));

      item.Flags = request.Flags;

      var entries = item.Entries ??= [];
      entries.Clear();
      foreach (var entry in request.Entries)
      {
        var formKey = ResolveItemRef(entry.ItemFormKey, entry.DraftListId, draftMap);
        if (formKey == null)
        {
          _logger.Warning("Leveled list {EditorId} has an unresolved entry; skipping.", request.EditorId);
          continue;
        }

        entries.Add(new LeveledItemEntry
                    {
                      Data = new LeveledItemEntryData
                             {
                               Reference = formKey.Value.ToLink<IItemGetter>(),
                               Level     = entry.Level,
                               Count     = entry.Count
                             }
                    });
        requiredMasters.Add(formKey.Value.ModKey);
      }

      results.Add(new LeveledListCreationResult(request.EditorId, item.FormKey));
    }

    return results;
  }

  private LeveledItem? ResolveExistingLeveledItem(
    SkyrimMod patchMod,
    LeveledListCreationRequest request,
    HashSet<ModKey> requiredMasters)
  {
    LeveledItem? existing = null;

    if (request.ExistingFormKey.HasValue)
    {
      existing = patchMod.LeveledItems.FirstOrDefault(l => l.FormKey == request.ExistingFormKey.Value);

      if (existing == null &&
          mutagenService.LinkCache!.TryResolve<ILeveledItemGetter>(
            request.ExistingFormKey.Value,
            out var sourceList))
      {
        existing = patchMod.LeveledItems.GetOrAddAsOverride(sourceList);
        requiredMasters.Add(sourceList.FormKey.ModKey);
      }
    }

    existing ??= patchMod.LeveledItems
                         .FirstOrDefault(l =>
                                           string.Equals(
                                             l.EditorID,
                                             request.EditorId,
                                             StringComparison.OrdinalIgnoreCase));

    return existing;
  }

  private static FormKey? ResolveItemRef(FormKey? formKey, Guid? draftId, Dictionary<Guid, FormKey> draftMap)
  {
    if (draftId.HasValue && draftMap.TryGetValue(draftId.Value, out var mapped))
    {
      return mapped;
    }

    if (formKey.HasValue && formKey.Value != FormKey.Null)
    {
      return formKey.Value;
    }

    return null;
  }

  private void EnsureMinimumFormId(SkyrimMod patchMod)
  {
    var current = patchMod.ModHeader.Stats.NextFormID;
    if (current < MinimumFormId)
    {
      patchMod.ModHeader.Stats.NextFormID = MinimumFormId;
      _logger.Warning(
        "NextFormID was {Current:X}, bumped to {Minimum:X} for ESL compatibility.",
        current,
        MinimumFormId);
    }
  }

  private static void ApplyGlamOnlyAdjustments(Armor target) => target.ArmorRating = 0;

  private static void CopyArmorStats(Armor target, IArmorGetter source)
  {
    target.ArmorRating = source.ArmorRating;
    target.Value       = source.Value;
    target.Weight      = source.Weight;
  }

  private static void CopyKeywords(Armor target, IArmorGetter source)
  {
    if (source.Keywords is null)
    {
      return;
    }

    target.Keywords = [.. source.Keywords];
  }

  private static void CopyEnchantment(Armor target, IArmorGetter source)
  {
    if (source.ObjectEffect.FormKey != FormKey.Null)
    {
      target.ObjectEffect.SetTo(source.ObjectEffect);
    }
    else
    {
      target.ObjectEffect.Clear();
    }

    target.EnchantmentAmount = source.EnchantmentAmount;
  }

  private void EnsureMasters(SkyrimMod patchMod, HashSet<ModKey> requiredMasters)
  {
    var masterList = patchMod.ModHeader.MasterReferences;

    var existing = masterList.Select(m => m.Master).ToHashSet();

    foreach (var master in requiredMasters)
    {
      if (master == patchMod.ModKey || master.IsNull)
      {
        continue;
      }

      if (!existing.Add(master))
      {
        continue;
      }

      masterList.Add(new MasterReference { Master = master });
      _logger.Debug("Added master {Master} to patch header.", master);
    }

    _logger.Information(
      "Patch master list: {Masters}",
      string.Join(", ", masterList.Select(m => m.Master.FileName)));
  }

  private static bool MasterExistsOnDisk(string dataFolder, ModKey master)
  {
    var masterPath = Path.Combine(dataFolder, master.FileName);
    return File.Exists(masterPath);
  }

  private void WritePatchWithRetry(SkyrimMod patchMod, string outputPath, HashSet<ModKey> extraMasters)
  {
    var tempPath = Path.Combine(
      Path.GetDirectoryName(outputPath)!,
      "_temp_" + Path.GetFileName(outputPath));

    try
    {
      _logger.Debug(
        "Masters before write: {Masters}",
        string.Join(", ", patchMod.ModHeader.MasterReferences.Select(m => m.Master.FileName)));
      _logger.Debug(
        "Extra masters to include: {ExtraMasters}",
        string.Join(", ", extraMasters.Select(m => m.FileName)));

      patchMod.BeginWrite
              .ToPath(tempPath)
              .WithNoLoadOrder()
              .WithExtraIncludedMasters(extraMasters)
              .NoModKeySync()
              .Write();

      using (var writtenMod =
        SkyrimMod.CreateFromBinaryOverlay(
          tempPath,
          mutagenService.SkyrimRelease,
          mutagenService.Utf8ReadParameters))
      {
        _logger.Information(
          "Masters after write: {Masters}",
          string.Join(", ", writtenMod.ModHeader.MasterReferences.Select(m => m.Master.FileName)));
      }

      const int  maxRetries     = 10;
      const int  initialDelayMs = 100;
      Exception? lastException  = null;

      for (var attempt = 1; attempt <= maxRetries; attempt++)
      {
        try
        {
          File.Move(tempPath, outputPath, true);
          return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
          lastException = ex;
          if (attempt < maxRetries)
          {
            var delay = initialDelayMs * attempt;
            _logger.Debug(
              ex,
              "File replace attempt {Attempt}/{Max} failed, retrying in {Delay}ms...",
              attempt,
              maxRetries,
              delay);
            Thread.Sleep(delay);
          }
        }
      }

      var fileName = Path.GetFileName(outputPath);
      throw lastException switch
      {
        UnauthorizedAccessException => new InvalidOperationException(
          $"Cannot write to '{fileName}'. " +
          "The file may be locked by another program (Skyrim, xEdit, etc). " +
          "Please close any programs that might have it open and try again."),
        IOException ioEx => new InvalidOperationException(
          $"Cannot write to '{fileName}': {ioEx.Message}. " +
          "The file may be locked by another program."),
        _ => new InvalidOperationException($"Cannot write to '{fileName}': Unknown error.")
      };
    }
    finally
    {
      try
      {
        if (File.Exists(tempPath))
        {
          File.Delete(tempPath);
        }
      }
      catch (Exception ex)
      {
        _logger.Warning(ex, "Failed to clean up temp file {TempPath}", tempPath);
      }
    }
  }

  private void TryApplyEslFlag(SkyrimMod patchMod)
  {
    if (mutagenService.SkyrimRelease == SkyrimRelease.SkyrimVR)
    {
      _logger.Information("ESL flag skipped — Skyrim VR does not natively support ESL plugins.");
      return;
    }

    const int eslRecordLimit = 2048;

    var totalRecordCount = patchMod.EnumerateMajorRecords().Count();
    var newRecordCount = patchMod.EnumerateMajorRecords()
                                 .Count(r => r.FormKey.ModKey == patchMod.ModKey);

    if (totalRecordCount < eslRecordLimit)
    {
      patchMod.ModHeader.Flags |= SkyrimModHeader.HeaderFlag.Small;
      _logger.Information(
        "ESL flag applied. Total records: {Total} ({New} new, {Override} overrides), limit: {Limit}.",
        totalRecordCount,
        newRecordCount,
        totalRecordCount - newRecordCount,
        eslRecordLimit);
    }
    else
    {
      _logger.Warning(
        "ESL flag NOT applied. Total record count {Total} exceeds limit of {Limit}.",
        totalRecordCount,
        eslRecordLimit);
    }
  }

  public async Task<MissingMastersResult> CheckMissingMastersAsync(string patchPath)
  {
    return await Task.Run(() =>
    {
      if (!File.Exists(patchPath))
      {
        _logger.Debug("Patch file does not exist at {Path}, no missing masters check needed.", patchPath);
        return new MissingMastersResult(false, [], []);
      }

      try
      {
        using var patchMod = SkyrimMod.CreateFromBinaryOverlay(
          patchPath,
          mutagenService.SkyrimRelease,
          mutagenService.Utf8ReadParameters);
        var dataFolder       = mutagenService.DataFolderPath ?? string.Empty;
        var loadOrderModKeys = mutagenService.GetLoadOrderModKeys();
        var masterRefs       = patchMod.ModHeader.MasterReferences.Select(m => m.Master).ToList();
        var missingMasters   = new List<ModKey>();
        var patchModKey      = patchMod.ModKey;

        foreach (var master in masterRefs)
        {
          if (master == patchModKey)
          {
            continue;
          }

          if (loadOrderModKeys.Contains(master))
          {
            continue;
          }

          if (!string.IsNullOrEmpty(dataFolder) && MasterExistsOnDisk(dataFolder, master))
          {
            _logger.Debug(
              "Master {Master} not in load order but exists on disk, treating as present.",
              master.FileName);
            continue;
          }

          missingMasters.Add(master);
          _logger.Warning("Missing master detected: {Master}", master.FileName);
        }

        if (missingMasters.Count == 0)
        {
          _logger.Debug("All masters present for patch {Patch}.", patchPath);
          return new MissingMastersResult(false, [], []);
        }

        var missingMasterSet        = missingMasters.ToHashSet();
        var affectedOutfitsByMaster = new Dictionary<ModKey, List<AffectedOutfitInfo>>();
        var validOutfitsList        = new List<IOutfitGetter>();

        foreach (var outfit in patchMod.Outfits)
        {
          var orphanedFormKeys = new List<FormKey>();
          var affectingMasters = new HashSet<ModKey>();

          if (outfit.Items != null)
          {
            foreach (var formKey in outfit.Items
                       .Select(itemLink => itemLink.FormKeyNullable)
                       .Where(fk => fk.HasValue && fk.Value != FormKey.Null)
                       .Select(fk => fk!.Value))
            {
              var itemModKey = formKey.ModKey;
              if (!missingMasterSet.Contains(itemModKey))
              {
                continue;
              }

              orphanedFormKeys.Add(formKey);
              affectingMasters.Add(itemModKey);
            }
          }

          if (orphanedFormKeys.Count > 0)
          {
            var affectedInfo = new AffectedOutfitInfo(
              outfit.FormKey,
              outfit.EditorID,
              orphanedFormKeys);

            foreach (var master in affectingMasters)
            {
              if (!affectedOutfitsByMaster.TryGetValue(master, out var list))
              {
                list                            = [];
                affectedOutfitsByMaster[master] = list;
              }

              list.Add(affectedInfo);
            }
          }
          else
          {
            validOutfitsList.Add(outfit);
          }
        }

        var missingMasterInfos = missingMasters
            .ConvertAll(m => new MissingMasterInfo(
                          m,
                          affectedOutfitsByMaster.TryGetValue(m, out var list) ? list : []))
          ;

        var allAffectedOutfits = affectedOutfitsByMaster
                                 .SelectMany(kvp => kvp.Value)
                                 .DistinctBy(a => a.FormKey)
                                 .ToList();

        _logger.Information(
          "Missing masters check complete: {MissingCount} missing master(s), {AffectedCount} affected outfit(s).",
          missingMasters.Count,
          allAffectedOutfits.Count);

        return new MissingMastersResult(true, missingMasterInfos, allAffectedOutfits);
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error checking missing masters for patch {Path}.", patchPath);
        return new MissingMastersResult(false, [], []);
      }
    });
  }

  public async Task<(bool success, string message)> CleanPatchMissingMastersAsync(
    string patchPath,
    IReadOnlyList<AffectedOutfitInfo> outfitsToRemove)
  {
    return await Task.Run(() =>
    {
      try
      {
        if (!File.Exists(patchPath))
        {
          return (false, "Patch file does not exist.");
        }

        _logger.Information(
          "Cleaning patch {Path}: removing {Count} outfit(s) with missing masters.",
          patchPath,
          outfitsToRemove.Count);

        var patchMod           = SkyrimMod.CreateFromBinary(patchPath, mutagenService.SkyrimRelease);
        var outfitsToRemoveSet = outfitsToRemove.Select(o => o.FormKey).ToHashSet();

        var removedCount = 0;
        var outfitsToKeep = patchMod.Outfits
                                    .Where(o =>
                                    {
                                      if (outfitsToRemoveSet.Contains(o.FormKey))
                                      {
                                        _logger.Debug(
                                          "Removing outfit {EditorId} ({FormKey}) due to missing masters.",
                                          o.EditorID,
                                          o.FormKey);
                                        removedCount++;
                                        return false;
                                      }

                                      return true;
                                    })
                                    .ToList();

        patchMod.Outfits.Clear();
        foreach (var outfit in outfitsToKeep)
        {
          patchMod.Outfits.Add(outfit);
        }

        var remainingMasters = CollectRequiredMasters(patchMod, outfitsToRemoveSet);
        CleanupMasterReferences(patchMod, remainingMasters);

        TryApplyEslFlag(patchMod);

        mutagenService.ReleaseLinkCache();

        WritePatchWithRetry(patchMod, patchPath, remainingMasters);

        _logger.Information("Patch cleaned successfully. Removed {Count} outfit(s).", removedCount);
        return (true, $"Successfully removed {removedCount} outfit(s) with missing masters.");
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error cleaning patch {Path}.", patchPath);
        return (false, $"Error cleaning patch: {ex.Message}");
      }
    });
  }

  private static HashSet<ModKey> CollectRequiredMasters(SkyrimMod patchMod, HashSet<FormKey> excludedOutfits)
  {
    var requiredMasters = new HashSet<ModKey>();
    var patchModKey     = patchMod.ModKey;

    void AddMaster(ModKey modKey)
    {
      if (modKey != patchModKey && !modKey.IsNull)
      {
        requiredMasters.Add(modKey);
      }
    }

    foreach (var record in patchMod.EnumerateMajorRecords())
    {
      if (record is IOutfitGetter outfit && excludedOutfits.Contains(outfit.FormKey))
      {
        continue;
      }

      AddMaster(record.FormKey.ModKey);

      switch (record)
      {
        case IOutfitGetter { Items: not null } outfitRecord:
        {
          foreach (var formKey in outfitRecord.Items
                     .Select(item => item.FormKeyNullable)
                     .Where(fk => fk.HasValue && fk.Value != FormKey.Null))
          {
            AddMaster(formKey!.Value.ModKey);
          }

          break;
        }

        case ILeveledItemGetter { Entries: not null } leveledItem:
        {
          foreach (var entry in leveledItem.Entries)
          {
            if (entry.Data?.Reference.FormKeyNullable is { } refKey && refKey != FormKey.Null)
            {
              AddMaster(refKey.ModKey);
            }
          }

          break;
        }

        case IArmorGetter armor:
        {
          if (armor.ObjectEffect.FormKeyNullable is { } enchantKey && enchantKey != FormKey.Null)
          {
            AddMaster(enchantKey.ModKey);
          }

          if (armor.Keywords != null)
          {
            foreach (var keyword in armor.Keywords)
            {
              if (keyword.FormKeyNullable is { } keywordKey && keywordKey != FormKey.Null)
              {
                AddMaster(keywordKey.ModKey);
              }
            }
          }

          break;
        }
      }
    }

    return requiredMasters;
  }

  private void CleanupMasterReferences(SkyrimMod patchMod, HashSet<ModKey> requiredMasters)
  {
    var masterList = patchMod.ModHeader.MasterReferences;
    var mastersToRemove = masterList
                          .Where(m => !requiredMasters.Contains(m.Master) && m.Master != patchMod.ModKey)
                          .ToList();

    foreach (var master in mastersToRemove)
    {
      masterList.Remove(master);
      _logger.Debug("Removed unused master {Master} from patch header.", master.Master.FileName);
    }

    _logger.Information(
      "Cleaned master list: {Masters}",
      string.Join(", ", masterList.Select(m => m.Master.FileName)));
  }
}
