using System.Windows;
using Boutique.Services;

namespace Boutique.Views;

public partial class RestartDialog
{
  public RestartDialog()
  {
    InitializeComponent();
    if (ThemeService.Current is { } themeService)
    {
      RootScaleTransform.ScaleX = themeService.CurrentFontScale;
      RootScaleTransform.ScaleY = themeService.CurrentFontScale;

      SourceInitialized += (_, _) => themeService.ApplyTitleBarTheme(this);
    }
  }

  public bool QuitNow { get; private set; }

  private void QuitButton_Click(object sender, RoutedEventArgs e)
  {
    QuitNow = true;
    Close();
  }

  private void LaterButton_Click(object sender, RoutedEventArgs e)
  {
    QuitNow = false;
    Close();
  }
}
