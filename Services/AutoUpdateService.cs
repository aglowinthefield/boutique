using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using AutoUpdaterDotNET;
using Boutique.Views;
using Serilog;

namespace Boutique.Services;

public partial class AutoUpdateService
{
  private string _pendingReleaseNotes = string.Empty;
  private bool   _forceShowUpdate;

  public void CheckForUpdates(bool forceShow = false)
  {
    if (!forceShow && GuiSettingsService.Current?.AutoUpdateEnabled != true)
    {
      return;
    }

    _forceShowUpdate = forceShow;

    try
    {
      AutoUpdater.ReportErrors     = forceShow;
      AutoUpdater.RunUpdateAsAdmin = false;
      AutoUpdater.HttpUserAgent    = "Boutique-Updater";
      AutoUpdater.InstallationPath = AppDomain.CurrentDomain.BaseDirectory;

      var installedVersion = GetInstalledVersion();
      if (installedVersion != null)
      {
        AutoUpdater.InstalledVersion = installedVersion;
        Log.Information("Current installed version: {Version}", installedVersion);
      }

      AutoUpdater.ParseUpdateInfoEvent -= ParseGitHubReleases;
      AutoUpdater.ParseUpdateInfoEvent += ParseGitHubReleases;
      AutoUpdater.CheckForUpdateEvent  -= OnCheckForUpdate;
      AutoUpdater.CheckForUpdateEvent  += OnCheckForUpdate;

      const string updateUrl = "https://api.github.com/repos/aglowinthefield/Boutique/releases";

      AutoUpdater.Start(updateUrl);
      Log.Information("Update check initiated (forceShow: {ForceShow}).", forceShow);
    }
    catch (Exception ex)
    {
      Log.Warning(ex, "Failed to check for updates.");
    }
  }

  private void OnCheckForUpdate(UpdateInfoEventArgs args)
  {
    if (args.IsUpdateAvailable)
    {
      var dialog = new UpdateDialog
                   {
                     CurrentVersion = (AutoUpdater.InstalledVersion ?? new Version(0, 0, 0)).ToString(),
                     LatestVersion  = args.CurrentVersion,
                     ReleaseNotes   = _pendingReleaseNotes,
                     DownloadUrl    = args.DownloadURL,
                     Owner          = Application.Current.MainWindow,
                     DataContext    = null
                   };
      dialog.DataContext = dialog;

      if (dialog.ShowDialog() == true && dialog.Result == UpdateResult.Update)
      {
        try
        {
          if (AutoUpdater.DownloadUpdate(args))
          {
            Application.Current.Shutdown();
          }
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Failed to download update.");
          MessageBox.Show(
            $"Failed to download update: {ex.Message}",
            "Update Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        }
      }
    }
    else if (_forceShowUpdate)
    {
      MessageBox.Show(
        "You are running the latest version.",
        "No Update Available",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
    }
  }

  private static Version? GetInstalledVersion()
  {
    var assembly = Assembly.GetExecutingAssembly();
    var infoVersion = assembly
                      .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                      .OfType<AssemblyInformationalVersionAttribute>()
                      .FirstOrDefault()?.InformationalVersion;

    return string.IsNullOrEmpty(infoVersion) ? assembly.GetName().Version : ParseSemanticVersion(infoVersion);
  }

  private void ParseGitHubReleases(ParseUpdateInfoEventArgs args)
  {
    try
    {
      using var doc      = JsonDocument.Parse(args.RemoteData);
      var       releases = doc.RootElement;

      if (releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() == 0)
      {
        Log.Warning("No releases found in GitHub response.");
        return;
      }

      var installedVersion = AutoUpdater.InstalledVersion ?? new Version(0, 0, 0);
      var newerReleases    = new List<(Version Version, string Tag, string Body, string? DownloadUrl)>();

      foreach (var release in releases.EnumerateArray())
      {
        var tagName       = release.GetProperty("tag_name").GetString() ?? string.Empty;
        var parsedVersion = ParseSemanticVersion(tagName);
        if (parsedVersion == null || parsedVersion <= installedVersion)
        {
          continue;
        }

        var body = release.TryGetProperty("body", out var bodyProp)
                     ? bodyProp.GetString() ?? string.Empty
                     : string.Empty;

        string? downloadUrl = null;
        if (release.TryGetProperty("assets", out var assets))
        {
          foreach (var asset in assets.EnumerateArray())
          {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
              downloadUrl = asset.GetProperty("browser_download_url").GetString();
              break;
            }
          }
        }

        newerReleases.Add((parsedVersion, tagName, body, downloadUrl));
      }

      if (newerReleases.Count == 0)
      {
        Log.Information("No newer releases found. Current version: {Version}", installedVersion);
        return;
      }

      newerReleases.Sort((a, b) => b.Version.CompareTo(a.Version));
      var latest = newerReleases[0];

      if (string.IsNullOrEmpty(latest.DownloadUrl))
      {
        Log.Warning("No zip asset found in latest release {Tag}.", latest.Tag);
        return;
      }

      var sb = new StringBuilder();
      foreach (var (_, tag, body, _) in newerReleases)
      {
        sb.AppendLine($"═══ {tag} ═══")
          .AppendLine()
          .AppendLine(string.IsNullOrWhiteSpace(body) ? "(No release notes)" : body.Trim())
          .AppendLine();
      }

      _pendingReleaseNotes = sb.ToString().TrimEnd();

      args.UpdateInfo = new UpdateInfoEventArgs
                        {
                          CurrentVersion = latest.Version.ToString(),
                          DownloadURL    = latest.DownloadUrl,
                          Mandatory      = new Mandatory { Value = false }
                        };

      Log.Information(
        "Found {Count} newer release(s). Latest: {Version}",
        newerReleases.Count,
        latest.Version);
    }
    catch (Exception ex)
    {
      Log.Warning(ex, "Failed to parse GitHub releases info.");
    }
  }

  private static Version? ParseSemanticVersion(string versionString)
  {
    versionString = versionString.TrimStart('v');
    var match = SemanticVersionRegex().Match(versionString);
    if (!match.Success)
    {
      return null;
    }

    var major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
    var patch = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

    return new Version(major, minor, patch);
  }

  [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)")]
  private static partial Regex SemanticVersionRegex();
}
