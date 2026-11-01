using System.IO;
using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

/// <summary>
/// Loads and parses distribution files (SPID and SkyPatcher formats) with format detection
/// and error tracking, converting raw INI content into structured distribution entries.
/// </summary>
public class DistributionFileEditorService(MutagenService mutagenService, ILogger logger)
{
  private readonly ILogger _logger = logger.ForContext<DistributionFileEditorService>();

  public async Task<(IReadOnlyList<DistributionEntry> Entries, DistributionFileType DetectedFormat)>
    LoadDistributionFileWithFormatAsync(
      string filePath,
      CancellationToken cancellationToken = default)
  {
    var (entries, detectedFormat, _) = await LoadDistributionFileWithErrorsAsync(filePath, cancellationToken);
    return (entries, detectedFormat);
  }

  public async
    Task<(IReadOnlyList<DistributionEntry> Entries, DistributionFileType DetectedFormat,
      IReadOnlyList<DistributionParseError> ParseErrors)> LoadDistributionFileWithErrorsAsync(
      string filePath,
      CancellationToken cancellationToken = default)
  {
    return await Task.Run(
             () =>
             {
               var entries = new List<DistributionEntry>();
               var parseErrors = new List<DistributionParseError>();
               var hasSpidLines = false;
               var detectedFormat = DistributionFileType.SkyPatcher;

               if (!File.Exists(filePath))
               {
                 _logger.Warning("Distribution file does not exist: {FilePath}", filePath);
                 return (entries, detectedFormat, parseErrors);
               }

               if (mutagenService.LinkCache is not { } linkCache)
               {
                 _logger.Warning("LinkCache not available. Cannot load distribution file.");
                 return (entries, detectedFormat, parseErrors);
               }

               try
               {
                 var lines = File.ReadAllLines(filePath, Encoding.UTF8);

                 List<INpcGetter>? cachedNpcs = null;
                 List<IOutfitGetter>? cachedOutfits = null;
                 FormIdLookupCache? formIdCache = null;

                 var outfitByEditorId = FormKeyHelper.BuildOutfitEditorIdLookup(linkCache);
                 var virtualKeywords = ExtractVirtualKeywordsFromLines(lines);

                 for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
                 {
                   cancellationToken.ThrowIfCancellationRequested();

                   var line = lines[lineNumber];
                   var trimmed = line.Trim();
                   if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                   {
                     continue;
                   }

                   DistributionEntry? entry = null;
                   string? parseFailureReason = null;

                   if (trimmed.Contains("outfitDefault=", StringComparison.OrdinalIgnoreCase))
                   {
                     var (parsedEntry, reason) = new SkyPatcherEntryParser(_logger).Parse(trimmed, linkCache, outfitByEditorId);

                     // The editor has no class-exclusion control; do not lose that constraint on save.
                     entry = parsedEntry?.ExcludedClassFormKeys.Count > 0 ? null : parsedEntry;
                     parseFailureReason = parsedEntry?.ExcludedClassFormKeys.Count > 0
                       ? "SkyPatcher class exclusion (preserved)" : reason;
                   }
                   else if (SpidLineParser.TryParse(trimmed, out var spidFilter) && spidFilter != null)
                   {
                     hasSpidLines = true;

                     if (spidFilter.FormType == SpidFormType.Outfit)
                     {
                       cachedNpcs ??= linkCache.PriorityOrder.WinningOverrides<INpcGetter>().ToList();
                       cachedOutfits ??= linkCache.PriorityOrder.WinningOverrides<IOutfitGetter>().ToList();
                       formIdCache ??= new FormIdLookupCache(linkCache);

                       entry = SpidFilterResolver.Resolve(
                         spidFilter,
                         linkCache,
                         cachedNpcs,
                         cachedOutfits,
                         virtualKeywords,
                         formIdCache,
                         _logger);
                       if (entry == null)
                       {
                         parseFailureReason = "Could not resolve outfit or filters from SPID syntax";
                       }
                     }
                     else if (spidFilter.FormType == SpidFormType.Keyword)
                     {
                       cachedNpcs ??= linkCache.PriorityOrder.WinningOverrides<INpcGetter>().ToList();
                       formIdCache ??= new FormIdLookupCache(linkCache);

                       entry = SpidFilterResolver.ResolveKeyword(
                         spidFilter,
                         linkCache,
                         cachedNpcs,
                         virtualKeywords,
                         formIdCache,
                         _logger);
                       if (entry == null)
                       {
                         parseFailureReason =
                           "Could not resolve keyword distribution filters from SPID syntax";
                       }
                     }
                     else if (spidFilter.FormType == SpidFormType.ExclusiveGroup)
                     {
                       entry = new DistributionEntry
                       {
                         Type = DistributionType.ExclusiveGroup,
                         ExclusiveGroupName = spidFilter.FormIdentifier,
                         ExclusiveGroupForms = spidFilter.ExclusiveGroupForms.ToList()
                       };
                     }
                     else
                     {
                       parseErrors.Add(
                         new DistributionParseError(
                           lineNumber + 1,
                           trimmed,
                           $"{spidFilter.FormType} distribution (preserved)"));
                     }
                   }
                   else
                   {
                     parseFailureReason = "Unrecognized distribution syntax";
                   }

                   if (entry != null)
                   {
                     entries.Add(entry);
                   }
                   else if (parseFailureReason != null)
                   {
                     parseErrors.Add(new DistributionParseError(lineNumber + 1, trimmed, parseFailureReason));
                   }
                 }

                 detectedFormat = hasSpidLines ? DistributionFileType.Spid : DistributionFileType.SkyPatcher;

                 _logger.Information(
                   "Loaded {Count} distribution entries from {FilePath} (detected format: {Format}, {ErrorCount} parse errors)",
                   entries.Count,
                   filePath,
                   detectedFormat,
                   parseErrors.Count);
               }
               catch (OperationCanceledException ex)
               {
                 _logger.Information(ex, "Distribution file load cancelled.");
               }
               catch (Exception ex)
               {
                 _logger.Error(ex, "Failed to load distribution file: {FilePath}", filePath);
               }

               return (entries, detectedFormat, parseErrors);
             },
             cancellationToken);
  }

  private static HashSet<string> ExtractVirtualKeywordsFromLines(string[] lines)
  {
    var virtualKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var line in lines)
    {
      var trimmed = line.Trim();
      if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
      {
        continue;
      }

      if (SpidLineParser.TryParse(trimmed, out var spidFilter) &&
          spidFilter?.FormType == SpidFormType.Keyword &&
          !string.IsNullOrWhiteSpace(spidFilter.FormIdentifier))
      {
        virtualKeywords.Add(spidFilter.FormIdentifier);
      }
    }

    return virtualKeywords;
  }
}
