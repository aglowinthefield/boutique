namespace Boutique.Services;

public interface IDialogService
{
  void ShowInfo(string message, string title);
  void ShowError(string message, string title);
  bool Confirm(string message, string title);
  bool? ConfirmWithCancel(string message, string title);
}
