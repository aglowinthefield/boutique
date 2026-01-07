using System.IO;
using System.Reactive.Linq;
using System.Windows.Input;
using Boutique.Models;
using Boutique.Services;
using Microsoft.Win32;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

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
    private readonly CrossSessionCacheService _cacheService;
    private readonly ThemeService _themeService;
    private readonly TutorialService _tutorialService;

    public SettingsViewModel(
        PatcherSettings settings,
        CrossSessionCacheService cacheService,
        ThemeService themeService,
        TutorialService tutorialService)
    {
        _settings = settings;
        _cacheService = cacheService;
        _themeService = themeService;
        _tutorialService = tutorialService;

        SkyrimDataPath = settings.SkyrimDataPath;
        OutputPatchPath = settings.OutputPatchPath;
        PatchFileName = settings.PatchFileName;
        SelectedTheme = (ThemeOption)_themeService.CurrentThemeSetting;

        this.WhenAnyValue(x => x.SkyrimDataPath)
            .Skip(1)
            .Subscribe(v => _settings.SkyrimDataPath = v);

        this.WhenAnyValue(x => x.OutputPatchPath)
            .Skip(1)
            .Subscribe(v => _settings.OutputPatchPath = v);

        this.WhenAnyValue(x => x.PatchFileName)
            .Skip(1)
            .Subscribe(v => _settings.PatchFileName = v);

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
                SkyrimDataPath = folder;
        }
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
        var mo2DataPath = Environment.GetEnvironmentVariable("MO_DATAPATH");
        if (!string.IsNullOrEmpty(mo2DataPath) && Directory.Exists(mo2DataPath))
        {
            SkyrimDataPath = mo2DataPath;
            OutputPatchPath = mo2DataPath;
            IsRunningFromMO2 = true;
            DetectionSource = "Detected from Mod Organizer 2";
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
                DetectionSource = "Detected from Mod Organizer 2 (Game Path)";
                return;
            }
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
