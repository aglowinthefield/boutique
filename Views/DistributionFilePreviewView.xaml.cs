using System.Windows;
using System.Windows.Media.Animation;
using Boutique.ViewModels;
using ReactiveUI;

namespace Boutique.Views;

public partial class DistributionFilePreviewView
{
    private static readonly DoubleAnimation HighlightAnimation = new()
    {
        From = 0.35,
        To = 0,
        Duration = TimeSpan.FromMilliseconds(600),
        BeginTime = TimeSpan.FromMilliseconds(200)
    };

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
                .WhereNotNull()
                .Subscribe(r => ScrollToLineAndHighlight(r.LineNumber));
        }
    }

    private void ScrollToLineAndHighlight(int lineNumber)
    {
        if (PreviewTextBox?.Text is not { Length: > 0 } text)
            return;

        var lines = text.Split('\n');
        if ((uint)lineNumber >= (uint)lines.Length)
            return;

        var charIndex = lines.Take(lineNumber).Sum(l => l.Length + 1);
        var lineRect = PreviewTextBox.GetRectFromCharacterIndex(charIndex);

        if (lineRect.Top < 0 || lineRect.Top > PreviewTextBox.ActualHeight - 20)
        {
            PreviewTextBox.CaretIndex = charIndex;
            PreviewTextBox.ScrollToLine(lineNumber);
        }

        Dispatcher.BeginInvoke(() =>
        {
            var rect = PreviewTextBox.GetRectFromCharacterIndex(charIndex);
            if (double.IsFinite(rect.Top))
            {
                HighlightOverlay.Margin = new Thickness(0, rect.Top, 0, 0);
                HighlightOverlay.Height = rect.Height > 0 ? rect.Height : 16;
                HighlightOverlay.BeginAnimation(OpacityProperty, HighlightAnimation);
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
