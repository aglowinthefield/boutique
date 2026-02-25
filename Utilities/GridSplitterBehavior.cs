using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Autofac;
using Boutique.Services;

namespace Boutique.Utilities;

public static class GridSplitterBehavior
{
  public static readonly DependencyProperty PersistKeyProperty = DependencyProperty.RegisterAttached(
    "PersistKey",
    typeof(string),
    typeof(GridSplitterBehavior),
    new PropertyMetadata(null, OnPersistKeyChanged));

  public static string? GetPersistKey(DependencyObject obj) => (string?)obj.GetValue(PersistKeyProperty);

  public static void SetPersistKey(DependencyObject obj, string? value) => obj.SetValue(PersistKeyProperty, value);

  private static void OnPersistKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
  {
    if (d is not GridSplitter splitter)
    {
      return;
    }

    splitter.Loaded        -= OnSplitterLoaded;
    splitter.DragCompleted -= OnSplitterDragCompleted;

    if (e.NewValue is not string key || string.IsNullOrEmpty(key))
    {
      return;
    }

    splitter.Loaded        += OnSplitterLoaded;
    splitter.DragCompleted += OnSplitterDragCompleted;
  }

  private static void OnSplitterLoaded(object sender, RoutedEventArgs e)
  {
    if (sender is not GridSplitter splitter)
    {
      return;
    }

    var key = GetPersistKey(splitter);
    if (string.IsNullOrEmpty(key))
    {
      return;
    }

    var savedRatio = GetSettingsService()?.GetSplitterPosition(key);
    if (savedRatio is null or <= 0)
    {
      return;
    }

    if (splitter.Parent is not Grid parent)
    {
      return;
    }

    var neighbors = ResolveNeighbors(splitter, parent);
    if (neighbors is not var (before, after, isHorizontal))
    {
      return;
    }

    if (isHorizontal)
    {
      var afterDef        = parent.RowDefinitions[after];
      var afterStarValue  = afterDef.Height.IsStar ? afterDef.Height.Value : 1.0;
      parent.RowDefinitions[before].Height = new GridLength(savedRatio.Value * afterStarValue, GridUnitType.Star);
    }
    else
    {
      var afterDef        = parent.ColumnDefinitions[after];
      var afterStarValue  = afterDef.Width.IsStar ? afterDef.Width.Value : 1.0;
      parent.ColumnDefinitions[before].Width = new GridLength(savedRatio.Value * afterStarValue, GridUnitType.Star);
    }
  }

  private static void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
  {
    if (sender is not GridSplitter splitter)
    {
      return;
    }

    var key = GetPersistKey(splitter);
    if (string.IsNullOrEmpty(key))
    {
      return;
    }

    var settingsService = GetSettingsService();
    if (settingsService == null)
    {
      return;
    }

    if (splitter.Parent is not Grid parent)
    {
      return;
    }

    var neighbors = ResolveNeighbors(splitter, parent);
    if (neighbors is not var (before, after, isHorizontal))
    {
      return;
    }

    double beforeSize, afterSize;
    if (isHorizontal)
    {
      beforeSize = parent.RowDefinitions[before].ActualHeight;
      afterSize  = parent.RowDefinitions[after].ActualHeight;
    }
    else
    {
      beforeSize = parent.ColumnDefinitions[before].ActualWidth;
      afterSize  = parent.ColumnDefinitions[after].ActualWidth;
    }

    if (afterSize > 0)
    {
      settingsService.SetSplitterPosition(key, beforeSize / afterSize);
    }
  }

  private static (int Before, int After, bool IsHorizontal)? ResolveNeighbors(
    GridSplitter splitter,
    Grid parent)
  {
    var isHorizontal = splitter.ResizeDirection == GridResizeDirection.Rows ||
                       (splitter.ResizeDirection == GridResizeDirection.Auto && splitter.Height > splitter.Width);

    var splitterIndex = isHorizontal ? Grid.GetRow(splitter) : Grid.GetColumn(splitter);
    var count         = isHorizontal ? parent.RowDefinitions.Count : parent.ColumnDefinitions.Count;
    var before        = splitterIndex - 1;
    var after         = splitterIndex + 1;

    return before >= 0 && after < count
             ? (before, after, isHorizontal)
             : null;
  }

  private static GuiSettingsService? GetSettingsService()
  {
    try
    {
      return ((App)Application.Current).Container?.Resolve<GuiSettingsService>();
    }
    catch
    {
      return null;
    }
  }
}
