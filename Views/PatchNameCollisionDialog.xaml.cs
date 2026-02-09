using System.Globalization;
using System.Windows;
using Boutique.Resources;
using Boutique.Services;

namespace Boutique.Views;

public partial class PatchNameCollisionDialog
{
  public PatchNameCollisionDialog(string newFileName)
  {
    InitializeComponent();
    if (ThemeService.Current is { } themeService)
    {
      RootScaleTransform.ScaleX = themeService.CurrentFontScale;
      RootScaleTransform.ScaleY = themeService.CurrentFontScale;

      SourceInitialized += (_, _) => themeService.ApplyTitleBarTheme(this);
    }

    var messageTemplate = Strings.ResourceManager.GetString(
      "PatchNameCollision_Message",
      Strings.Culture) ?? "\"{0}\" already exists in your load order. Using this name may overwrite existing data.";
    MessageTextBlock.Text = string.Format(CultureInfo.CurrentCulture, messageTemplate, newFileName);
  }

  public bool ShouldRevert { get; private set; }

  private void RevertButton_Click(object sender, RoutedEventArgs e)
  {
    ShouldRevert = true;
    Close();
  }

  private void KeepButton_Click(object sender, RoutedEventArgs e)
  {
    ShouldRevert = false;
    Close();
  }
}
