using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;
using System.Text;
using System.Windows;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Microsoft.Win32;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public sealed partial class DistributionEditTabViewModel
{
  [ReactiveCommand(CanExecute = nameof(_canSave))]
  private async Task SaveDistributionFileAsync()
  {
    if (string.IsNullOrWhiteSpace(DistributionFilePath))
    {
      StatusMessage = "Please select a file path.";
      return;
    }

    DetectConflicts();

    var finalFilePath = DistributionFilePath;

    if (IsCreatingNewFile && HasConflicts && !string.IsNullOrEmpty(SuggestedFileName))
    {
      var sb = new StringBuilder();
      sb.AppendLine("⚠ Distribution Conflicts Detected");
      sb.AppendLine();
      sb.AppendLine(ConflictSummary);
      sb.AppendLine();
      sb.AppendLine("To ensure your new distributions take priority (load last), the filename will be changed to:");
      sb.AppendLine();
      sb.Append(CultureInfo.InvariantCulture, $"    {SuggestedFileName}").AppendLine();
      sb.AppendLine();
      sb.AppendLine("This 'Z' prefix ensures alphabetical sorting places your file after the conflicting files.");
      sb.AppendLine();
      sb.AppendLine("Do you want to continue with this filename?");

      var result = MessageBox.Show(
        sb.ToString(),
        "Conflicts Detected - Filename Change Required",
        MessageBoxButton.YesNoCancel,
        MessageBoxImage.Warning);

      if (result == MessageBoxResult.Cancel)
      {
        StatusMessage = "Save cancelled.";
        return;
      }

      if (result == MessageBoxResult.Yes)
      {
        var directory = Path.GetDirectoryName(DistributionFilePath);
        var finalFileName = SuggestedFileName;
        if (!finalFileName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
        {
          finalFileName += ".ini";
        }

        finalFilePath = !string.IsNullOrEmpty(directory)
                          ? Path.Combine(directory, finalFileName)
                          : finalFileName;

        NewFileName = finalFileName;
      }
    }

    if (File.Exists(finalFilePath))
    {
      var result = MessageBox.Show(
        $"The file '{Path.GetFileName(finalFilePath)}' already exists.\n\nDo you want to overwrite it?",
        "Confirm Overwrite",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);

      if (result != MessageBoxResult.Yes)
      {
        StatusMessage = "Save cancelled.";
        return;
      }
    }

    try
    {
      IsLoading     = true;
      StatusMessage = "Saving distribution file...";

      var directory = Path.GetDirectoryName(finalFilePath);
      if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }

      await File.WriteAllTextAsync(finalFilePath, DistributionFileContent, Encoding.UTF8);

      StatusMessage = $"Successfully saved distribution file: {Path.GetFileName(finalFilePath)}";
      _logger.Information(
        "Saved distribution file: {FilePath} ({LineCount} lines)",
        finalFilePath,
        DistributionFileContent.Split('\n').Length);

      _justSavedFilePath                    = finalFilePath;
      _guiSettings.LastDistributionFilePath = finalFilePath;
      DistributionFilePath                  = finalFilePath;

      FileSaved?.Invoke(finalFilePath);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to save distribution file.");
      StatusMessage = $"Error saving file: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  public event Action<string>? FileSaved;

  private async Task LoadDistributionFileAsync()
  {
    _logger.Debug(
      "LoadDistributionFileAsync called. DistributionFilePath: {Path}, Exists: {Exists}",
      DistributionFilePath,
      File.Exists(DistributionFilePath));

    if (string.IsNullOrWhiteSpace(DistributionFilePath) || !File.Exists(DistributionFilePath))
    {
      StatusMessage = "File does not exist. Please select a valid file.";
      _logger.Warning("Cannot load file - path is empty or file does not exist: {Path}", DistributionFilePath);
      return;
    }

    try
    {
      IsLoading     = true;
      StatusMessage = "Loading distribution file...";
      _logger.Information("Loading distribution file: {FilePath}", DistributionFilePath);

      var (entries, detectedFormat, parseErrors) =
        await _fileWriterService.LoadDistributionFileWithErrorsAsync(DistributionFilePath);
      DistributionFormat = detectedFormat;
      ParseErrors        = parseErrors;
      RefreshActualParseErrors();

      await LoadAvailableOutfitsAsync();
      _isBulkLoading = true;
      try
      {
        DistributionEntries.Clear();
        ResetConflictState();
        var entryVms = await Task.Run(() =>
                                        entries.Select(entry => CreateEntryViewModel(entry)).ToList());
        foreach (var entryVm in entryVms)
        {
          DistributionEntries.Add(entryVm);
        }

        foreach (var entryVm in entryVms)
        {
          SubscribeToEntryChanges(entryVm);
        }
      }
      finally
      {
        _isBulkLoading = false;
      }

      this.RaisePropertyChanged(nameof(DistributionEntriesCount));
      UpdateFileContent();
      UpdateHasChanceBasedEntries();
      UpdateHasKeywordDistributions();
      UpdateHasExclusiveGroupDistributions();

      var statusMsg =
        $"Loaded {entries.Count} distribution entries from {Path.GetFileName(DistributionFilePath)}";
      if (parseErrors.Count > 0)
      {
        statusMsg += $" ({parseErrors.Count} line(s) could not be parsed)";
      }

      StatusMessage = statusMsg;
      _logger.Information(
        "Loaded distribution file: {FilePath} with {Count} entries, {ErrorCount} parse errors",
        DistributionFilePath,
        entries.Count,
        parseErrors.Count);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to load distribution file.");
      StatusMessage = $"Error loading file: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [ReactiveCommand(CanExecute = nameof(_notLoading))]
  private async Task ScanNpcsAsync()
  {
    try
    {
      IsLoading = true;
      if (!_mutagenService.IsInitialized)
      {
        var dataPath = _settings.SkyrimDataPath;
        if (string.IsNullOrWhiteSpace(dataPath))
        {
          StatusMessage = "Please set the Skyrim data path in Settings before scanning NPCs.";
          return;
        }

        if (!Directory.Exists(dataPath))
        {
          StatusMessage = $"Skyrim data path does not exist: {dataPath}";
          return;
        }

        StatusMessage = "Initializing Skyrim environment...";
        await _mutagenService.InitializeAsync(dataPath);
        this.RaisePropertyChanged(nameof(IsInitialized));
      }

      if (_cache is { IsLoaded: false, IsLoading: false })
      {
        StatusMessage = "Loading game data (NPCs, factions, keywords, races, classes)...";
        await _cache.LoadAsync();
      }
      else if (_cache.IsLoading)
      {
        StatusMessage = "Loading game data from plugins...";
        while (_cache.IsLoading)
        {
          await Task.Delay(100);
        }
      }

      StatusMessage =
        $"Loaded: {AvailableNpcs.Count:N0} NPCs, {AvailableFactions.Count:N0} factions, {AvailableRaces.Count:N0} races, {AvailableClasses.Count:N0} classes, {AvailableKeywords.Count:N0} keywords.";
      _logger.Information(
        "Game data loaded: {NpcCount} NPCs, {FactionCount} factions.",
        AvailableNpcs.Count,
        AvailableFactions.Count);
      await LoadAvailableOutfitsAsync();
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to scan NPCs.");
      StatusMessage = $"Error scanning NPCs: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [ReactiveCommand]
  private void SelectDistributionFilePath()
  {
    if (!IsCreatingNewFile)
    {
      return;
    }

    var dialog = new SaveFileDialog
                 {
                   Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*", DefaultExt = "ini", FileName = NewFileName
                 };

    var targetDirectory = !string.IsNullOrWhiteSpace(_settings.OutputPatchPath) &&
                          Directory.Exists(_settings.OutputPatchPath)
                            ? _settings.OutputPatchPath
                            : _settings.SkyrimDataPath;

    if (!string.IsNullOrWhiteSpace(targetDirectory) && Directory.Exists(targetDirectory))
    {
      var defaultDir = PathUtilities.GetSkyPatcherNpcPath(targetDirectory);
      dialog.InitialDirectory = Directory.Exists(defaultDir) ? defaultDir : targetDirectory;
    }

    if (dialog.ShowDialog() == true)
    {
      NewFileName          = Path.GetFileName(dialog.FileName);
      DistributionFilePath = dialog.FileName;
      _logger.Debug("Selected distribution file path: {FilePath}", DistributionFilePath);
    }
  }

  /// <summary>
  ///   Refreshes the file dropdown after a file is saved. Preserves current selection.
  ///   For initial setup, use InitializeFromCache() instead.
  /// </summary>
  private void RefreshAvailableDistributionFiles()
  {
    var previousSelected = SelectedDistributionFile;
    var justSaved        = _justSavedFilePath;
    _justSavedFilePath = null;

    _logger.Debug(
      "Refreshing distribution files dropdown. Previous selection: {Selection}, NewFileName: {NewFileName}, JustSaved: {JustSaved}",
      previousSelected?.DisplayName,
      NewFileName,
      justSaved);

    var files                    = _cache.AllDistributionFiles.ToList();
    var (items, dropdownItems)   = DistributionFileDropdownBuilder.Build(files);
    AvailableDistributionFiles   = new ObservableCollection<DistributionFileSelectionItem>(items);
    DropdownItems                = dropdownItems;
    this.RaisePropertyChanged(nameof(SelectedDropdownItem));

    var resolved = DistributionFileDropdownBuilder.ResolveSelection(
      items,
      justSaved,
      previousSelected,
      _guiSettings.LastDistributionFilePath);

    if (resolved != null && !resolved.IsNewFile && justSaved != null && resolved.File?.FullPath == justSaved)
    {
      _logger.Debug("Selecting just-saved file: {Path}", justSaved);
      IsCreatingNewFile        = false;
      NewFileName              = string.Empty;
      SelectedDistributionFile = resolved;
    }
    else
    {
      SelectedDistributionFile = resolved;
    }
  }

  private void UpdateDistributionFilePathForFormat()
  {
    var newPath = _filePathService.UpdatePathForFormat(
      DistributionFilePath,
      IsCreatingNewFile,
      NewFileName,
      DistributionFormat);

    if (newPath != null)
    {
      DistributionFilePath = newPath;
    }
  }

  private void UpdateFileContent()
  {
    try
    {
      var hasSpidOnlyEntries = DistributionEntries.Any(e =>
                                                         e.UseChance ||
                                                         e.Type == DistributionType.Keyword ||
                                                         e.Type == DistributionType.ExclusiveGroup);
      var effectiveFormat = hasSpidOnlyEntries ? DistributionFileType.Spid : DistributionFormat;

      _logger.Debug(
        "UpdateFileContent: {EntryCount} entries, effectiveFormat={Format}, hasSpidOnlyEntries={SpidOnly}",
        DistributionEntries.Count,
        effectiveFormat,
        hasSpidOnlyEntries);

      DistributionFileContent =
        DistributionFileFormatter.GenerateFileContent(DistributionEntries, effectiveFormat, ParseErrors);

      _logger.Debug("UpdateFileContent: Generated {LineCount} lines", DistributionFileContent.Split('\n').Length);
      DetectConflicts();

      RaiseHighlightRequestForChangedEntry();
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Error updating distribution file content");
      DistributionFileContent = $"; Error generating file content: {ex.Message}";
    }
  }

  private void RaiseHighlightRequestForChangedEntry()
  {
    var entryToHighlight = _lastChangedEntry ?? SelectedEntry;
    _lastChangedEntry = null;

    if (entryToHighlight == null || _isBulkLoading)
    {
      return;
    }

    var lineNumber = CalculateLineNumberForEntry(entryToHighlight);
    if (lineNumber < 0)
    {
      return;
    }

    var lines = DistributionFileContent.Split('\n');

    _ = lineNumber < lines.Length ? lines[lineNumber].TrimEnd('\r') : string.Empty;

    _logger.Information("RaiseHighlightRequest: line {LineNumber}", lineNumber);
    HighlightRequest = new PreviewLineHighlightRequest(lineNumber);
  }

  private int CalculateLineNumberForEntry(DistributionEntryViewModel targetEntry)
  {
    var lineNumber = DistributionFileFormatter.GenerateHeaderLines().Count;

    foreach (var entry in DistributionEntries)
    {
      var producesLine = EntryProducesLine(entry);

      if (entry == targetEntry)
      {
        return producesLine ? lineNumber : -1;
      }

      if (producesLine)
      {
        lineNumber++;
      }
    }

    return -1;
  }

  public void SelectEntryByLineNumber(int lineNumber)
  {
    var headerLineCount = DistributionFileFormatter.GenerateHeaderLines().Count;
    if (lineNumber < headerLineCount)
    {
      return;
    }

    var currentLine = headerLineCount;
    foreach (var entry in DistributionEntries)
    {
      var producesLine = EntryProducesLine(entry);

      if (!producesLine)
      {
        continue;
      }

      if (currentLine == lineNumber)
      {
        SelectedEntry = entry;
        return;
      }

      currentLine++;
    }
  }

  private static bool EntryProducesLine(DistributionEntryViewModel entry) =>
    entry.Type switch
    {
      DistributionType.Keyword => !string.IsNullOrWhiteSpace(entry.KeywordToDistribute),
      DistributionType.ExclusiveGroup => !string.IsNullOrWhiteSpace(entry.ExclusiveGroupName) &&
                                         !string.IsNullOrWhiteSpace(entry.ExclusiveGroupFormsText),
      _ => entry.SelectedOutfit != null
    };

  private void RaiseHighlightForEntry(DistributionEntryViewModel entry)
  {
    var lineNumber = CalculateLineNumberForEntry(entry);
    if (lineNumber < 0)
    {
      return;
    }

    HighlightRequest = new PreviewLineHighlightRequest(lineNumber);
  }

  private async Task LoadAvailableOutfitsAsync()
  {
    if (_outfitsLoaded && AvailableOutfits.Count > 0)
    {
      return;
    }

    if (_mutagenService.LinkCache is not { } linkCache)
    {
      if (AvailableOutfits.Count > 0)
      {
        AvailableOutfits.Clear();
      }

      _outfitsLoaded = false;
      return;
    }

    try
    {
      var outfits = await Task.Run(() =>
                                     linkCache.WinningOverrides<IOutfitGetter>().ToList());
      await MergeOutfitsFromPatchFileAsync(outfits);
      AvailableOutfits = new ObservableCollection<IOutfitGetter>(outfits);
      _outfitsLoaded   = true;
      _logger.Debug("Loaded {Count} available outfits.", outfits.Count);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to load available outfits.");
      AvailableOutfits.Clear();
    }
  }

  /// <summary>
  ///   Merges outfits from the patch file into the provided list if the patch exists
  ///   but isn't in the active load order (not enabled in plugins.txt).
  /// </summary>
  private async Task MergeOutfitsFromPatchFileAsync(List<IOutfitGetter> outfits)
  {
    var patchPath = _settings.FullOutputPath;
    if (string.IsNullOrEmpty(patchPath) || !File.Exists(patchPath))
    {
      return;
    }

    var patchOutfits     = await _mutagenService.LoadOutfitsFromPluginAsync(Path.GetFileName(patchPath));
    var existingFormKeys = outfits.Select(o => o.FormKey).ToHashSet();

    var newOutfits = patchOutfits.Where(o => !existingFormKeys.Contains(o.FormKey)).ToList();
    if (newOutfits.Count > 0)
    {
      outfits.AddRange(newOutfits);
      _logger.Information(
        "Added {Count} outfit(s) from patch file {Patch} (not in active load order).",
        newOutfits.Count,
        Path.GetFileName(patchPath));
    }
  }

  /// <summary>Triggers lazy loading of outfits when ComboBox opens.</summary>
  public void EnsureOutfitsLoaded()
  {
    if (!_outfitsLoaded)
    {
      _ = LoadAvailableOutfitsAsync();
    }
  }

  private async void OnPluginsChanged(object? sender, EventArgs e)
  {
    _logger.Debug("PluginsChanged event received in DistributionEditTabViewModel, invalidating outfits cache...");
    _outfitsLoaded = false;
    _logger.Information("Reloading available outfits...");
    await LoadAvailableOutfitsAsync();
  }

  private void OnCacheLoaded(object? sender, EventArgs e)
  {
    if (!_isInitialized)
    {
      _logger.Debug("CacheLoaded: First load, performing full initialization...");
      InitializeFromCache();
    }
    else
    {
      _logger.Debug("CacheLoaded: Already initialized, refreshing file list...");
      RefreshAvailableDistributionFiles();
    }
  }

  /// <summary>
  ///   Single initialization point after cache is fully loaded (first time only).
  ///   Linear flow: 1) populate filters, 2) populate file dropdown, 3) select &lt;New File&gt; with unique name.
  /// </summary>
  private void InitializeFromCache()
  {
    _logger.Debug("InitializeFromCache: Starting linear initialization...");
    var files                  = _cache.AllDistributionFiles.ToList();
    var (items, dropdownItems) = DistributionFileDropdownBuilder.Build(files);
    AvailableDistributionFiles = new ObservableCollection<DistributionFileSelectionItem>(items);
    DropdownItems              = dropdownItems;

    var lastFilePath = _guiSettings.LastDistributionFilePath;
    var resolved = DistributionFileDropdownBuilder.ResolveSelection(items, null, null, lastFilePath);

    if (resolved != null && !resolved.IsNewFile && resolved.File != null)
    {
      _logger.Debug("Restoring last saved file from settings: {Path}", resolved.File.FullPath);
      DistributionFilePath     = resolved.File.FullPath;
      SelectedDistributionFile = resolved;
      this.RaisePropertyChanged(nameof(SelectedDropdownItem));
      _isInitialized = true;
      this.RaisePropertyChanged(nameof(IsInitialized));
      _logger.Information(
        "Initialization complete: {NpcCount} NPCs, {FactionCount} factions, {KeywordCount} keywords, {RaceCount} races, {FileCount} files. Restored file: {FilePath}",
        FilteredNpcs.Count,
        FilteredFactions.Count,
        FilteredKeywords.Count,
        FilteredRaces.Count,
        files.Count,
        resolved.File.FullPath);
      _ = LoadDistributionFileAsync();
      return;
    }

    var newFileItem = AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
    if (newFileItem != null)
    {
      NewFileName = GenerateUniqueNewFileName();
      _logger.Debug("Generated unique filename: {FileName}", NewFileName);
      SelectedDistributionFile = newFileItem;
    }

    this.RaisePropertyChanged(nameof(SelectedDropdownItem));
    _isInitialized = true;
    this.RaisePropertyChanged(nameof(IsInitialized));
    _logger.Information(
      "Initialization complete: {NpcCount} NPCs, {FactionCount} factions, {KeywordCount} keywords, {RaceCount} races, {FileCount} files. NewFileName={NewFileName}",
      FilteredNpcs.Count,
      FilteredFactions.Count,
      FilteredKeywords.Count,
      FilteredRaces.Count,
      files.Count,
      NewFileName);
  }

  private string GenerateUniqueNewFileName()
  {
    const string baseName = "Boutique_Distribution";
    var existingFileNames = AvailableDistributionFiles
                            .Where(f => !f.IsNewFile && f.File != null)
                            .Select(f => f.File!.FileName)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (IsBaseNameAvailable(baseName, existingFileNames))
    {
      return $"{baseName}.ini";
    }

    for (var i = 2; i < 1000; i++)
    {
      var numberedBase = $"{baseName}_{i}";
      if (IsBaseNameAvailable(numberedBase, existingFileNames))
      {
        return $"{numberedBase}.ini";
      }
    }

    return $"{baseName}_{Guid.NewGuid():N}.ini";
  }

  private static bool IsBaseNameAvailable(string baseName, HashSet<string> existingFileNames) =>
    !existingFileNames.Contains($"{baseName}.ini") &&
    !existingFileNames.Contains($"{baseName}_DISTR.ini");

  [ReactiveCommand(CanExecute = nameof(_notLoading))]
  private async Task PreviewEntryAsync(DistributionEntryViewModel? entry)
  {
    if (entry == null || entry.SelectedOutfit == null)
    {
      StatusMessage = "No outfit selected for preview.";
      return;
    }

    if (!_mutagenService.IsInitialized ||
        _mutagenService.LinkCache is not { } linkCache)
    {
      StatusMessage = "Initialize Skyrim data path before previewing outfits.";
      return;
    }

    var outfit = entry.SelectedOutfit;
    var label  = outfit.EditorID ?? outfit.FormKey.ToString();

    var initialResult = OutfitResolver.GatherArmorPieces(outfit, linkCache, Environment.TickCount);
    if (initialResult.ArmorPieces.Count == 0)
    {
      StatusMessage = $"Outfit '{label}' has no armor pieces to preview.";
      return;
    }

    try
    {
      StatusMessage = $"Building preview for {label}...";

      var initialGender = entry.Gender switch
      {
        GenderFilter.Male   => GenderedModelVariant.Male,
        GenderFilter.Female => GenderedModelVariant.Female,
        _                   => GenderedModelVariant.Female
      };

      var metadata = new OutfitMetadata(
        label,
        outfit.FormKey.ModKey.FileName.String,
        false,
        initialResult.ContainsLeveledItems);
      var collection = new ArmorPreviewSceneCollection(
        1,
        0,
        [metadata],
        async (_, gender) =>
        {
          var result = OutfitResolver.GatherArmorPieces(outfit, linkCache, Environment.TickCount);
          var scene  = await _armorPreviewService.BuildPreviewAsync(result.ArmorPieces, gender);
          return scene with { };
        },
        initialGender);

      await ShowPreview.Handle(collection);
      StatusMessage = $"Preview ready for {label}.";
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to preview outfit {Identifier}", label);
      StatusMessage = $"Failed to preview outfit: {ex.Message}";
    }
  }

  /// <summary>
  ///   Detects conflicts between the current distribution entries and existing distribution files.
  ///   Updates HasConflicts, ConflictSummary, and NPC conflict indicators.
  /// </summary>
  private void DetectConflicts()
  {
    if (!IsCreatingNewFile || DistributionEntries.Count == 0 || _mutagenService.LinkCache is not { } linkCache || DistributionFiles.Count == 0)
    {
      ResetConflictState(NewFileName);
      return;
    }

    var entryCountAtStart = DistributionEntries.Count;
    var entriesSnapshot   = DistributionEntries.ToList();
    Task.Run(() =>
    {
      try
      {
        var result = DistributionConflictDetectionService.DetectConflicts(
          entriesSnapshot,
          [.. DistributionFiles],
          NewFileName,
          linkCache);
        Application.Current?.Dispatcher.Invoke(() =>
        {
          if (DistributionEntries.Count == entryCountAtStart && DistributionEntries.Count > 0)
          {
            HasConflicts                = result.HasConflicts;
            ConflictsResolvedByFilename = result.ConflictsResolvedByFilename;
            ConflictSummary             = result.ConflictSummary;
            HasOverlaps                 = result.HasOverlaps;
            OverlapsResolvedByFilename  = result.OverlapsResolvedByFilename;
            OverlapSummary              = result.OverlapSummary;
            SuggestedFileName           = result.SuggestedFileName;
            DistributionConflictDetectionService.ApplyConflictIndicators(result, DistributionEntries);
          }
          else
          {
            _logger.Debug("Conflict detection completed but entries were cleared/changed, clearing conflict state");
            ResetConflictState(NewFileName);
          }
        });
      }
      catch (Exception ex)
      {
        _logger.Error(ex, "Error during conflict detection");
      }
    });
  }

  private void ResetConflictState(string suggestedFileName = "")
  {
    HasConflicts                = false;
    ConflictsResolvedByFilename = false;
    ConflictSummary             = string.Empty;
    HasOverlaps                 = false;
    OverlapsResolvedByFilename  = false;
    OverlapSummary              = string.Empty;
    SuggestedFileName           = suggestedFileName;
    ClearNpcConflictIndicators();
  }

  private void ClearNpcConflictIndicators() =>
    DistributionConflictDetectionService.ClearConflictIndicators(DistributionEntries);

  private void UpdateMatchingNpcsForEntry(DistributionEntryViewModel? entry)
  {
    if (entry == null || _cache.AllNpcs.Count == 0)
    {
      _matchingNpcsForSelectedEntry = [];
      this.RaisePropertyChanged(nameof(MatchingNpcsForSelectedEntry));
      this.RaisePropertyChanged(nameof(MatchingNpcsCount));
      return;
    }

    try
    {
      var matchingNpcs = FilterMatchingService.GetMatchingNpcsForEntry(
                            _cache.AllNpcs,
                            entry.Entry,
                            _cache.SimulatedKeywordsByNpc);
      _matchingNpcsForSelectedEntry = matchingNpcs;
      this.RaisePropertyChanged(nameof(MatchingNpcsForSelectedEntry));
      this.RaisePropertyChanged(nameof(MatchingNpcsCount));
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Failed to compute matching NPCs for entry");
      _matchingNpcsForSelectedEntry = [];
      this.RaisePropertyChanged(nameof(MatchingNpcsForSelectedEntry));
      this.RaisePropertyChanged(nameof(MatchingNpcsCount));
    }
  }

  /// <summary>
  ///   Called when a user enables chance-based distribution.
  ///   Returns true if format is currently SkyPatcher (will be changed to SPID), false if already SPID.
  /// </summary>
  private bool IsFormatChangingToSpid()
  {
    if (DistributionFormat != DistributionFileType.SkyPatcher)
    {
      return false;
    }

    DistributionFormat = DistributionFileType.Spid;
    UpdateFileContent();
    return true;
  }

  private DistributionEntryViewModel CreateEntryViewModel(DistributionEntry entry)
  {
    var entryVm = new DistributionEntryViewModel(entry, RemoveDistributionEntry, IsFormatChangingToSpid);
    _hydrationService.HydrateEntry(entryVm, entry, AvailableOutfits);
    return entryVm;
  }
}
