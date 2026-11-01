using Boutique.Models;
using Boutique.Services;
using Boutique.ViewModels;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

public sealed class ConnectedDraftOutputSettingsTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), "boutique-output-settings-" + Guid.NewGuid());
  private readonly LoggingService _logging = new();

  public ConnectedDraftOutputSettingsTests() => Directory.CreateDirectory(_directory);

  [Theory]
  [InlineData("file")]
  [InlineData("name")]
  [InlineData("extension")]
  [InlineData("directory")]
  [InlineData("data")]
  public void RejectedOutputChangeLeavesSettingsAndWorkspaceAssociated(string change)
  {
    var (settings, gui, lifecycle) = Workspace();
    lifecycle.AddOutfit("Draft", [new(new FormKey(ModKey.FromFileName("Source.esp"), 0x800))]);
    var original = settings.FullOutputPath;
    var serialized = File.ReadAllText(Path.Combine(_directory, "settings.json"));
    Assert.Throws<InvalidOperationException>(() =>
    {
      switch (change)
      {
        case "file": settings.PatchFileName = "Other.esp"; break;
        case "name": settings.PatchFileBaseName = "Other"; break;
        case "extension": settings.SelectedPatchExtension = ".esl"; break;
        case "directory": settings.OutputPatchPath = Path.Combine(_directory, "Other"); break;
        case "data": settings.SkyrimDataPath = Path.Combine(_directory, "OtherData"); break;
      }
    });
    Assert.Equal(original, settings.FullOutputPath);
    Assert.Equal(original, lifecycle.OutputPath);
    Assert.Equal("Output", settings.PatchFileBaseName);
    Assert.Equal(".esp", settings.SelectedPatchExtension);
    Assert.Equal("Output.esp", gui.PatchFileName);
    Assert.Equal(serialized, File.ReadAllText(Path.Combine(_directory, "settings.json")));
  }

  [Fact]
  public void DirectoryOnlyChangePublishesFullPathAfterWorkspaceSwitch()
  {
    var (settings, _, lifecycle) = Workspace();
    var changed = new List<string>();
    settings.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName != nameof(SettingsViewModel.FullOutputPath)) return;
      Assert.Equal(settings.FullOutputPath, lifecycle.OutputPath);
      changed.Add(settings.FullOutputPath);
    };
    var next = Path.Combine(_directory, "Other");
    Directory.CreateDirectory(next);
    settings.OutputPatchPath = next;
    Assert.Equal([Path.Combine(next, "Output.esp")], changed);
  }

  private (SettingsViewModel settings, GuiSettingsService gui, ConnectedDraftLifecycle lifecycle) Workspace()
  {
    var gui = new GuiSettingsService(_logging.Logger, Path.Combine(_directory, "settings.json"))
    {
      SkyrimDataPath = _directory,
      PatchFileName = "Output.esp"
    };
    var patchSettings = new PatcherSettings();
    var mutagen = new MutagenService(_logging, patchSettings, gui);
    var settings = new SettingsViewModel(patchSettings, gui, _logging, null!,
      new ThemeService(_logging.Logger), new LocalizationService(_logging.Logger, gui), mutagen);
    var lifecycle = new ConnectedDraftLifecycle(new PatchingService(mutagen, _logging, new EmptyEnvironment()));
    lifecycle.SetOutputPath(settings.FullOutputPath);
    settings.PrepareOutputChange = lifecycle.SetOutputPath;
    return (settings, gui, lifecycle);
  }

  public void Dispose()
  {
    _logging.Dispose();
    Directory.Delete(_directory, true);
  }

  private sealed class EmptyEnvironment : IConnectedDraftEnvironment
  {
    public SkyrimRelease Release => SkyrimRelease.SkyrimSE;
    public IReadOnlyList<ISkyrimModGetter> LoadedPlugins => [];
    public void ReleaseHandles() { }
    public Task RefreshAsync(string outputPath) => Task.CompletedTask;
  }
}
