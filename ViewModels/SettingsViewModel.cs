using System.IO;
using System.Reactive.Linq;
using System.Windows.Input;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Microsoft.Win32;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Boutique.ViewModels;

public enum ThemeOption
{
    System,
    Light,
    Dark
}

public class RelayCommand(Action execute) : ICommand
{
    private readonly Action _execute = execute;

#pragma warning disable CS0067 // Event is never used - required by ICommand interface
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute();
}

public class SettingsViewModel : ReactiveObject
{
    private readonly PatcherSettings _settings;
    private readonly GuiSettingsService _guiSettings;
    private readonly CrossSessionCacheService _cacheService;
    private readonly ThemeService _themeService;
    private readonly TutorialService _tutorialService;

    public SettingsViewModel(
        PatcherSettings settings,
        GuiSettingsService guiSettings,
        CrossSessionCacheService cacheService,
        ThemeService themeService,
        TutorialService tutorialService)
    {
        _settings = settings;
        _guiSettings = guiSettings;
        _cacheService = cacheService;
        _themeService = themeService;
        _tutorialService = tutorialService;

        var savedDataPath = !string.IsNullOrEmpty(guiSettings.SkyrimDataPath) ? guiSettings.SkyrimDataPath : settings.SkyrimDataPath;
        SkyrimDataPath = NormalizeDataPath(savedDataPath);
        OutputPatchPath = !string.IsNullOrEmpty(guiSettings.OutputPatchPath) ? guiSettings.OutputPatchPath : settings.OutputPatchPath;
        PatchFileName = !string.IsNullOrEmpty(guiSettings.PatchFileName) ? guiSettings.PatchFileName : settings.PatchFileName;
        SelectedTheme = (ThemeOption)_themeService.CurrentThemeSetting;

        this.WhenAnyValue(x => x.SkyrimDataPath)
            .Skip(1)
            .Subscribe(v =>
            {
                var previousPath = _settings.SkyrimDataPath;
                _settings.SkyrimDataPath = v;
                _guiSettings.SkyrimDataPath = v;

                // Invalidate cache when data path changes - cached data is path-specific
                if (!string.Equals(previousPath, v, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Data path changed from {OldPath} to {NewPath}, invalidating cache", previousPath, v);
                    _cacheService.InvalidateCache();
                    RefreshCacheStatus();
                }
            });

        this.WhenAnyValue(x => x.OutputPatchPath)
            .Skip(1)
            .Subscribe(v =>
            {
                _settings.OutputPatchPath = v;
                _guiSettings.OutputPatchPath = v;
            });

        this.WhenAnyValue(x => x.PatchFileName)
            .Skip(1)
            .Subscribe(v =>
            {
                _settings.PatchFileName = v;
                _guiSettings.PatchFileName = v;
            });

        this.WhenAnyValue(x => x.SelectedTheme)
            .Skip(1)
            .Subscribe(theme =>
            {
                _themeService.SetTheme((AppTheme)theme);
                ShowRestartDialog();
            });

        BrowseDataPathCommand = new RelayCommand(BrowseDataPath);
        BrowseOutputPathCommand = new RelayCommand(BrowseOutputPath);
        AutoDetectPathCommand = new RelayCommand(AutoDetectPath);
        ClearCacheCommand = new RelayCommand(ClearCache);
        RestartTutorialCommand = new RelayCommand(RestartTutorial);

        if (string.IsNullOrEmpty(SkyrimDataPath))
            AutoDetectPath();

        RefreshCacheStatus();
    }

    [Reactive] public bool IsRunningFromMO2 { get; set; }
    [Reactive] public string DetectionSource { get; set; } = "";
    [Reactive] public string SkyrimDataPath { get; set; } = "";
    [Reactive] public string OutputPatchPath { get; set; } = "";
    [Reactive] public string PatchFileName { get; set; } = "";
    [Reactive] public string CacheStatus { get; set; } = "No cache";
    [Reactive] public bool HasCache { get; set; }
    [Reactive] public ThemeOption SelectedTheme { get; set; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = Enum.GetValues<ThemeOption>();

    public string FullOutputPath => Path.Combine(OutputPatchPath, PatchFileName);

    public ICommand BrowseDataPathCommand { get; }
    public ICommand BrowseOutputPathCommand { get; }
    public ICommand AutoDetectPathCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand RestartTutorialCommand { get; }

    public static bool IsTutorialEnabled => FeatureFlags.TutorialEnabled;

    private void BrowseDataPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Skyrim Data Folder",
            FileName = "Select Folder",
            Filter = "Folder|*.none",
            CheckFileExists = false,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            var folder = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(folder))
                SkyrimDataPath = NormalizeDataPath(folder);
        }
    }

    private static string NormalizeDataPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return path ?? "";

        // Check if the current path has plugins
        var hasPlugins = PathUtilities.HasPluginFiles(path);

        if (hasPlugins)
            return path;

        // Common mistake: user selected "Game Root" instead of "Game Root\Data"
        // This is common with Wabbajack modlists
        var dataSubfolder = Path.Combine(path, "Data");
        if (Directory.Exists(dataSubfolder))
        {
            var subfolderHasPlugins = PathUtilities.HasPluginFiles(dataSubfolder);
            if (subfolderHasPlugins)
            {
                Log.Information("Auto-corrected data path: {Original} -> {Corrected} (found Data subfolder with plugins)",
                    path, dataSubfolder);
                return dataSubfolder;
            }
        }

        Log.Warning("Data path {Path} contains no .esp/.esm files and no Data subfolder was found. " +
                    "Plugins may not load correctly. For Wabbajack modlists, select the 'Game Root\\Data' folder.", path);
        return path;
    }

    private void BrowseOutputPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Output Folder",
            FileName = "Select Folder",
            Filter = "Folder|*.none",
            CheckFileExists = false,
            CheckPathExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            var folder = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(folder))
                OutputPatchPath = folder;
        }
    }

    private void AutoDetectPath()
    {
        // Try various MO2 environment variables - different MO2 versions/configs set different ones
        var mo2DataPath = Environment.GetEnvironmentVariable("MO_DATAPATH");
        if (!string.IsNullOrEmpty(mo2DataPath) && Directory.Exists(mo2DataPath))
        {
            SkyrimDataPath = mo2DataPath;
            OutputPatchPath = mo2DataPath;
            IsRunningFromMO2 = true;
            DetectionSource = "Detected from Mod Organizer 2 (MO_DATAPATH)";
            return;
        }

        var mo2GamePath = Environment.GetEnvironmentVariable("MO_GAMEPATH");
        if (!string.IsNullOrEmpty(mo2GamePath))
        {
            var dataPath = Path.Combine(mo2GamePath, "Data");
            if (Directory.Exists(dataPath))
            {
                SkyrimDataPath = dataPath;
                OutputPatchPath = dataPath;
                IsRunningFromMO2 = true;
                DetectionSource = "Detected from Mod Organizer 2 (MO_GAMEPATH)";
                return;
            }
        }

        // MO2 2.5+ may use different variable names
        var mo2VirtualPath = Environment.GetEnvironmentVariable("VIRTUAL_STORE");
        if (!string.IsNullOrEmpty(mo2VirtualPath) && Directory.Exists(mo2VirtualPath))
        {
            SkyrimDataPath = mo2VirtualPath;
            OutputPatchPath = mo2VirtualPath;
            IsRunningFromMO2 = true;
            DetectionSource = "Detected from Mod Organizer 2 (VIRTUAL_STORE)";
            return;
        }

        // Check if running under USVFS (MO2's virtual filesystem hook)
        var usvfsLog = Environment.GetEnvironmentVariable("USVFS_LOGFILE");
        var mo2Profile = Environment.GetEnvironmentVariable("MO_PROFILE");
        if (!string.IsNullOrEmpty(usvfsLog) || !string.IsNullOrEmpty(mo2Profile))
        {
            // We're running under MO2's USVFS but didn't get the data path
            // Log this for debugging - the VFS should make the Data folder work
            IsRunningFromMO2 = true;
            DetectionSource = "Running under Mod Organizer 2 USVFS (data path not explicitly set)";
            // Don't return - fall through to find the game's Data folder which USVFS will virtualize
        }

        var commonPaths = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition\Data",
            @"C:\Program Files\Steam\steamapps\common\Skyrim Special Edition\Data",
            @"D:\Steam\steamapps\common\Skyrim Special Edition\Data",
            @"E:\Steam\steamapps\common\Skyrim Special Edition\Data",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) +
            @"\Steam\steamapps\common\Skyrim Special Edition\Data"
        };

        foreach (var path in commonPaths)
            if (Directory.Exists(path))
            {
                SkyrimDataPath = path;
                OutputPatchPath = path;
                IsRunningFromMO2 = false;
                DetectionSource = "Detected from common installation path";
                return;
            }

        try
        {
            using var key =
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim Special Edition");
            if (key is not null)
            {
                var installPath = key.GetValue("installed path") as string;
                if (!string.IsNullOrEmpty(installPath))
                {
                    var dataPath = Path.Combine(installPath, "Data");
                    if (Directory.Exists(dataPath))
                    {
                        SkyrimDataPath = dataPath;
                        OutputPatchPath = dataPath;
                        IsRunningFromMO2 = false;
                        DetectionSource = "Detected from Windows Registry";
                    }
                }
            }
        }
        catch
        {
            IsRunningFromMO2 = false;
            DetectionSource = "Auto-detection failed - please set manually";
        }
    }

    private void ClearCache()
    {
        _cacheService.InvalidateCache();
        RefreshCacheStatus();
    }

    private void RestartTutorial()
    {
        if (!FeatureFlags.TutorialEnabled)
            return;

        _tutorialService.ResetTutorial();
        _tutorialService.StartTutorial();
    }

    public void RefreshCacheStatus()
    {
        var info = _cacheService.GetCacheInfo();
        if (info is null)
        {
            CacheStatus = "No cache";
            HasCache = false;
        }
        else
        {
            CacheStatus = $"Cache: {info.FileSizeFormatted}, updated {info.LastModifiedFormatted}";
            HasCache = true;
        }
    }

    private static void ShowRestartDialog()
    {
        var dialog = new Views.RestartDialog
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();

        if (dialog.QuitNow)
            System.Windows.Application.Current.Shutdown();
    }
}
