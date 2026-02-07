using System.Windows;

namespace Boutique.Views;

public partial class UpdateDialog : Window
{
  public UpdateDialog()
  {
    InitializeComponent();
  }

  public string CurrentVersion { get; set; } = string.Empty;
  public string LatestVersion { get; set; } = string.Empty;
  public string ReleaseNotes { get; set; } = string.Empty;
  public string DownloadUrl { get; set; } = string.Empty;

  public UpdateResult Result { get; private set; } = UpdateResult.Later;

  private void Update_Click(object sender, RoutedEventArgs e)
  {
    Result = UpdateResult.Update;
    DialogResult = true;
    Close();
  }

  private void Skip_Click(object sender, RoutedEventArgs e)
  {
    Result = UpdateResult.Skip;
    DialogResult = false;
    Close();
  }

  private void Later_Click(object sender, RoutedEventArgs e)
  {
    Result = UpdateResult.Later;
    DialogResult = false;
    Close();
  }
}

public enum UpdateResult
{
  Update,
  Skip,
  Later
}
