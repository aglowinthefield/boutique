using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Serilog;

namespace Boutique.Services;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public class ThemeService
{
    private const string ThemeConfigFileName = "theme.json";
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, ".config", ThemeConfigFileName);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger _logger;
    private AppTheme _currentThemeSetting = AppTheme.System;
    private bool _isCurrentlyDark = true;

    public static ThemeService? Current { get; private set; }

    public ThemeService(ILogger logger)
    {
        _logger = logger;
    }

    public AppTheme CurrentThemeSetting => _currentThemeSetting;
    public bool IsCurrentlyDark => _isCurrentlyDark;

    public event EventHandler<bool>? ThemeChanged;

    public void Initialize()
    {
        Current = this;
        _currentThemeSetting = LoadThemeSetting();
        ApplyTheme(_currentThemeSetting);

        // Subscribe to system theme changes
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    public void SetTheme(AppTheme theme)
    {
        _currentThemeSetting = theme;
        ApplyTheme(theme);
        SaveThemeSetting(theme);
    }

    public void ToggleTheme()
    {
        var newTheme = _isCurrentlyDark ? AppTheme.Light : AppTheme.Dark;
        SetTheme(newTheme);
    }

    private void ApplyTheme(AppTheme theme)
    {
        var isDark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            AppTheme.System => IsSystemDarkMode(),
            _ => true
        };

        _isCurrentlyDark = isDark;
        var palettePath = isDark ? "Themes/ColorPaletteDark.xaml" : "Themes/ColorPaletteLight.xaml";

        try
        {
            var app = Application.Current;
            if (app == null) return;

            // Find and remove existing color palette dictionaries
            var toRemove = app.Resources.MergedDictionaries
                .Where(d => d.Source?.OriginalString.Contains("ColorPalette") == true)
                .ToList();

            foreach (var dict in toRemove)
                app.Resources.MergedDictionaries.Remove(dict);

            // Add the new color palette FIRST (before Controls.xaml references it)
            var paletteDict = new ResourceDictionary
            {
                Source = new Uri(palettePath, UriKind.Relative)
            };
            app.Resources.MergedDictionaries.Insert(0, paletteDict);

            // Ensure Controls.xaml is loaded after the palette
            EnsureControlsLoaded(app);

            // Notify subscribers (like windows that need to update title bars)
            ThemeChanged?.Invoke(this, isDark);

            _logger.Information("Applied {Theme} theme (dark: {IsDark})", theme, isDark);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to apply theme {Theme}", theme);
        }
    }

    public void ApplyTitleBarTheme(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        var useDarkMode = _isCurrentlyDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        // Set window border color (Windows 11+)
        // Color format: 0x00BBGGRR
        uint borderColor = _isCurrentlyDark ? 0x00232323u : 0x00E0E0E0u;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
    }

    public static void ApplyTitleBarTheme(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero) return;

        var useDarkMode = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));

        // Set window border color (Windows 11+)
        uint borderColor = isDark ? 0x00232323u : 0x00E0E0E0u;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
    }

    private static void EnsureControlsLoaded(Application app)
    {
        // Check if Controls.xaml is already loaded
        var hasControls = app.Resources.MergedDictionaries
            .Any(d => d.Source?.OriginalString.Contains("Controls.xaml") == true);

        if (!hasControls)
        {
            // Load Controls.xaml after the color palette
            var controlsDict = new ResourceDictionary
            {
                Source = new Uri("Themes/Controls.xaml", UriKind.Relative)
            };
            app.Resources.MergedDictionaries.Add(controlsDict);
        }

        // Note: Brushes are now included directly in the theme files (ColorPaletteDark/Light.xaml)
        // so we no longer need to load ColorPalette.xaml separately
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value == 0; // 0 = dark mode, 1 = light mode
        }
        catch
        {
            // Default to dark if we can't read the registry
        }
        return true;
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_currentThemeSetting != AppTheme.System) return;

        // Re-apply system theme when Windows theme changes
        Application.Current?.Dispatcher.BeginInvoke(() => ApplyTheme(AppTheme.System));
    }

    private AppTheme LoadThemeSetting()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return AppTheme.System;

            var json = File.ReadAllText(ConfigPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Theme", out var themeElement))
            {
                var themeStr = themeElement.GetString();
                if (Enum.TryParse<AppTheme>(themeStr, true, out var theme))
                    return theme;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load theme setting");
        }
        return AppTheme.System;
    }

    private void SaveThemeSetting(AppTheme theme)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(new { Theme = theme.ToString() }, JsonOptions);
            File.WriteAllText(ConfigPath, json);

            _logger.Information("Saved theme setting: {Theme}", theme);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to save theme setting");
        }
    }
}
