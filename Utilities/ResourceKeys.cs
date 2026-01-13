namespace Boutique.Utilities;

/// <summary>
/// Centralized resource key constants for XAML resources.
/// Using these constants prevents typos and enables compile-time checking.
/// Usage in XAML: {DynamicResource {x:Static util:ResourceKeys.BrushCardBackground}}
/// Usage in C#: Application.Current.Resources[ResourceKeys.BrushCardBackground]
/// </summary>
public static class ResourceKeys
{
    // Backgrounds
    public const string BrushPrimaryBackground = "Brush.PrimaryBackground";
    public const string BrushPanelBackground = "Brush.PanelBackground";
    public const string BrushCardBackground = "Brush.CardBackground";

    // Borders
    public const string BrushBorder = "Brush.Border";
    public const string BrushBorderLight = "Brush.BorderLight";

    // Accents
    public const string BrushAccent = "Brush.Accent";
    public const string BrushAccentMuted = "Brush.AccentMuted";
    public const string BrushWarning = "Brush.Warning";
    public const string BrushHighlightOverlay = "Brush.HighlightOverlay";

    // Text
    public const string BrushTextPrimary = "Brush.TextPrimary";
    public const string BrushTextSecondary = "Brush.TextSecondary";
    public const string BrushTextDisabled = "Brush.TextDisabled";

    // Error Panel
    public const string BrushErrorBackground = "Brush.Error.Background";
    public const string BrushErrorBorder = "Brush.Error.Border";
    public const string BrushErrorTitle = "Brush.Error.Title";
    public const string BrushErrorText = "Brush.Error.Text";

    // Tutorial
    public const string BrushTutorialOverlay = "Brush.Tutorial.Overlay";
    public const string BrushTutorialDialogBackground = "Brush.Tutorial.DialogBackground";
    public const string BrushTutorialDialogBorder = "Brush.Tutorial.DialogBorder";
    public const string BrushTutorialDialogForeground = "Brush.Tutorial.DialogForeground";
    public const string BrushTutorialButtonBackground = "Brush.Tutorial.ButtonBackground";
    public const string BrushTutorialButtonForeground = "Brush.Tutorial.ButtonForeground";
}
