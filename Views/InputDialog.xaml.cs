using System.Windows;
using System.Windows.Input;
using Boutique.Services;

namespace Boutique.Views;

public partial class InputDialog : Window
{
  public InputDialog(string prompt, string title, string defaultValue = "")
  {
    InitializeComponent();

    Title             = title;
    PromptText.Text   = prompt;
    InputTextBox.Text = defaultValue;

    if (ThemeService.Current is { } themeService)
    {
      RootScaleTransform.ScaleX = themeService.CurrentFontScale;
      RootScaleTransform.ScaleY = themeService.CurrentFontScale;

      SourceInitialized += (_, _) => themeService.ApplyTitleBarTheme(this);
    }

    Loaded += (_, _) =>
    {
      InputTextBox.Focus();
      InputTextBox.SelectAll();
    };
  }

  public string? Result { get; private set; }

  public static string? Show(Window? owner, string prompt, string title, string defaultValue = "")
  {
    var dialog = new InputDialog(prompt, title, defaultValue);
    if (owner != null)
    {
      dialog.Owner = owner;
    }

    dialog.ShowDialog();
    return dialog.Result;
  }

  private void OkButton_Click(object sender, RoutedEventArgs e)
  {
    Result = InputTextBox.Text;
    Close();
  }

  private void CancelButton_Click(object sender, RoutedEventArgs e)
  {
    Result = null;
    Close();
  }

  private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key == Key.Enter)
    {
      Result = InputTextBox.Text;
      Close();
      e.Handled = true;
    }
  }
}
