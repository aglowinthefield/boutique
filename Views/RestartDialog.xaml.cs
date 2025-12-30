using System.Windows;

namespace Boutique.Views;

public partial class RestartDialog : Window
{
    public bool QuitNow { get; private set; }

    public RestartDialog() => InitializeComponent();

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
