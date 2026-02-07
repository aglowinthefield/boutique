using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Boutique.ViewModels;
using ReactiveUI;

namespace Boutique.Views;

public partial class DistributionFilePreviewView
{
  private IDisposable? _highlightSubscription;

  public DistributionFilePreviewView()
  {
    InitializeComponent();
    DataContextChanged += OnDataContextChanged;
  }

  private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
  {
    _highlightSubscription?.Dispose();

    if (e.NewValue is DistributionEditTabViewModel vm)
    {
      _highlightSubscription = vm
        .WhenAnyValue(x => x.HighlightRequest)
        .ObserveOn(RxApp.MainThreadScheduler)
        .Subscribe(r =>
        {
          if (r != null)
          {
            ScrollToLineAndHighlight(r.LineNumber);
          }
        });
    }
  }

  private void LineNumber_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (sender is FrameworkElement { DataContext: PreviewLine line } &&
        DataContext is DistributionEditTabViewModel vm)
    {
      vm.SelectEntryByLineNumber(line.LineNumber - 1);
    }
  }

  private void ScrollToLineAndHighlight(int lineNumber)
  {
    if (LinesControl.Items.Count == 0 || lineNumber < 0 || lineNumber >= LinesControl.Items.Count)
    {
      return;
    }

    Dispatcher.BeginInvoke(() =>
      {
        if (LinesControl.ItemContainerGenerator.ContainerFromIndex(lineNumber) is not FrameworkElement container)
        {
          return;
        }

        var transform = container.TransformToAncestor(LinesControl);
        var position = transform.Transform(new Point(0, 0));
        var lineTop = position.Y;
        var lineHeight = container.ActualHeight > 0 ? container.ActualHeight : 16;

        var viewportCenter = PreviewScrollViewer.ViewportHeight / 2;
        var targetOffset = lineTop - viewportCenter + lineHeight / 2;
        targetOffset = Math.Max(0, Math.Min(targetOffset, PreviewScrollViewer.ScrollableHeight));
        PreviewScrollViewer.ScrollToVerticalOffset(targetOffset);

        HighlightOverlay.Margin = new Thickness(0, lineTop, 0, 0);
        HighlightOverlay.Height = lineHeight;

        HighlightOverlay.BeginAnimation(OpacityProperty, null);

        var animation = new DoubleAnimation
        {
          From = 0.4, To = 0, Duration = TimeSpan.FromMilliseconds(800), BeginTime = TimeSpan.FromMilliseconds(100)
        };
        HighlightOverlay.BeginAnimation(OpacityProperty, animation);
      },
      DispatcherPriority.Loaded);
  }
}
