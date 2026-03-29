using System.Windows;

namespace Boutique.Services;

public sealed class WpfDialogService : IDialogService
{
  public void ShowInfo(string message, string title) =>
    MessageBox.Show(
      Application.Current.MainWindow!,
      message,
      title,
      MessageBoxButton.OK,
      MessageBoxImage.Information);

  public void ShowError(string message, string title) =>
    MessageBox.Show(
      Application.Current.MainWindow!,
      message,
      title,
      MessageBoxButton.OK,
      MessageBoxImage.Error);

  public bool Confirm(string message, string title) =>
    MessageBox.Show(
      Application.Current.MainWindow!,
      message,
      title,
      MessageBoxButton.YesNo,
      MessageBoxImage.Warning,
      MessageBoxResult.No) == MessageBoxResult.Yes;

  public bool? ConfirmWithCancel(string message, string title)
  {
    var result = MessageBox.Show(
      Application.Current.MainWindow!,
      message,
      title,
      MessageBoxButton.YesNoCancel,
      MessageBoxImage.Warning);

    return result switch
    {
      MessageBoxResult.Yes => true,
      MessageBoxResult.No => false,
      _ => null
    };
  }
}
