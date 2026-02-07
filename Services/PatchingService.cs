using System.IO;
using Boutique.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class PatchingService(MutagenService mutagenService, ILoggingService loggingService)
{
  private const uint MinimumFormId = 0x800;
  private readonly ILogger _logger = loggingService.ForContext<PatchingService>();

  public bool ValidatePatch(IEnumerable<ArmorMatch> matches, out string validationMessage)
  {
    var matchList = matches.ToList();

    if (matchList.Count == 0)
    {
      validationMessage = "No armor matches to patch.";
      return false;
    }

    var validMatches = matchList.Where(m => m.TargetArmor != null).ToList();

    if (validMatches.Count == 0)
    {
      validationMessage = "No valid armor matches found. Please ensure target armors are selected.";
      return false;
    }

    if (!mutagenService.IsInitialized)
    {
      validationMessage = "Mutagen service is not initialized. Please set the Skyrim data path first.";
      return false;
    }

    validationMessage = $"Ready to patch {validMatches.Count} armor(s).";
    return true;
  }

  private void RequireInitialized()
  {
    if (!mutagenService.IsInitialized)
    {
      throw new InvalidOperationException(
        "Mutagen service is not initialized. Please set the Skyrim data path first.");
    }
  }

  private (SkyrimMod patchMod, HashSet<ModKey> requiredMasters) LoadOrCreatePatch(
    string outputPath,
    string operationName)
  {
    var modKey = ModKey.FromFileName(Path.GetFileName(outputPath));
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
    var existingMasters = patchMod.ModHeader.MasterReferences?.Select(m => m.Master) ?? [];
    requiredMasters.UnionWith(existingMasters);

    return (patchMod, requiredMasters);
  }

  private void FinalizePatch(
    SkyrimMod patchMod,
    HashSet<ModKey> requiredMasters,
    string outputPath,
    IProgress<(int current, int total, string message)>? progress)
  {
    EnsureMasters(patchMod, requiredMasters);

    var actuallyRequiredMasters = CollectRequiredMasters(patchMod, []);
    CleanupMasterReferences(patchMod, actuallyRequiredMasters);

    TryApplyEslFlag(patchMod);

    progress?.Report((1, 1, "Writing patch file..."));
    mutagenService.ReleaseLinkCache();

    WritePatchWithRetry(patchMod, outputPath, actuallyRequiredMasters);
  }

  private async Task RefreshAfterWrite(
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
        var total = validMatches.Count;

        foreach (var match in validMatches)
        {
          current++;
          var sourceName = match.SourceArmor.Name?.String ?? match.SourceArmor.EditorID ?? "Unknown";
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
      await RefreshAfterWrite(outputPath, progress);
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
    var result = await Task.Run(() =>
    {
      try
      {
        RequireInitialized();

        var outfitList = outfits.ToList();
        if (outfitList.Count == 0)
        {
          return (false, "No outfits to create.", []);
        }

        _logger.Information(
          "Beginning outfit creation. Destination: {OutputPath}. OutfitCount={Count}",
          outputPath,
          outfitList.Count);

        var (patchMod, requiredMasters) = LoadOrCreatePatch(outputPath, "outfit creation");

        var results = new List<OutfitCreationResult>();
        var total = outfitList.Count;
        var current = 0;

        foreach (var (name, editorId, pieces, existingFormKey, isOverride, overrideSourceMod) in outfitList)
        {
          current++;
          progress?.Report((current, total, $"Writing outfit {name}..."));

          if (isOverride && existingFormKey.HasValue)
          {
            if (!mutagenService.LinkCache!.TryResolve<IOutfitGetter>(
                  existingFormKey.Value,
                  out var sourceOutfit))
            {
              _logger.Warning(
                "Override outfit {FormKey} not found in LinkCache, skipping.",
                existingFormKey.Value);
              continue;
            }

            var overrideOutfit = patchMod.Outfits.GetOrAddAsOverride(sourceOutfit);

            requiredMasters.Add(sourceOutfit.FormKey.ModKey);

            if (overrideSourceMod.HasValue)
            {
              requiredMasters.Add(overrideSourceMod.Value);
              _logger.Debug(
                "Adding override source mod {SourceMod} as master for outfit {FormKey}",
                overrideSourceMod.Value.FileName,
                existingFormKey.Value);
            }

            var overrideItems = overrideOutfit.Items ??= [];
            overrideItems.Clear();
            foreach (var armor in pieces)
            {
              overrideItems.Add(armor.ToLink());
              requiredMasters.Add(armor.FormKey.ModKey);
            }

            results.Add(new OutfitCreationResult(editorId, overrideOutfit.FormKey));
            _logger.Information(
              "Created override for outfit {EditorId} ({FormKey}) with {PieceCount} piece(s).",
              editorId,
              existingFormKey.Value,
              pieces.Count);
            continue;
          }

          Outfit? existing = null;
          if (existingFormKey.HasValue)
          {
            existing = patchMod.Outfits
              .FirstOrDefault(o => o.FormKey == existingFormKey.Value);
          }

          existing ??= patchMod.Outfits
            .FirstOrDefault(o =>
              string.Equals(o.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

          if (pieces.Count == 0)
          {
            if (existing != null)
            {
              patchMod.Outfits.Remove(existing);
              _logger.Information("Deleted outfit {EditorId}.", editorId);
              results.Add(new OutfitCreationResult(editorId, existing.FormKey));
            }
            else
            {
              _logger.Debug("Skipping deletion of {EditorId} — not in patch.", editorId);
            }

            continue;
          }

          Outfit outfit;
          if (existing != null)
          {
            outfit = existing;
            if (!string.Equals(outfit.EditorID, editorId, StringComparison.OrdinalIgnoreCase))
            {
              _logger.Information(
                "Renaming outfit {OldEditorId} to {NewEditorId}.",
                outfit.EditorID,
                editorId);
              outfit.EditorID = editorId;
            }

            _logger.Information(
              "Updating existing outfit {EditorId} with {PieceCount} piece(s).",
              editorId,
              pieces.Count);
          }
          else
          {
            outfit = patchMod.Outfits.AddNew();
            outfit.EditorID = editorId;
            _logger.Information(
              "Creating new outfit {EditorId} with {PieceCount} piece(s).",
              editorId,
              pieces.Count);
          }

          var items = outfit.Items ??= [];
          items.Clear();
          foreach (var armor in pieces)
          {
            items.Add(armor.ToLink());
            requiredMasters.Add(armor.FormKey.ModKey);
          }

          results.Add(new OutfitCreationResult(editorId, outfit.FormKey));
        }

        FinalizePatch(patchMod, requiredMasters, outputPath, progress);

        _logger.Information("Outfit creation completed successfully. File: {OutputPath}", outputPath);

        return (true, $"Saved {results.Count} outfit(s) to {outputPath}", results);
      }
      catch (InvalidOperationException ex)
      {
        return (false, ex.Message, []);
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error creating outfits destined for {OutputPath}", outputPath);
        return (false, $"Error creating outfits: {ex.Message}", (IReadOnlyList<OutfitCreationResult>)[]);
      }
    });

    if (result.Item1)
    {
      await RefreshAfterWrite(outputPath, progress);
    }
    else
    {
      await mutagenService.RefreshLinkCacheAsync();
    }

    return result;
  }

  public async Task<(bool success, string message, IReadOnlyList<LeveledListCreationResult> results)>
    CreateOrUpdateLeveledListsAsync(
      IEnumerable<LeveledListCreationRequest> leveledLists,
      string outputPath,
      IProgress<(int current, int total, string message)>? progress = null)
  {
    var result = await Task.Run(() =>
    {
      try
      {
        RequireInitialized();

        var listRequests = leveledLists.ToList();
        if (listRequests.Count == 0)
        {
          return (false, "No leveled lists to create.", []);
        }

        _logger.Information(
          "Beginning leveled list creation. Destination: {OutputPath}. Count={Count}",
          outputPath,
          listRequests.Count);

        var (patchMod, requiredMasters) = LoadOrCreatePatch(outputPath, "leveled list creation");

        var results = listRequests
          .Select((request, index) =>
          {
            progress?.Report((index + 1, listRequests.Count, $"Writing leveled list {request.Name}..."));
            return ProcessLeveledListRequest(request, patchMod, requiredMasters);
          })
          .Where(r => r != null)
          .Select(r => r!)
          .ToList();

        FinalizePatch(patchMod, requiredMasters, outputPath, progress);

        _logger.Information("Leveled list creation completed successfully. File: {OutputPath}", outputPath);

        return (true, $"Saved {results.Count} leveled list(s) to {outputPath}", results);
      }
      catch (InvalidOperationException ex)
      {
        return (false, ex.Message, []);
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error creating leveled lists destined for {OutputPath}", outputPath);
        return (false, $"Error creating leveled lists: {ex.Message}", (IReadOnlyList<LeveledListCreationResult>)[]);
      }
    });

    if (result.Item1)
    {
      await RefreshAfterWrite(outputPath, progress);
    }
    else
    {
      await mutagenService.RefreshLinkCacheAsync();
    }

    return result;
  }

  private LeveledListCreationResult? ProcessLeveledListRequest(
    LeveledListCreationRequest request,
    SkyrimMod patchMod,
    HashSet<ModKey> requiredMasters)
  {
    var existing = FindExistingLeveledItem(patchMod, request.ExistingFormKey, request.EditorId);

    if (request.Entries.Count == 0)
    {
      return HandleEmptyLeveledList(patchMod, existing, request.EditorId);
    }

    var leveledItem = GetOrCreateLeveledItem(patchMod, existing, request);
    ApplyLeveledItemFlags(leveledItem, request);
    PopulateLeveledItemEntries(leveledItem, request.Entries, requiredMasters);

    return new LeveledListCreationResult(request.EditorId, leveledItem.FormKey);
  }

  private static LeveledItem? FindExistingLeveledItem(SkyrimMod patchMod, FormKey? existingFormKey, string editorId) =>
    existingFormKey.HasValue
      ? patchMod.LeveledItems.FirstOrDefault(l => l.FormKey == existingFormKey.Value)
        ?? patchMod.LeveledItems.FirstOrDefault(l =>
          string.Equals(l.EditorID, editorId, StringComparison.OrdinalIgnoreCase))
      : patchMod.LeveledItems.FirstOrDefault(l =>
        string.Equals(l.EditorID, editorId, StringComparison.OrdinalIgnoreCase));

  private LeveledListCreationResult? HandleEmptyLeveledList(SkyrimMod patchMod, LeveledItem? existing, string editorId)
  {
    if (existing == null)
    {
      return null;
    }

    patchMod.LeveledItems.Remove(existing);
    _logger.Information("Deleted leveled list {EditorId}.", editorId);
    return new LeveledListCreationResult(editorId, existing.FormKey);
  }

  private LeveledItem GetOrCreateLeveledItem(SkyrimMod patchMod,
    LeveledItem? existing,
    LeveledListCreationRequest request)
  {
    if (existing != null)
    {
      if (!string.Equals(existing.EditorID, request.EditorId, StringComparison.OrdinalIgnoreCase))
      {
        _logger.Information("Renaming leveled list {OldEditorId} to {NewEditorId}.",
          existing.EditorID,
          request.EditorId);
        existing.EditorID = request.EditorId;
      }

      _logger.Information("Updating existing leveled list {EditorId} with {EntryCount} entries.",
        request.EditorId,
        request.Entries.Count);
      return existing;
    }

    var leveledItem = patchMod.LeveledItems.AddNew();
    leveledItem.EditorID = request.EditorId;
    _logger.Information("Creating new leveled list {EditorId} with {EntryCount} entries.",
      request.EditorId,
      request.Entries.Count);
    return leveledItem;
  }

  private static void ApplyLeveledItemFlags(LeveledItem leveledItem, LeveledListCreationRequest request)
  {
    leveledItem.Flags = request.Flags;
    if (request.UseAll)
    {
      leveledItem.Flags |= LeveledItem.Flag.UseAll;
    }
  }

  private static void PopulateLeveledItemEntries(
    LeveledItem leveledItem,
    IReadOnlyList<LeveledListEntryRequest> entryRequests,
    HashSet<ModKey> requiredMasters)
  {
    var entries = leveledItem.Entries ??= [];
    entries.Clear();

    foreach (var entryRequest in entryRequests)
    {
      entries.Add(new LeveledItemEntry
      {
        Data = new LeveledItemEntryData
        {
          Reference = entryRequest.ItemFormKey.ToLink<IItemGetter>(),
          Level = entryRequest.Level,
          Count = entryRequest.Count
        }
      });
      requiredMasters.Add(entryRequest.ItemFormKey.ModKey);
    }
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
    target.Value = source.Value;
    target.Weight = source.Weight;
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
             SkyrimMod.CreateFromBinaryOverlay(tempPath,
               mutagenService.SkyrimRelease,
               mutagenService.Utf8ReadParameters))
      {
        _logger.Information(
          "Masters after write: {Masters}",
          string.Join(", ", writtenMod.ModHeader.MasterReferences.Select(m => m.Master.FileName)));
      }

      GC.Collect();
      GC.WaitForPendingFinalizers();

      const int maxRetries = 10;
      const int initialDelayMs = 100;
      Exception? lastException = null;

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
              "File replace attempt {Attempt}/{Max} failed ({Error}), retrying in {Delay}ms...",
              attempt,
              maxRetries,
              ex.GetType().Name,
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
        return new MissingMastersResult(false, [], [], []);
      }

      try
      {
        using var patchMod = SkyrimMod.CreateFromBinaryOverlay(patchPath,
          mutagenService.SkyrimRelease,
          mutagenService.Utf8ReadParameters);
        var dataFolder = mutagenService.DataFolderPath ?? string.Empty;
        var loadOrderModKeys = mutagenService.GetLoadOrderModKeys();
        var masterRefs = patchMod.ModHeader.MasterReferences.Select(m => m.Master).ToList();
        var missingMasters = new List<ModKey>();
        var patchModKey = patchMod.ModKey;

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
          var validOutfits = patchMod.Outfits.ToList();
          return new MissingMastersResult(false, [], [], validOutfits);
        }

        var missingMasterSet = missingMasters.ToHashSet();
        var affectedOutfitsByMaster = new Dictionary<ModKey, List<AffectedOutfitInfo>>();
        var validOutfitsList = new List<IOutfitGetter>();

        foreach (var outfit in patchMod.Outfits)
        {
          var orphanedFormKeys = new List<FormKey>();
          var affectingMasters = new HashSet<ModKey>();

          if (outfit.Items != null)
          {
            foreach (var itemLink in outfit.Items)
            {
              var formKeyNullable = itemLink?.FormKeyNullable;
              if (!formKeyNullable.HasValue || formKeyNullable.Value == FormKey.Null)
              {
                continue;
              }

              var itemModKey = formKeyNullable.Value.ModKey;
              if (missingMasterSet.Contains(itemModKey))
              {
                orphanedFormKeys.Add(formKeyNullable.Value);
                affectingMasters.Add(itemModKey);
              }
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
                list = [];
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
          .Select(m => new MissingMasterInfo(
            m,
            affectedOutfitsByMaster.TryGetValue(m, out var list) ? list : []))
          .ToList();

        var allAffectedOutfits = affectedOutfitsByMaster
          .SelectMany(kvp => kvp.Value)
          .DistinctBy(a => a.FormKey)
          .ToList();

        _logger.Information(
          "Missing masters check complete: {MissingCount} missing master(s), {AffectedCount} affected outfit(s).",
          missingMasters.Count,
          allAffectedOutfits.Count);

        return new MissingMastersResult(true, missingMasterInfos, allAffectedOutfits, validOutfitsList);
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error checking missing masters for patch {Path}.", patchPath);
        return new MissingMastersResult(false, [], [], []);
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

        var patchMod = SkyrimMod.CreateFromBinary(patchPath, mutagenService.SkyrimRelease);
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
    var patchModKey = patchMod.ModKey;

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

      if (record is IOutfitGetter outfitRecord && outfitRecord.Items != null)
      {
        foreach (var item in outfitRecord.Items)
        {
          var formKey = item?.FormKeyNullable;
          if (formKey.HasValue && formKey.Value != FormKey.Null)
          {
            AddMaster(formKey.Value.ModKey);
          }
        }
      }

      if (record is IArmorGetter armor)
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
