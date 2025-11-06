using System.IO;
using System.Windows.Input;
using Boutique.Models;
using Microsoft.Win32;
using ReactiveUI;

namespace Boutique.ViewModels;

public class SettingsViewModel : ReactiveObject
{
    private readonly PatcherSettings _settings;
    private string _detectionSource;
    private bool _isRunningFromMO2;
    private string _outputPatchPath;
    private string _patchFileName;
    private string _skyrimDataPath;

    public SettingsViewModel(PatcherSettings settings)
    {
        _settings = settings;
        _skyrimDataPath = settings.SkyrimDataPath;
        _outputPatchPath = settings.OutputPatchPath;
        _patchFileName = settings.PatchFileName;
        _detectionSource = "";

        BrowseDataPathCommand = ReactiveCommand.Create(BrowseDataPath);
        BrowseOutputPathCommand = ReactiveCommand.Create(BrowseOutputPath);
        AutoDetectPathCommand = ReactiveCommand.Create(AutoDetectPath);

        // Auto-detect on creation if path is empty
        if (string.IsNullOrEmpty(_skyrimDataPath)) AutoDetectPath();
    }

    public bool IsRunningFromMO2
    {
        get => _isRunningFromMO2;
        set => this.RaiseAndSetIfChanged(ref _isRunningFromMO2, value);
    }

    public string DetectionSource
    {
        get => _detectionSource;
        set => this.RaiseAndSetIfChanged(ref _detectionSource, value);
    }

    public string SkyrimDataPath
    {
        get => _skyrimDataPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _skyrimDataPath, value);
            _settings.SkyrimDataPath = value;
        }
    }

    public string OutputPatchPath
    {
        get => _outputPatchPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _outputPatchPath, value);
            _settings.OutputPatchPath = value;
        }
    }

    public string PatchFileName
    {
        get => _patchFileName;
        set
        {
            this.RaiseAndSetIfChanged(ref _patchFileName, value);
            _settings.PatchFileName = value;
        }
    }

    public string FullOutputPath => Path.Combine(OutputPatchPath, PatchFileName);

    public ICommand BrowseDataPathCommand { get; }
    public ICommand BrowseOutputPathCommand { get; }
    public ICommand AutoDetectPathCommand { get; }

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
        // PRIORITY 1: Check for Mod Organizer 2 environment variables
        // MO2 sets these when launching executables
        var mo2DataPath = Environment.GetEnvironmentVariable("MO_DATAPATH");
        if (!string.IsNullOrEmpty(mo2DataPath) && Directory.Exists(mo2DataPath))
        {
            SkyrimDataPath = mo2DataPath;
            OutputPatchPath = mo2DataPath;
            IsRunningFromMO2 = true;
            DetectionSource = "Detected from Mod Organizer 2";
            return;
        }

        // Alternative: Try MO_GAMEPATH and append Data
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

        // PRIORITY 2: Try common Skyrim SE installation paths
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

        // PRIORITY 3: Try reading from registry
        try
        {
            using var key =
                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim Special Edition");
            if (key != null)
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
            // Registry read failed, ignore
            IsRunningFromMO2 = false;
            DetectionSource = "Auto-detection failed - please set manually";
        }
    }
}