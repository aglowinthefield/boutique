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
    private IDisposable? _highlightSubscription;
    private ScrollViewer? _textBoxScrollViewer;

    public DistributionFilePreviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PreviewTextBox.Loaded += (_, _) =>
        {
            _textBoxScrollViewer = FindChild<ScrollViewer>(PreviewTextBox);
        };
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

    private void PreviewTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        LineNumbersScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void ScrollToLineAndHighlight(int lineNumber)
    {
        if (PreviewTextBox.Text is not { Length: > 0 } text)
        {
            return;
        }

        var lines = text.Split('\n');
        if (lineNumber < 0 || lineNumber >= lines.Length)
        {
            return;
        }

        var charIndex = 0;
        for (var i = 0; i < lineNumber; i++)
        {
            charIndex += lines[i].Length + 1;
        }

        PreviewTextBox.CaretIndex = charIndex;

        var lineHeight = PreviewTextBox.FontSize * 1.2;
        var targetLineTop = lineNumber * lineHeight;

        if (_textBoxScrollViewer != null)
        {
            var viewportCenter = _textBoxScrollViewer.ViewportHeight / 2;
            var targetOffset = targetLineTop - viewportCenter + (lineHeight / 2);
            targetOffset = Math.Max(0, Math.Min(targetOffset, _textBoxScrollViewer.ScrollableHeight));
            _textBoxScrollViewer.ScrollToVerticalOffset(targetOffset);
        }

        Dispatcher.BeginInvoke(() =>
        {
            var rect = PreviewTextBox.GetRectFromCharacterIndex(charIndex);
            if (double.IsFinite(rect.Top) && rect.Top >= 0)
            {
                HighlightOverlay.Margin = new Thickness(0, rect.Top, 0, 0);
                HighlightOverlay.Height = rect.Height > 0 ? rect.Height : 16;

                HighlightOverlay.BeginAnimation(OpacityProperty, null);

                var animation = new DoubleAnimation
                {
                    From = 0.4,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(800),
                    BeginTime = TimeSpan.FromMilliseconds(100)
                };
                HighlightOverlay.BeginAnimation(OpacityProperty, animation);
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
