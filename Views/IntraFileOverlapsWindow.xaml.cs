using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Boutique.Models;
using Boutique.Services;

namespace Boutique.Views;

public partial class IntraFileOverlapsWindow : Window
{
  public IntraFileOverlapsWindow(IntraFileConflictResult result)
  {
    InitializeComponent();

    if (ThemeService.Current is { } themeService)
    {
      RootScaleTransform.ScaleX = themeService.CurrentFontScale;
      RootScaleTransform.ScaleY = themeService.CurrentFontScale;

      SourceInitialized += (_, _) => themeService.ApplyTitleBarTheme(this);
    }

    HeaderText.Text = $"{result.TotalOverlappingNpcCount} NPC(s) targeted by multiple outfits";
    BuildMatrix(result);
  }

  private void BuildMatrix(IntraFileConflictResult result)
  {
    var names = result.OutfitNames;
    var n = names.Count;

    if (n < 2)
    {
      return;
    }

    for (var i = 0; i <= n; i++)
    {
      MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    }

    for (var i = 0; i <= n; i++)
    {
      MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    var headerBrush    = FindResource("Brush.TextSecondary") as Brush ?? Brushes.Gray;
    var accentBrush    = FindResource("Brush.Accent") as Brush ?? Brushes.DodgerBlue;
    var borderBrush    = FindResource("Brush.Border") as Brush ?? Brushes.LightGray;
    var highlightBrush = FindResource("Brush.HighlightOverlay") as Brush ?? Brushes.Transparent;

    for (var col = 0; col < n; col++)
    {
      var header = new TextBlock
      {
        Text = names[col],
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = headerBrush,
        Padding = new Thickness(6, 4, 6, 4),
        ToolTip = names[col]
      };
      Grid.SetRow(header, 0);
      Grid.SetColumn(header, col + 1);
      MatrixGrid.Children.Add(header);
    }

    for (var row = 0; row < n; row++)
    {
      var header = new TextBlock
      {
        Text = names[row],
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = headerBrush,
        Padding = new Thickness(6, 4, 6, 4),
        ToolTip = names[row],
        VerticalAlignment = VerticalAlignment.Center
      };
      Grid.SetRow(header, row + 1);
      Grid.SetColumn(header, 0);
      MatrixGrid.Children.Add(header);
    }

    for (var row = 0; row < n; row++)
    {
      for (var col = 0; col < n; col++)
      {
        var cellBorder = new Border
        {
          BorderBrush = borderBrush,
          BorderThickness = new Thickness(0.5),
          Padding = new Thickness(6, 4, 6, 4),
          MinWidth = 40,
          HorizontalAlignment = HorizontalAlignment.Stretch
        };

        string text;
        Brush foreground;

        if (row == col)
        {
          text = "\u2014";
          foreground = headerBrush;
        }
        else
        {
          var key = string.Compare(names[row], names[col], StringComparison.Ordinal) < 0
                      ? (names[row], names[col])
                      : (names[col], names[row]);

          if (result.PairwiseOverlapCounts.TryGetValue(key, out var count) && count > 0)
          {
            text = count.ToString();
            foreground = accentBrush;
            cellBorder.Background = highlightBrush;
          }
          else
          {
            text = "\u2014";
            foreground = headerBrush;
          }
        }

        var textBlock = new TextBlock
        {
          Text = text,
          FontSize = 12,
          Foreground = foreground,
          HorizontalAlignment = HorizontalAlignment.Center,
          VerticalAlignment = VerticalAlignment.Center
        };

        cellBorder.Child = textBlock;
        Grid.SetRow(cellBorder, row + 1);
        Grid.SetColumn(cellBorder, col + 1);
        MatrixGrid.Children.Add(cellBorder);
      }
    }
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
