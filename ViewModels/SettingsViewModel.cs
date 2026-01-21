using System.IO;
using System.Reactive.Linq;
using System.Windows.Input;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using Microsoft.Win32;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Installs;
using Mutagen.Bethesda.Skyrim;
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
    private readonly ThemeService _themeService;
    private readonly TutorialService _tutorialService;
    private readonly LocalizationService _localizationService;

    public SettingsViewModel(
        PatcherSettings settings,
        GuiSettingsService guiSettings,
        ThemeService themeService,
        TutorialService tutorialService,
        LocalizationService localizationService)
    {
        _settings = settings;
        _guiSettings = guiSettings;
        _themeService = themeService;
        _tutorialService = tutorialService;
        _localizationService = localizationService;

        var savedDataPath = !string.IsNullOrEmpty(guiSettings.SkyrimDataPath) ? guiSettings.SkyrimDataPath : settings.SkyrimDataPath;
        SkyrimDataPath = NormalizeDataPath(savedDataPath);
        PatchFileName = !string.IsNullOrEmpty(guiSettings.PatchFileName) ? guiSettings.PatchFileName : settings.PatchFileName;
        OutputPatchPath = guiSettings.OutputPatchPath ?? string.Empty;
        SelectedSkyrimRelease = guiSettings.SelectedSkyrimRelease != default ? guiSettings.SelectedSkyrimRelease : settings.SelectedSkyrimRelease;
        _settings.SelectedSkyrimRelease = SelectedSkyrimRelease;

        SelectedTheme = (ThemeOption)_themeService.CurrentThemeSetting;
        SelectedFontScale = _themeService.CurrentFontScale;

        this.WhenAnyValue(x => x.SkyrimDataPath)
            .Skip(1)
            .Subscribe(v =>
            {
                _settings.SkyrimDataPath = v;
                _guiSettings.SkyrimDataPath = v;
                this.RaisePropertyChanged(nameof(FullOutputPath));
            });

        this.WhenAnyValue(x => x.PatchFileName)
            .Skip(1)
            .Subscribe(v =>
            {
                _settings.PatchFileName = v;
                _guiSettings.PatchFileName = v;
                this.RaisePropertyChanged(nameof(FullOutputPath));
            });

        this.WhenAnyValue(x => x.OutputPatchPath)
            .Skip(1)
            .Subscribe(v =>
            {
                _guiSettings.OutputPatchPath = v;
                this.RaisePropertyChanged(nameof(FullOutputPath));
            });

        this.WhenAnyValue(x => x.SelectedSkyrimRelease)
            .Skip(1)
            .Subscribe(release =>
            {
                _settings.SelectedSkyrimRelease = release;
                _guiSettings.SelectedSkyrimRelease = release;
            });

        this.WhenAnyValue(x => x.SelectedTheme)
            .Skip(1)
            .Subscribe(theme =>
            {
                _themeService.SetTheme((AppTheme)theme);
                ShowRestartDialog();
            });

        this.WhenAnyValue(x => x.SelectedFontScale)
            .Skip(1)
            .Subscribe(scale => _themeService.SetFontScale(scale));

        SelectedLanguage = _localizationService.GetCurrentLanguageOption() ?? AvailableLanguages[0];

        this.WhenAnyValue(x => x.SelectedLanguage)
            .Skip(1)
            .Where(lang => lang != null)
            .Subscribe(lang => _localizationService.SetLanguage(lang!.Code));

        BrowseDataPathCommand = new RelayCommand(BrowseDataPath);
        BrowseOutputPathCommand = new RelayCommand(BrowseOutputPath);
        AutoDetectPathCommand = new RelayCommand(AutoDetectPath);
        RestartTutorialCommand = new RelayCommand(RestartTutorial);

        if (string.IsNullOrEmpty(SkyrimDataPath))
            AutoDetectPath();
    }

    [Reactive] public bool IsRunningFromMO2 { get; set; }
    [Reactive] public string DetectionSource { get; set; } = string.Empty;
    [Reactive] public bool DetectionFailed { get; set; }
    [Reactive] public string SkyrimDataPath { get; set; } = string.Empty;
    [Reactive] public string OutputPatchPath { get; set; } = string.Empty;
    [Reactive] public string PatchFileName { get; set; } = string.Empty;
    [Reactive] public SkyrimRelease SelectedSkyrimRelease { get; set; }
    [Reactive] public ThemeOption SelectedTheme { get; set; }
    [Reactive] public LanguageOption? SelectedLanguage { get; set; }
    [Reactive] public double SelectedFontScale { get; set; }

    public IReadOnlyList<SkyrimRelease> SkyrimReleaseOptions { get; } = new[]
    {
        SkyrimRelease.SkyrimSE,
        SkyrimRelease.SkyrimVR,
        SkyrimRelease.SkyrimSEGog
    };
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = Enum.GetValues<ThemeOption>();
    public IReadOnlyList<LanguageOption> AvailableLanguages => _localizationService.AvailableLanguages;
    public IReadOnlyList<double> FontScaleOptions { get; } = [0.85, 1.0, 1.15, 1.3];

    public string FullOutputPath
    {
        get
        {
            var folder = !string.IsNullOrWhiteSpace(OutputPatchPath) ? OutputPatchPath : SkyrimDataPath;
            var fileName = string.IsNullOrWhiteSpace(PatchFileName) ? "BoutiquePatch.esp" : PatchFileName;
            return string.IsNullOrWhiteSpace(folder) ? fileName : Path.Combine(folder, fileName);
        }
    }

    public ICommand BrowseDataPathCommand { get; }
    public ICommand BrowseOutputPathCommand { get; }
    public ICommand AutoDetectPathCommand { get; }
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

    private void BrowseOutputPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Output Folder for Patch",
            FileName = "Select Folder",
            Filter = "Folder|*.none",
            CheckFileExists = false,
            CheckPathExists = true
        };

        if (!string.IsNullOrEmpty(OutputPatchPath) && Directory.Exists(OutputPatchPath))
            dialog.InitialDirectory = OutputPatchPath;
        else if (!string.IsNullOrEmpty(SkyrimDataPath) && Directory.Exists(SkyrimDataPath))
            dialog.InitialDirectory = SkyrimDataPath;

        if (dialog.ShowDialog() == true)
        {
            var folder = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(folder))
                OutputPatchPath = folder;
        }
    }

    private static string NormalizeDataPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return path ?? string.Empty;

        var hasPlugins = PathUtilities.HasPluginFiles(path);

        if (hasPlugins)
            return path;

        var dataSubfolder = Path.Combine(path, "Data");
        if (Directory.Exists(dataSubfolder))
        {
            var subfolderHasPlugins = PathUtilities.HasPluginFiles(dataSubfolder);
            if (subfolderHasPlugins)
            {
                Log.Information(
                    "Auto-corrected data path: {Original} -> {Corrected} (found Data subfolder with plugins)",
                    path, dataSubfolder);
                return dataSubfolder;
            }
        }

        Log.Warning(
            "Data path {Path} contains no .esp/.esm files and no Data subfolder was found. " +
                    "Plugins may not load correctly. For Wabbajack modlists, select the 'Game Root\\Data' folder.", path);
        return path;
    }

    private void AutoDetectPath()
    {
        var (gameRelease, gameName) = GetGameInfo(SelectedSkyrimRelease);

        if (GameLocations.TryGetDataFolder(gameRelease, out var dataFolder))
        {
            SkyrimDataPath = dataFolder.Path;
            DetectionFailed = false;
        }
        else
        {
            DetectionFailed = true;
        }

        DetectionSource = GetDetectionMessage(gameName, !DetectionFailed);
    }

    private static (GameRelease GameRelease, string GameName) GetGameInfo(SkyrimRelease release)
    {
        return release switch
        {
            SkyrimRelease.SkyrimVR => (GameRelease.SkyrimVR, "Skyrim VR"),
            SkyrimRelease.SkyrimSEGog => (GameRelease.SkyrimSEGog, "Skyrim SE (GOG)"),
            _ => (GameRelease.SkyrimSE, "Skyrim SE")
        };
    }

    private static string GetDetectionMessage(string gameName, bool success) =>
        success ? $"Detected {gameName} using Mutagen"
                : $"Auto-detection failed for {gameName} - please set manually";

    private void RestartTutorial()
    {
        if (!FeatureFlags.TutorialEnabled)
            return;

        _tutorialService.ResetTutorial();
        _tutorialService.StartTutorial();
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
