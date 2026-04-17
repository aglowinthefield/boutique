using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Boutique.Services;

public record DistributionBackupInfo(string FilePath, DateTime CreatedUtc, long SizeBytes);

/// <summary>
///   Creates rotating, on-disk backups of distribution files before they are overwritten.
///   Backups are stored under %LOCALAPPDATA%\Boutique\Backups\ — outside the user's Skyrim
///   Data tree — so they survive MO2/Vortex virtual filesystem writes and are not picked up
///   by SPID / SkyPatcher's load scanner.
/// </summary>
public class DistributionFileBackupService
{
  public const int MaxBackupsPerFile = 15;

  private static readonly string BackupRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Boutique",
    "Backups");

  private readonly ILogger _logger;

  public DistributionFileBackupService(ILogger logger) =>
    _logger = logger.ForContext<DistributionFileBackupService>();

  /// <summary>
  ///   Copies the current contents of <paramref name="filePath" /> into a timestamped backup,
  ///   then prunes the backup directory down to <see cref="MaxBackupsPerFile" /> entries.
  ///   Never throws — backup failures must not block the user's save.
  /// </summary>
  public void CreateBackup(string filePath)
  {
    if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
    {
      return;
    }

    try
    {
      var backupDir = GetBackupDirectory(filePath);
      Directory.CreateDirectory(backupDir);

      var timestamp  = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
      var backupName = $"{Path.GetFileName(filePath)}.{timestamp}.bak";
      var backupPath = Path.Combine(backupDir, backupName);

      File.Copy(filePath, backupPath, overwrite: false);
      _logger.Debug("Created backup: {BackupPath}", backupPath);

      PruneOldBackups(backupDir);
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Failed to create backup for {Path}", filePath);
    }
  }

  public IReadOnlyList<DistributionBackupInfo> ListBackups(string filePath)
  {
    if (string.IsNullOrWhiteSpace(filePath))
    {
      return [];
    }

    var dir = GetBackupDirectory(filePath);
    if (!Directory.Exists(dir))
    {
      return [];
    }

    return new DirectoryInfo(dir)
           .EnumerateFiles("*.bak")
           .OrderByDescending(f => f.CreationTimeUtc)
           .Select(f => new DistributionBackupInfo(f.FullName, f.CreationTimeUtc, f.Length))
           .ToList();
  }

  /// <summary>
  ///   Returns the per-file backup directory. Scheme: <c>&lt;filename&gt;_&lt;shortHash&gt;</c>
  ///   inside the global backup root. The filename component is human-readable; the hash
  ///   disambiguates same-named files living in different paths (e.g. two mods each shipping
  ///   a <c>Boutique_Distribution.ini</c>).
  /// </summary>
  public static string GetBackupDirectory(string filePath)
  {
    var shortHash  = ShortHashOfPath(filePath);
    var safeName   = Path.GetFileName(filePath);
    var folderName = $"{safeName}_{shortHash}";
    return Path.Combine(BackupRoot, folderName);
  }

  private void PruneOldBackups(string backupDir)
  {
    var backups = new DirectoryInfo(backupDir)
                  .EnumerateFiles("*.bak")
                  .OrderByDescending(f => f.CreationTimeUtc)
                  .ToList();

    foreach (var stale in backups.Skip(MaxBackupsPerFile))
    {
      try
      {
        stale.Delete();
      }
      catch (Exception ex)
      {
        _logger.Debug(ex, "Failed to prune stale backup {File}", stale.FullName);
      }
    }
  }

  private static string ShortHashOfPath(string filePath)
  {
    var normalized = filePath.ToLowerInvariant();
    var bytes      = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    return Convert.ToHexString(bytes).Substring(0, 8).ToLowerInvariant();
  }
}
