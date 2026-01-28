using System.IO;
using System.Text.Json;
using System.Windows;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class GuiSettings
{
    public bool IsFilePreviewExpanded { get; set; }
    public string? SkyrimDataPath { get; set; }
    public string? OutputPatchPath { get; set; }
    public string? PatchFileName { get; set; }
    public SkyrimRelease SelectedSkyrimRelease { get; set; }
    public string? LastDistributionFilePath { get; set; }
    public string? Language { get; set; }
    public List<string>? BlacklistedPlugins { get; set; }

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public WindowState? WindowState { get; set; }

    public Dictionary<string, double>? GridSplitterPositions { get; set; }
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
        using (StartupProfiler.Instance.BeginOperation("GuiSettingsService.LoadSettings"))
        {
            LoadSettings();
        }
    }

    public bool IsFilePreviewExpanded
    {
        get => _settings.IsFilePreviewExpanded;
        set
        {
            if (_settings.IsFilePreviewExpanded == value)
            {
                return;
            }

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
            {
                return;
            }

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
            {
                return;
            }

            _settings.PatchFileName = value;
            SaveSettings();
        }
    }

    public string? OutputPatchPath
    {
        get => _settings.OutputPatchPath;
        set
        {
            if (_settings.OutputPatchPath == value)
            {
                return;
            }

            _settings.OutputPatchPath = value;
            SaveSettings();
        }
    }

    public SkyrimRelease SelectedSkyrimRelease
    {
        get => _settings.SelectedSkyrimRelease;
        set
        {
            if (_settings.SelectedSkyrimRelease == value)
            {
                return;
            }

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
            {
                return;
            }

            _settings.LastDistributionFilePath = value;
            SaveSettings();
        }
    }

    public string? Language
    {
        get => _settings.Language;
        set
        {
            if (_settings.Language == value)
            {
                return;
            }

            _settings.Language = value;
            SaveSettings();
        }
    }

    public List<string>? BlacklistedPlugins
    {
        get => _settings.BlacklistedPlugins;
        set
        {
            _settings.BlacklistedPlugins = value;
            SaveSettings();
        }
    }

    public void RestoreWindowGeometry(Window window)
    {
        if (_settings.WindowWidth.HasValue && _settings.WindowHeight.HasValue &&
            _settings.WindowWidth.Value > 0 && _settings.WindowHeight.Value > 0)
        {
            window.Width = _settings.WindowWidth.Value;
            window.Height = _settings.WindowHeight.Value;
        }

        if (_settings.WindowLeft.HasValue && _settings.WindowTop.HasValue)
        {
            var left = _settings.WindowLeft.Value;
            var top = _settings.WindowTop.Value;

            if (IsOnScreen(left, top, window.Width, window.Height))
            {
                window.Left = left;
                window.Top = top;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }

        if (_settings.WindowState.HasValue && _settings.WindowState.Value != WindowState.Minimized)
        {
            window.WindowState = _settings.WindowState.Value;
        }
    }

    public void SaveWindowGeometry(Window window)
    {
        if (window.WindowState == WindowState.Normal)
        {
            _settings.WindowLeft = window.Left;
            _settings.WindowTop = window.Top;
            _settings.WindowWidth = window.Width;
            _settings.WindowHeight = window.Height;
        }
        else if (window.WindowState == WindowState.Maximized)
        {
            _settings.WindowLeft = window.RestoreBounds.Left;
            _settings.WindowTop = window.RestoreBounds.Top;
            _settings.WindowWidth = window.RestoreBounds.Width;
            _settings.WindowHeight = window.RestoreBounds.Height;
        }

        _settings.WindowState = window.WindowState;
        SaveSettings();
    }

    public double? GetSplitterPosition(string key) =>
        _settings.GridSplitterPositions?.TryGetValue(key, out var position) == true ? position : null;

    public void SetSplitterPosition(string key, double position)
    {
        _settings.GridSplitterPositions ??= new Dictionary<string, double>();

        if (_settings.GridSplitterPositions.TryGetValue(key, out var existing) &&
            Math.Abs(existing - position) < 0.1)
        {
            return;
        }

        _settings.GridSplitterPositions[key] = position;
        SaveSettings();
    }

    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var virtualScreenLeft = SystemParameters.VirtualScreenLeft;
        var virtualScreenTop = SystemParameters.VirtualScreenTop;
        var virtualScreenWidth = SystemParameters.VirtualScreenWidth;
        var virtualScreenHeight = SystemParameters.VirtualScreenHeight;

        var virtualScreen = new Rect(virtualScreenLeft, virtualScreenTop, virtualScreenWidth, virtualScreenHeight);
        var windowRect = new Rect(left, top, width, height);

        if (!windowRect.IntersectsWith(virtualScreen))
        {
            return false;
        }

        var intersection = Rect.Intersect(windowRect, virtualScreen);
        return intersection.Width >= 100 && intersection.Height >= 100;
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
                _logger.Debug(
                    "Loaded GUI settings: IsFilePreviewExpanded={IsExpanded}",
                    _settings.IsFilePreviewExpanded);
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
            {
                Directory.CreateDirectory(dir);
            }

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
