using Boutique.Models;
using Boutique.ViewModels;

namespace Boutique.Utilities;

/// <summary>
///   Organizes distribution files into a grouped dropdown structure.
/// </summary>
public static class DistributionDropdownOrganizer
{
  public const string NewFileActionId = "new-file";

  /// <summary>
  ///   Organizes files into a dropdown structure with headers and items.
  ///   Files with duplicate names get their unique path shown.
  /// </summary>
  /// <returns>Tree-like structure for rendering.</returns>
  public static GroupedDropdownStructure<DistributionFileInfo> Organize(IEnumerable<DistributionFileViewModel> files)
  {
    var newFileAction = new GroupedDropdownAction("<New File>", NewFileActionId);
    var fileList = files.ToList();
    if (fileList.Count == 0)
    {
      return new GroupedDropdownStructure<DistributionFileInfo>(
        [newFileAction],
        []);
    }

    var duplicateFileNames = GetDuplicateFileNames(fileList);

    var grouped = fileList
      .Select(f => new
      {
        File = f,
        GroupName = f.ModName,
        UniquePath = duplicateFileNames.Contains(f.FileName) ? f.UniquePath : f.FileName
      })
      .GroupBy(x => x.GroupName, StringComparer.OrdinalIgnoreCase)
      .OrderBy(g => string.IsNullOrEmpty(g.Key) ? 0 : 1)
      .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var items = new List<GroupedDropdownItem> { newFileAction };
    var groupNames = new List<string>();

    foreach (var group in grouped)
    {
      var groupName = group.Key;

      if (!string.IsNullOrEmpty(groupName))
      {
        items.Add(new GroupedDropdownHeader(groupName));
        groupNames.Add(groupName);
      }

      var sortedFiles = group
        .OrderBy(x => x.UniquePath, StringComparer.OrdinalIgnoreCase)
        .ToList();

      foreach (var fileInfo in sortedFiles)
      {
        var displayPath = StripGroupPrefix(fileInfo.UniquePath, groupName);
        var payload = new DistributionFileInfo(
          fileInfo.File.FileName,
          displayPath,
          fileInfo.File.FullPath);

        var effectiveGroupName = string.IsNullOrEmpty(groupName) ? null : groupName;
        items.Add(new GroupedDropdownItem<DistributionFileInfo>(displayPath, payload, effectiveGroupName));
      }
    }

    return new GroupedDropdownStructure<DistributionFileInfo>(items, groupNames);
  }

  private static HashSet<string> GetDuplicateFileNames(IEnumerable<DistributionFileViewModel> files) =>
    files
      .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
      .Where(g => g.Count() > 1)
      .Select(g => g.Key)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

  private static string StripGroupPrefix(string path, string groupName)
  {
    if (string.IsNullOrEmpty(groupName))
    {
      return path;
    }

    // Check if path starts with groupName followed by / or \
    var prefixWithSlash = groupName + "/";
    var prefixWithBackslash = groupName + "\\";

    if (path.StartsWith(prefixWithSlash, StringComparison.OrdinalIgnoreCase))
    {
      return path[prefixWithSlash.Length..];
    }

    if (path.StartsWith(prefixWithBackslash, StringComparison.OrdinalIgnoreCase))
    {
      return path[prefixWithBackslash.Length..];
    }

    return path;
  }
}
