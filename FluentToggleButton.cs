using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SonnyTray;

public class FluentToggleButton : ToggleButton
{
    private static readonly CubicEase _ease = new() { EasingMode = EasingMode.EaseOut };
    private static readonly Duration _duration = new(TimeSpan.FromMilliseconds(150));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Snap to correct position without animation (handles theme switch + initial load)
        if (GetTemplateChild("ThumbTranslate") is TranslateTransform t)
            t.X = IsChecked == true ? 20 : 0;
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        AnimateThumb(20);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        AnimateThumb(0);
    }

    private void AnimateThumb(double to)
    {
        if (GetTemplateChild("ThumbTranslate") is TranslateTransform t)
        {
            t.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(to, _duration) { EasingFunction = _ease });
        }
    }
}
