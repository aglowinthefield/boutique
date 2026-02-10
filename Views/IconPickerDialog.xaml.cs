using System.Globalization;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Boutique.Services;
using Boutique.ViewModels;
using ReactiveUI;

namespace Boutique.Views;

public partial class IconPickerDialog
{
  private readonly IconPickerViewModel _viewModel;
  private          string?             _selectedIcon;
  private          bool                _wasCleared;

  public IconPickerDialog(string? currentIcon)
  {
    InitializeComponent();

    _viewModel    = new IconPickerViewModel(currentIcon);
    _selectedIcon = currentIcon;
    DataContext   = _viewModel;

    if (ThemeService.Current is { } themeService)
    {
      RootScaleTransform.ScaleX = themeService.CurrentFontScale;
      RootScaleTransform.ScaleY = themeService.CurrentFontScale;

      SourceInitialized += (_, _) => themeService.ApplyTitleBarTheme(this);
    }

    _viewModel.FilteredIcons
              .ObserveOn(RxApp.MainThreadScheduler)
              .Subscribe(icons =>
              {
                IconsItemsControl.ItemsSource = icons;
                UpdateSelection();
              });

    Loaded += (_, _) =>
    {
      SearchBox.Focus();
      UpdateSelection();
    };
  }

  public static (string? Icon, bool WasCleared) Show(Window? owner, string? currentIcon)
  {
    var dialog = new IconPickerDialog(currentIcon);
    if (owner != null)
    {
      dialog.Owner = owner;
    }

    dialog.ShowDialog();
    return (dialog._selectedIcon, dialog._wasCleared);
  }

  private void UpdateSelection()
  {
    foreach (var item in IconsItemsControl.Items)
    {
      if (item is not string iconName)
      {
        continue;
      }

      var container = IconsItemsControl.ItemContainerGenerator.ContainerFromItem(item);
      if (container is not ContentPresenter presenter)
      {
        continue;
      }

      var button = FindVisualChild<Button>(presenter);
      if (button == null)
      {
        continue;
      }

      button.BorderBrush = string.Equals(iconName, _selectedIcon, StringComparison.OrdinalIgnoreCase)
                             ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))
                             : Brushes.Transparent;
    }
  }

  private void IconButton_Click(object sender, RoutedEventArgs e)
  {
    if (sender is not Button { Tag: string iconName })
    {
      return;
    }

    _selectedIcon = iconName;
    UpdateSelection();
  }

  private void ClearButton_Click(object sender, RoutedEventArgs e)
  {
    _selectedIcon = null;
    _wasCleared   = true;
    Close();
  }

  private void CancelButton_Click(object sender, RoutedEventArgs e)
  {
    _selectedIcon = null;
    _wasCleared   = false;
    Close();
  }

  private void OkButton_Click(object sender, RoutedEventArgs e) => Close();

  private static T? FindVisualChild<T>(DependencyObject parent)
    where T : DependencyObject
  {
    var count = VisualTreeHelper.GetChildrenCount(parent);
    for (var i = 0; i < count; i++)
    {
      var child = VisualTreeHelper.GetChild(parent, i);
      if (child is T match)
      {
        return match;
      }

      var result = FindVisualChild<T>(child);
      if (result != null)
      {
        return result;
      }
    }

    return null;
  }
}

public class IconNameToUriConverter : IValueConverter
{
  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is string iconName ? IconPickerViewModel.GetIconUri(iconName) : null;

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}
