namespace Boutique;

/// <summary>
///     Build-time feature flags. Configure in Boutique.csproj:
///     EnableAutoUpdate: Set to true to enable auto-update checks.
///     EnableTutorial: Set to true to enable the tutorial system.
/// </summary>
public static class FeatureFlags
{
#if FEATURE_AUTO_UPDATE
    public const bool AutoUpdateEnabled = true;
#else
    public const bool AutoUpdateEnabled = false;
#endif

#if FEATURE_TUTORIAL
    public const bool TutorialEnabled = true;
#else
    public const bool TutorialEnabled = false;
#endif
}
