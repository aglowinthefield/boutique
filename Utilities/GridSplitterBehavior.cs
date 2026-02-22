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

    var settingsService = GetSettingsService();

    var savedRatio = settingsService?.GetSplitterPosition(key);
    if (savedRatio is null or <= 0)
    {
      return;
    }

    if (splitter.Parent is not Grid parent)
    {
      return;
    }

    var isHorizontal = splitter.ResizeDirection == GridResizeDirection.Rows ||
                       (splitter.ResizeDirection == GridResizeDirection.Auto && splitter.Height > splitter.Width);

    if (isHorizontal)
    {
      var splitterRow = Grid.GetRow(splitter);
      var beforeRow   = splitterRow - 1;
      var afterRow    = splitterRow + 1;

      if (beforeRow >= 0 && afterRow < parent.RowDefinitions.Count)
      {
        var afterDef        = parent.RowDefinitions[afterRow];
        var afterStarValue  = afterDef.Height.IsStar ? afterDef.Height.Value : 1.0;
        var beforeStarValue = savedRatio.Value * afterStarValue;
        parent.RowDefinitions[beforeRow].Height = new GridLength(beforeStarValue, GridUnitType.Star);
      }
    }
    else
    {
      var splitterColumn = Grid.GetColumn(splitter);
      var beforeColumn   = splitterColumn - 1;
      var afterColumn    = splitterColumn + 1;

      if (beforeColumn >= 0 && afterColumn < parent.ColumnDefinitions.Count)
      {
        var afterDef        = parent.ColumnDefinitions[afterColumn];
        var afterStarValue  = afterDef.Width.IsStar ? afterDef.Width.Value : 1.0;
        var beforeStarValue = savedRatio.Value * afterStarValue;
        parent.ColumnDefinitions[beforeColumn].Width = new GridLength(beforeStarValue, GridUnitType.Star);
      }
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

    var isHorizontal = splitter.ResizeDirection == GridResizeDirection.Rows ||
                       (splitter.ResizeDirection == GridResizeDirection.Auto && splitter.Height > splitter.Width);

    if (isHorizontal)
    {
      var splitterRow = Grid.GetRow(splitter);
      var beforeRow   = splitterRow - 1;
      var afterRow    = splitterRow + 1;

      if (beforeRow >= 0 && afterRow < parent.RowDefinitions.Count)
      {
        var beforeHeight = parent.RowDefinitions[beforeRow].ActualHeight;
        var afterHeight  = parent.RowDefinitions[afterRow].ActualHeight;

        if (afterHeight > 0)
        {
          var ratio = beforeHeight / afterHeight;
          settingsService.SetSplitterPosition(key, ratio);
        }
      }
    }
    else
    {
      var splitterColumn = Grid.GetColumn(splitter);
      var beforeColumn   = splitterColumn - 1;
      var afterColumn    = splitterColumn + 1;

      if (beforeColumn >= 0 && afterColumn < parent.ColumnDefinitions.Count)
      {
        var beforeWidth = parent.ColumnDefinitions[beforeColumn].ActualWidth;
        var afterWidth  = parent.ColumnDefinitions[afterColumn].ActualWidth;

        if (afterWidth > 0)
        {
          var ratio = beforeWidth / afterWidth;
          settingsService.SetSplitterPosition(key, ratio);
        }
      }
    }
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
