using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Arcadia.Controls;

/// <summary>Attached properties used by the control templates.</summary>
public static class Ui
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static string? GetGlyph(DependencyObject d) => (string?)d.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject d, string? v) => d.SetValue(GlyphProperty, v);

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static string? GetPlaceholder(DependencyObject d) => (string?)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string? v) => d.SetValue(PlaceholderProperty, v);

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached("IsActive", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetIsActive(DependencyObject d) => (bool)d.GetValue(IsActiveProperty);
    public static void SetIsActive(DependencyObject d, bool v) => d.SetValue(IsActiveProperty, v);

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(10)));
    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius v) => d.SetValue(CornerRadiusProperty, v);
}

/// <summary>A Border that clips its content to its rounded corners.</summary>
public class ClipBorder : Border
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        if (Child is UIElement child)
        {
            var r = CornerRadius.TopLeft;
            var size = child.RenderSize;
            var geometry = new RectangleGeometry(new Rect(0, 0, size.Width, size.Height), r, r);
            geometry.Freeze();
            child.Clip = geometry;
        }
        return result;
    }
}

/// <summary>Sizes its child with a fixed height/width ratio (2:3 game covers).</summary>
public class AspectRatioBox : Decorator
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio), typeof(double), typeof(AspectRatioBox),
        new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Height divided by width.</summary>
    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var w = double.IsInfinity(constraint.Width) ? 180 : constraint.Width;
        var size = new Size(w, w * Ratio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = new Size(arrangeSize.Width, arrangeSize.Width * Ratio);
        Child?.Arrange(new Rect(size));
        return size;
    }
}

/// <summary>Small indeterminate progress ring.</summary>
public class Spinner : Control
{
    private RotateTransform? _rotate;

    public Spinner()
    {
        // Only animate while visible: a hidden spinner must not keep the render loop busy.
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("Ring") is not UIElement ring) return;
        _rotate = new RotateTransform();
        ring.RenderTransformOrigin = new Point(0.5, 0.5);
        ring.RenderTransform = _rotate;
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (_rotate == null) return;
        if (IsVisible)
            _rotate.BeginAnimation(RotateTransform.AngleProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
                {
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                });
        else
            _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }
}

/// <summary>
/// Opens the game context menu on right-click. The menu is created only when needed
/// (instead of one menu with 10 items per card), which keeps the grid light.
/// </summary>
public static class GameMenu
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable", typeof(bool), typeof(GameMenu), new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);
    public static void SetEnable(DependencyObject d, bool v) => d.SetValue(EnableProperty, v);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        fe.MouseRightButtonUp -= OnRightClick;
        if ((bool)e.NewValue) fe.MouseRightButtonUp += OnRightClick;
    }

    private static void OnRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext == null) return;
        Open(fe, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
        e.Handled = true;
    }

    public static void Open(FrameworkElement target, System.Windows.Controls.Primitives.PlacementMode placement)
    {
        if (Application.Current.TryFindResource("GameContextMenu") is not ContextMenu menu) return;
        menu.DataContext = target.DataContext;
        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
    }
}

public static class VisualTreeUtil
{
    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            var found = FindDescendant<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
