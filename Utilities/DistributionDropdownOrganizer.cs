using Boutique.Models;
using Boutique.ViewModels;

namespace Boutique.Utilities;

/// <summary>
/// Organizes distribution files into a grouped dropdown structure.
/// </summary>
public static class DistributionDropdownOrganizer
{
    /// <summary>
    /// Organizes files into a dropdown structure with headers and items.
    /// Files with duplicate names get their unique path shown.
    /// </summary>
    public static DistributionDropdownStructure Organize(IEnumerable<DistributionFileViewModel> files)
    {
        var fileList = files.ToList();
        if (fileList.Count == 0)
            return new DistributionDropdownStructure(
                [DistributionNewFileItem.Instance],
                []);

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

        var items = new List<DistributionDropdownItem> { DistributionNewFileItem.Instance };
        var groupNames = new List<string>();

        foreach (var group in grouped)
        {
            var groupName = group.Key;

            if (!string.IsNullOrEmpty(groupName))
            {
                items.Add(new DistributionGroupHeader(groupName));
                groupNames.Add(groupName);
            }

            var sortedFiles = group
                .OrderBy(x => x.UniquePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var fileInfo in sortedFiles)
            {
                // Strip the group name prefix from the display path since the header already shows it
                var displayPath = StripGroupPrefix(fileInfo.UniquePath, groupName);

                items.Add(new DistributionFileItem(
                    fileInfo.File.FileName,
                    displayPath,
                    fileInfo.File.FullPath,
                    groupName));
            }
        }

        return new DistributionDropdownStructure(items, groupNames);
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
            return path;

        // Check if path starts with groupName followed by / or \
        var prefixWithSlash = groupName + "/";
        var prefixWithBackslash = groupName + "\\";

        if (path.StartsWith(prefixWithSlash, StringComparison.OrdinalIgnoreCase))
            return path[prefixWithSlash.Length..];

        if (path.StartsWith(prefixWithBackslash, StringComparison.OrdinalIgnoreCase))
            return path[prefixWithBackslash.Length..];

        return path;
    }
}
