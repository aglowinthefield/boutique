using System.IO;
using System.Text.Json;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class GuiSettings
{
    public bool IsFilePreviewExpanded { get; set; }
    public string? SkyrimDataPath { get; set; }
    public string? PatchFileName { get; set; }
    public SkyrimRelease SelectedSkyrimRelease { get; set; }
    public string? LastDistributionFilePath { get; set; }
}

public class GuiSettingsService
{
    private const string SettingsFileName = "gui-settings.json";

    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, ".config", SettingsFileName);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger _logger;
    private GuiSettings _settings = new();

    public GuiSettingsService(ILogger logger)
    {
        _logger = logger.ForContext<GuiSettingsService>();
        LoadSettings();
    }

    public bool IsFilePreviewExpanded
    {
        get => _settings.IsFilePreviewExpanded;
        set
        {
            if (_settings.IsFilePreviewExpanded == value)
                return;
            _settings.IsFilePreviewExpanded = value;
            SaveSettings();
        }
    }

    public string? SkyrimDataPath
    {
        get => _settings.SkyrimDataPath;
        set
        {
            if (_settings.SkyrimDataPath == value)
                return;
            _settings.SkyrimDataPath = value;
            SaveSettings();
        }
    }

    public string? PatchFileName
    {
        get => _settings.PatchFileName;
        set
        {
            if (_settings.PatchFileName == value)
                return;
            _settings.PatchFileName = value;
            SaveSettings();
        }
    }

    public SkyrimRelease SelectedSkyrimRelease
    {
        get => _settings.SelectedSkyrimRelease;
        set
        {
            if (_settings.SelectedSkyrimRelease == value)
                return;
            _settings.SelectedSkyrimRelease = value;
            SaveSettings();
        }
    }

    public string? LastDistributionFilePath
    {
        get => _settings.LastDistributionFilePath;
        set
        {
            if (_settings.LastDistributionFilePath == value)
                return;
            _settings.LastDistributionFilePath = value;
            SaveSettings();
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                _logger.Debug("GUI settings file not found, using defaults");
                return;
            }

            var json = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize<GuiSettings>(json);
            if (loaded != null)
            {
                _settings = loaded;
                _logger.Debug("Loaded GUI settings: IsFilePreviewExpanded={IsExpanded}", _settings.IsFilePreviewExpanded);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load GUI settings, using defaults");
        }
    }

    private void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            File.WriteAllText(ConfigPath, json);

            _logger.Debug("Saved GUI settings");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to save GUI settings");
        }
    }
}
