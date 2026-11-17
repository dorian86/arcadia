using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Arcadia.Controls;

/// <summary>
/// Keeps the hover zoom of a game card inside the visible area. A card is enlarged by 5 % around its
/// centre; when it sits against the edge of a scrolling area (a row half scrolled under the header, the
/// first or last cover of a shelf) the enlarged cover would be sliced by that edge. On mouse enter the zoom
/// origin is moved toward that edge so the card grows inward instead, and its outline stays whole.
/// </summary>
public static class HoverZoom
{
    private const double Scale = 1.05;
    private const double Margin = 4; // accent ring + selection outline drawn outside the cover

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(HoverZoom), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;
        control.MouseEnter -= OnMouseEnter;
        if ((bool)e.NewValue) control.MouseEnter += OnMouseEnter;
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Control card) Apply(card);
    }

    /// <summary>Sets the zoom origin of a card's cover for its current position.</summary>
    public static void Apply(Control card)
    {
        if (card.Template is not { } template) return;
        if (template.FindName("CoverHost", card) is not FrameworkElement host || host.Parent is not Visual parent) return;
        try
        {
            var origin = host.RenderTransformOrigin = ComputeOrigin(host, parent);
            BringIntoView(host, parent, origin);
        }
        catch (InvalidOperationException) { /* not in a visual tree yet */ }
    }

    /// <summary>
    /// A card already slightly hidden by the edge of its list (a row half scrolled under the header) cannot
    /// fit by moving the zoom origin alone: scroll the list by those few pixels so the whole card shows.
    /// </summary>
    private static void BringIntoView(FrameworkElement host, Visual parent, Point origin)
    {
        double w = host.ActualWidth, h = host.ActualHeight, k = Scale - 1;
        for (DependencyObject? d = VisualTreeHelper.GetParent(parent); d != null; d = VisualTreeHelper.GetParent(d))
        {
            // ScrollOwner is not set when a virtualizing panel does the scrolling: use the templated parent
            if (d is not ScrollContentPresenter { TemplatedParent: ScrollViewer sv } viewport) continue;
            if (SmoothScroll.IsScrolling(sv)) return; // the user is scrolling: never fight the wheel
            var r = parent.TransformToAncestor(viewport).TransformBounds(new Rect(0, 0, w, h));
            if (sv.ScrollableHeight > 0)
            {
                var needTop = k * origin.Y * h + Margin;
                var needBottom = k * (1 - origin.Y) * h + Margin;
                var delta = r.Top < needTop ? r.Top - needTop
                    : viewport.ActualHeight - r.Bottom < needBottom ? needBottom - (viewport.ActualHeight - r.Bottom) : 0;
                if (delta != 0 && Math.Abs(delta) <= h * 0.25) SmoothScroll.ScrollVerticalBy(sv, delta);
            }
            if (sv.ScrollableWidth > 0)
            {
                var needLeft = k * origin.X * w + Margin;
                var needRight = k * (1 - origin.X) * w + Margin;
                var delta = r.Left < needLeft ? r.Left - needLeft
                    : viewport.ActualWidth - r.Right < needRight ? needRight - (viewport.ActualWidth - r.Right) : 0;
                if (delta != 0 && Math.Abs(delta) <= w * 0.25) SmoothScroll.ScrollHorizontalBy(sv, delta);
            }
        }
    }

    /// <summary>Origin (0..1) that keeps the enlarged cover inside every clipping scroll area above it.</summary>
    private static Point ComputeOrigin(FrameworkElement host, Visual parent)
    {
        double w = host.ActualWidth, h = host.ActualHeight;
        if (w <= 0 || h <= 0) return new Point(0.5, 0.5);

        // Free space around the (unscaled) cover inside each clipping ancestor; keep the smallest
        double left = double.MaxValue, right = double.MaxValue, top = double.MaxValue, bottom = double.MaxValue;
        for (DependencyObject? d = VisualTreeHelper.GetParent(parent); d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is not ScrollContentPresenter viewport) continue;
            // the cover is the first child of the card template, at the top-left of its parent
            var r = parent.TransformToAncestor(viewport).TransformBounds(new Rect(0, 0, w, h));
            left = Math.Min(left, r.Left);
            top = Math.Min(top, r.Top);
            right = Math.Min(right, viewport.ActualWidth - r.Right);
            bottom = Math.Min(bottom, viewport.ActualHeight - r.Bottom);
        }
        return new Point(Axis(w, left, right), Axis(h, top, bottom));
    }

    /// <summary>
    /// Scaling around origin p (pixels from the start) grows the start side by (Scale-1)·p and the end side by
    /// (Scale-1)·(size-p). Pick p as close to the centre as the free space on both sides allows.
    /// </summary>
    internal static double Axis(double size, double before, double after)
    {
        var k = Scale - 1;
        var max = (before - Margin) / k;          // largest p that still fits before the cover
        var min = size - (after - Margin) / k;    // smallest p that still fits after it
        var p = size / 2;
        if (p > max) p = max;
        if (p < min) p = min;                     // no room on either side: favour the end side
        return Math.Clamp(p / size, 0, 1);
    }
}
