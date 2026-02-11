using Boutique.Models;
using Boutique.ViewModels;

namespace Boutique.Utilities;

public static class DistributionFileDropdownBuilder
{
  public static (List<DistributionFileSelectionItem> Items, IReadOnlyList<GroupedDropdownItem> DropdownItems) Build(
    IReadOnlyList<DistributionFileViewModel> files)
  {
    var duplicateFileNames = GetDuplicateFileNames(files);

    var items = new List<DistributionFileSelectionItem> { new(true, null) };
    foreach (var file in files)
    {
      var hasDuplicate = duplicateFileNames.Contains(file.FileName);
      items.Add(new DistributionFileSelectionItem(false, file, hasDuplicate));
    }

    var dropdownStructure = DistributionDropdownOrganizer.Organize(files);
    return (items, dropdownStructure.Items);
  }

  public static DistributionFileSelectionItem? ResolveSelection(
    IReadOnlyList<DistributionFileSelectionItem> items,
    string? justSavedPath,
    DistributionFileSelectionItem? previous,
    string? lastSettingsPath)
  {
    if (!string.IsNullOrEmpty(justSavedPath))
    {
      var savedItem = FindByFullPath(items, justSavedPath);
      if (savedItem != null)
      {
        return savedItem;
      }
    }

    if (previous != null)
    {
      if (previous.IsNewFile)
      {
        return items.FirstOrDefault(f => f.IsNewFile);
      }

      if (previous.File != null)
      {
        var matchingItem = FindByFullPath(items, previous.File.FullPath);
        if (matchingItem != null)
        {
          return matchingItem;
        }
      }
    }

    if (!string.IsNullOrEmpty(lastSettingsPath))
    {
      var lastItem = FindByFullPath(items, lastSettingsPath);
      if (lastItem != null)
      {
        return lastItem;
      }
    }

    return items.FirstOrDefault(f => f.IsNewFile);
  }

  public static HashSet<string> GetDuplicateFileNames(IEnumerable<DistributionFileViewModel> files) =>
    files
      .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
      .Where(g => g.Count() > 1)
      .Select(g => g.Key)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

  private static DistributionFileSelectionItem? FindByFullPath(
    IReadOnlyList<DistributionFileSelectionItem> items,
    string fullPath) =>
    items.FirstOrDefault(item =>
                           !item.IsNewFile && item.File != null &&
                           string.Equals(
                             item.File.FullPath,
                             fullPath,
                             StringComparison.OrdinalIgnoreCase));
}
