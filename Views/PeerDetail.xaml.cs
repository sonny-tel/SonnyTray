using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SonnyTray.ViewModels;

namespace SonnyTray.Views;

public partial class PeerDetail : UserControl
{
    public PeerDetail()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PeerDetailViewModel oldVm)
        {
            oldVm.PingHistory.CollectionChanged -= OnPingHistoryChanged;
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is PeerDetailViewModel newVm)
        {
            newVm.PingHistory.CollectionChanged += OnPingHistoryChanged;
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private bool _hasScrolledForPing;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PeerDetailViewModel.IsPinging)
            && sender is PeerDetailViewModel { IsPinging: true })
        {
            _hasScrolledForPing = false;
        }

        if (e.PropertyName == nameof(PeerDetailViewModel.PingCount)
            && !_hasScrolledForPing
            && sender is PeerDetailViewModel { PingCount: > 0 })
        {
            _hasScrolledForPing = true;
            Dispatcher.InvokeAsync(() => SmoothScrollToElement(PingSection),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void SmoothScrollToElement(FrameworkElement element)
    {
        // Walk up to find the hosting ScrollViewer
        var scrollViewer = FindParentScrollViewer(this);
        if (scrollViewer is null) return;

        // Get the element's position relative to the scroll content
        var transform = element.TransformToVisual(scrollViewer);
        var position = transform.Transform(new Point(0, 0));
        var targetOffset = scrollViewer.VerticalOffset + position.Y - 8;
        targetOffset = Math.Clamp(targetOffset, 0, scrollViewer.ScrollableHeight);

        AnimateScroll(scrollViewer, targetOffset);
    }

    private static void AnimateScroll(ScrollViewer viewer, double toOffset)
    {
        var from = viewer.VerticalOffset;
        var duration = TimeSpan.FromMilliseconds(300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var start = DateTime.UtcNow;

        CompositionTarget.Rendering -= Scroll;
        CompositionTarget.Rendering += Scroll;

        void Scroll(object? s, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
            var t = Math.Clamp(elapsed / duration.TotalMilliseconds, 0, 1);
            // Apply cubic ease-out
            var eased = 1 - Math.Pow(1 - t, 3);
            viewer.ScrollToVerticalOffset(from + (toOffset - from) * eased);
            if (t >= 1)
                CompositionTarget.Rendering -= Scroll;
        }
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is ScrollViewer sv) return sv;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void OnPingHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawGraph();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawGraph();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        // Find the MainViewModel via the window's DataContext and close peer detail
        if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            mainVm.ShowPeerDetail = false;
    }

    private void RedrawGraph()
    {
        PingCanvas.Children.Clear();
        if (DataContext is not PeerDetailViewModel vm) return;
        var points = vm.PingHistory;
        if (points.Count < 2) return;

        var w = PingCanvas.ActualWidth;
        var h = PingCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        var validPoints = points.Where(p => !p.IsError && p.LatencyMs >= 0).ToList();
        if (validPoints.Count < 1) return;

        var maxMs = validPoints.Max(p => p.LatencyMs);
        var minMs = validPoints.Min(p => p.LatencyMs);
        var range = maxMs - minMs;
        if (range < 1) range = 1;

        // Leave some padding
        var padTop = 8.0;
        var padBot = 4.0;
        var graphH = h - padTop - padBot;

        var accentBrush = (Brush)FindResource("AccentBrush");
        var errorBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0xA4));
        var relayBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0xE1, 0x00));

        // Draw line connecting valid points
        var polyline = new Polyline
        {
            Stroke = accentBrush,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
        };

        var step = w / Math.Max(points.Count - 1, 1);

        for (int i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            var x = i * step;

            if (pt.IsError)
            {
                // Red dot for errors
                var dot = new Ellipse { Width = 4, Height = 4, Fill = errorBrush };
                Canvas.SetLeft(dot, x - 2);
                Canvas.SetTop(dot, h / 2 - 2);
                PingCanvas.Children.Add(dot);
                continue;
            }

            var y = padTop + graphH - ((pt.LatencyMs - minMs) / range * graphH);
            polyline.Points.Add(new Point(x, y));

            // Color-coded dots: accent for direct, yellow for relay
            var dotBrush = pt.IsDirect ? accentBrush : relayBrush;
            var d = new Ellipse { Width = 4, Height = 4, Fill = dotBrush };
            Canvas.SetLeft(d, x - 2);
            Canvas.SetTop(d, y - 2);
            PingCanvas.Children.Add(d);
        }

        PingCanvas.Children.Insert(0, polyline);

        // Labels: min & max on right side
        var maxLabel = new TextBlock
        {
            Text = $"{maxMs:F0}ms",
            FontSize = 9,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
        };
        Canvas.SetRight(maxLabel, 2);
        Canvas.SetTop(maxLabel, padTop);
        PingCanvas.Children.Add(maxLabel);

        var minLabel = new TextBlock
        {
            Text = $"{minMs:F0}ms",
            FontSize = 9,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
        };
        Canvas.SetRight(minLabel, 2);
        Canvas.SetBottom(minLabel, padBot);
        PingCanvas.Children.Add(minLabel);
    }
}
