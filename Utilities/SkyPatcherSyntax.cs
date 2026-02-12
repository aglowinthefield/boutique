using System.Text.RegularExpressions;
using Mutagen.Bethesda.Plugins;

namespace Boutique.Utilities;

public static class SkyPatcherSyntax
{
  private static readonly Regex _filterNamePattern = new(
    @"(filterBy\w+|outfitDefault|outfitSleep|formsToAdd|formsToRemove|formsToReplace|clear)=",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);

  private static readonly HashSet<string> _supportedFilters = new(StringComparer.OrdinalIgnoreCase)
                                                              {
                                                                "filterByNpcs",
                                                                "filterByNpcsExcluded",
                                                                "filterByFactions",
                                                                "filterByFactionsOr",
                                                                "filterByFactionsExcluded",
                                                                "filterByKeywords",
                                                                "filterByKeywordsOr",
                                                                "filterByKeywordsExcluded",
                                                                "filterByRaces",
                                                                "filterByGender",
                                                                "filterByEditorIdContains",
                                                                "filterByEditorIdContainsOr",
                                                                "filterByEditorIdContainsExcluded",
                                                                "filterByDefaultOutfits",
                                                                "filterByModNames",
                                                                "filterByOutfits",
                                                                "filterByForms",
                                                                "filterByFormsOr",
                                                                "filterByFormsExclude",
                                                                "outfitDefault",
                                                                "outfitSleep",
                                                                "formsToAdd",
                                                                "formsToRemove",
                                                                "formsToReplace",
                                                                "clear"
                                                              };

  public static string? ExtractFilterValue(string line, string filterName)
  {
    if (string.IsNullOrWhiteSpace(line))
    {
      return null;
    }

    var filterPrefix = filterName + "=";
    var index        = line.IndexOf(filterPrefix, StringComparison.OrdinalIgnoreCase);
    if (index < 0)
    {
      return null;
    }

    var start = index + filterPrefix.Length;
    var end   = line.IndexOf(':', start);

    var value = end >= 0 ? line.Substring(start, end - start) : line[start..];
    return value.Trim();
  }

  public static List<string> ExtractFilterValues(string line, string filterName)
  {
    var value = ExtractFilterValue(line, filterName);
    if (string.IsNullOrEmpty(value))
    {
      return [];
    }

    return value.Split(',')
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
  }

  public static List<string> ExtractFilterValuesWithVariants(string line, string baseFilterName)
  {
    var results = new List<string>();
    results.AddRange(ExtractFilterValues(line, baseFilterName));
    results.AddRange(ExtractFilterValues(line, baseFilterName + "Or"));
    return results;
  }

  public static bool? ParseGenderFilter(string line)
  {
    var value = ExtractFilterValue(line, "filterByGender");
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    if (value.Equals("female", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (value.Equals("male", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    return null;
  }

  public static List<FormKey> ParseFormKeys(string line, string filterName)
  {
    var values  = ExtractFilterValues(line, filterName);
    var results = new List<FormKey>();

    foreach (var value in values)
    {
      if (FormKeyHelper.TryParse(value, out var formKey))
      {
        results.Add(formKey);
      }
    }

    return results;
  }

  public static bool HasFilter(string line, string filterName) =>
    line.Contains(filterName + "=", StringComparison.OrdinalIgnoreCase);

  public static bool HasAnyVariant(string line, string baseFilterName) =>
    HasFilter(line, baseFilterName) ||
    HasFilter(line, baseFilterName + "Or") ||
    HasFilter(line, baseFilterName + "Excluded");

  public static List<string> GetAllFilterNames(string line)
  {
    var matches = _filterNamePattern.Matches(line);
    return matches
           .Select(m => m.Groups[1].Value)
           .Distinct(StringComparer.OrdinalIgnoreCase)
           .ToList();
  }

  public static List<string> GetUnsupportedFilters(string line)
  {
    var allFilters = GetAllFilterNames(line);
    return allFilters
           .Where(f => !_supportedFilters.Contains(f))
           .ToList();
  }

  public static bool HasUnsupportedFilters(string line) =>
    GetUnsupportedFilters(line).Count > 0;
}
