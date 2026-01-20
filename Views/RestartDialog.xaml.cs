using System.Windows;
using Boutique.Services;

namespace Boutique.Views;

public partial class RestartDialog : Window
{
    public bool QuitNow { get; private set; }

    public RestartDialog()
    {
        InitializeComponent();
        if (ThemeService.Current is { } themeService)
        {
            RootScaleTransform.ScaleX = themeService.CurrentFontScale;
            RootScaleTransform.ScaleY = themeService.CurrentFontScale;
        }
    }

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
