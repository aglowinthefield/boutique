namespace Boutique;

/// <summary>
///   Build-time feature flags. Configure in Boutique.csproj:
///   EnableTutorial: Set to true to enable the tutorial system.
/// </summary>
public static class FeatureFlags
{
#if FEATURE_TUTORIAL
    public const bool TutorialEnabled = true;
#else
  public const bool TutorialEnabled = false;
#endif
}
