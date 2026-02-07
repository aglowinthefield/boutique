using System.IO;
using Boutique.Utilities;
using GuideLine.Core;
using GuideLine.Core.Elements;
using GuideLine.WPF.View;
using Serilog;

namespace Boutique.Services;

public class TutorialService
{
  private static readonly string SettingsDirectory = PathUtilities.GetBoutiqueAppDataPath();
  private static readonly string TutorialCompletedFile = Path.Combine(SettingsDirectory, ".tutorial_completed");

  private readonly ILogger _logger;

  private GuideLineManager? _currentManager;
  private GuideLine_View? _guidelineView;

  public TutorialService(ILogger logger)
  {
    _logger = logger.ForContext<TutorialService>();
  }

  public bool HasCompletedTutorial
  {
    get => File.Exists(TutorialCompletedFile);
    private set
    {
      try
      {
        Directory.CreateDirectory(SettingsDirectory);
        if (value)
        {
          File.WriteAllText(TutorialCompletedFile, DateTime.UtcNow.ToString("O"));
        }
        else if (File.Exists(TutorialCompletedFile))
        {
          File.Delete(TutorialCompletedFile);
        }
      }
      catch (Exception ex)
      {
        _logger.Warning(ex, "Failed to persist tutorial completion state");
      }
    }
  }

  public void Initialize(GuideLine_View guidelineView) => _guidelineView = guidelineView;

  public void StartTutorial()
  {
    if (_guidelineView == null)
    {
      _logger.Warning("Cannot start tutorial: GuideLine_View not initialized");
      return;
    }

    _logger.Information("Starting application tutorial");

    var manager = new GuideLineManager();
    manager.OnGuideLineListCompleted += OnTutorialCompleted;
    manager.AddGuideLine(CreateMainTutorial());
    _guidelineView.DataContext = manager;
    _currentManager = manager;
    manager.StartGuideLine(_guidelineView.Name);
  }

  private void OnTutorialCompleted()
  {
    CompleteTutorial();
    if (_currentManager is null)
    {
      return;
    }

    _currentManager.OnGuideLineListCompleted -= OnTutorialCompleted;
    _currentManager = null;
  }

  public void CompleteTutorial()
  {
    HasCompletedTutorial = true;
    _logger.Information("Tutorial marked as completed");
  }

  public void ResetTutorial()
  {
    HasCompletedTutorial = false;
    _logger.Information("Tutorial progress reset");
  }

  private static GuideLineItem CreateMainTutorial() =>
    new(
    [
      new GuideLineStep(
        "Welcome to Boutique!",
        "This quick tour will show you the main features. Use the arrow buttons or keyboard arrows to navigate.",
        "MainTabControl"),

      new GuideLineStep(
        "Distribution Tab",
        "Create and manage outfit distributions for NPCs using SPID or SkyPatcher. This is the main workflow for assigning outfits to NPCs.",
        "DistributionTab"),

      new GuideLineStep(
        "Outfit Creator Tab",
        "Create new outfit records (OTFT) from armor pieces. Useful when you need custom outfit combinations.",
        "OutfitCreatorTab"),

      new GuideLineStep(
        "Armor Patch Tab",
        "Sync armor stats, keywords, and enchantments from master mods (like Requiem) to cosmetic armor mods.",
        "ArmorPatchTab"),

      new GuideLineStep(
        "Settings Tab",
        "Configure your Skyrim Data path, output location, and application preferences.",
        "SettingsTab"),

      new GuideLineStep(
        "Refresh Button",
        "Click here to reload game data after making changes to your load order or mod files.",
        "RefreshButton"),

      new GuideLineStep(
        "Patch File Name",
        "Set the name of the ESP file that Boutique will create. All changes are written to this file.",
        "PatchFileNamePanel"),

      new GuideLineStep(
        "You're Ready!",
        "That's it! Start by configuring your Skyrim Data path in Settings, then explore the Distribution tab to assign outfits to NPCs.\n\nYou can restart this tutorial anytime from the Help menu.",
        "MainTabControl")
    ]);
}
