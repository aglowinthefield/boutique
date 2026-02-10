using System.IO;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Serilog;

namespace Boutique.Services;

public class DistributionFilePathService(SettingsViewModel settings, ILogger logger)
{
  private readonly ILogger _logger = logger.ForContext<DistributionFilePathService>();

  public string? UpdatePathForFormat(
    string? currentPath,
    bool isCreatingNewFile,
    string? newFileName,
    DistributionFileType format)
  {
    if (isCreatingNewFile)
    {
      return BuildPathFromNewFileName(newFileName, format);
    }

    if (!string.IsNullOrWhiteSpace(currentPath))
    {
      return BuildPathFromExistingFile(currentPath, format);
    }

    return null;
  }

  public string? BuildPathFromExistingFile(string currentPath, DistributionFileType format)
  {
    var dataPath = settings.SkyrimDataPath;
    if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
    {
      return null;
    }

    var currentFileName = Path.GetFileNameWithoutExtension(currentPath);
    if (string.IsNullOrWhiteSpace(currentFileName))
    {
      return null;
    }

    var baseName      = StripFormatSuffixes(currentFileName);
    var baseDirectory = GetBaseDirectory(dataPath);
    var result        = GetDistributionFilePath(baseDirectory, baseName, format);

    _logger.Debug(
      "Updated distribution file path for format {Format}: {Path}",
      format,
      result);

    return result;
  }

  public string? BuildPathFromNewFileName(string? newFileName, DistributionFileType format)
  {
    var targetDirectory = GetTargetDirectory();
    if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
    {
      return null;
    }

    if (string.IsNullOrWhiteSpace(newFileName))
    {
      return string.Empty;
    }

    var fileName = newFileName.Trim();
    var baseName = StripFormatSuffixes(fileName);

    return GetDistributionFilePath(targetDirectory, baseName, format);
  }

  public static string GetDistributionFilePath(string baseDirectory, string baseName, DistributionFileType format)
  {
    if (format == DistributionFileType.Spid)
    {
      return Path.Combine(baseDirectory, $"{baseName}_DISTR.ini");
    }

    var skyPatcherPath = PathUtilities.GetSkyPatcherNpcPath(baseDirectory);
    return Path.Combine(skyPatcherPath, $"{baseName}.ini");
  }

  private static string StripFormatSuffixes(string fileName)
  {
    var baseName = fileName;

    if (baseName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
    {
      baseName = baseName[..^4];
    }

    if (baseName.EndsWith("_DISTR", StringComparison.OrdinalIgnoreCase))
    {
      baseName = baseName[..^6];
    }

    return baseName;
  }

  private string GetBaseDirectory(string dataPath) =>
    !string.IsNullOrWhiteSpace(settings.OutputPatchPath)
      ? settings.OutputPatchPath
      : dataPath;

  private string? GetTargetDirectory()
  {
    if (!string.IsNullOrWhiteSpace(settings.OutputPatchPath) &&
        Directory.Exists(settings.OutputPatchPath))
    {
      return settings.OutputPatchPath;
    }

    return settings.SkyrimDataPath;
  }
}
