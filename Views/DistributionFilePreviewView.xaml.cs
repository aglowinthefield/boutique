using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Boutique.ViewModels;
using ReactiveUI;

namespace Boutique.Views;

public partial class DistributionFilePreviewView
{
    private const double EstimatedLineHeight = 16.0;
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

    private void ScrollToLineAndHighlight(int lineNumber)
    {
        if (DataContext is not DistributionEditTabViewModel vm)
        {
            return;
        }

        var lines = vm.PreviewLines;
        if (lineNumber < 0 || lineNumber >= lines.Count)
        {
            return;
        }

        var targetOffset = lineNumber * EstimatedLineHeight - (PreviewScrollViewer.ViewportHeight / 2) +
                           (EstimatedLineHeight / 2);
        targetOffset = Math.Max(0, Math.Min(targetOffset, PreviewScrollViewer.ScrollableHeight));
        PreviewScrollViewer.ScrollToVerticalOffset(targetOffset);

        Dispatcher.BeginInvoke(() =>
        {
            LinesItemsControl.UpdateLayout();

            if (LinesItemsControl.ItemContainerGenerator.ContainerFromIndex(lineNumber) is FrameworkElement container)
            {
                var highlightBorder = FindChild<Border>(container, "HighlightBorder");
                if (highlightBorder != null)
                {
                    highlightBorder.BeginAnimation(OpacityProperty, null);

                    var animation = new DoubleAnimation
                    {
                        From = 0.4,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(800),
                        BeginTime = TimeSpan.FromMilliseconds(100)
                    };
                    highlightBorder.BeginAnimation(OpacityProperty, animation);
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static T? FindChild<T>(DependencyObject parent, string childName)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && child is FrameworkElement fe && fe.Name == childName)
            {
                return typedChild;
            }

            var result = FindChild<T>(child, childName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
