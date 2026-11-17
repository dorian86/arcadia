using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Arcadia.Controls;

/// <summary>
/// Lays its child out in a fixed "design" space (1600×900 by default) scaled to the window, like a console
/// screen: everything keeps its proportions at any window size, while the extra width or height of a
/// non-16:9 window is still given to the child (bottom bars stay at the bottom, backgrounds fill the window).
/// </summary>
public class DesignScaler : Decorator
{
    public double DesignWidth { get; set; } = 1600;
    public double DesignHeight { get; set; } = 900;

    private double ScaleFor(Size size)
    {
        if (double.IsInfinity(size.Width) || double.IsInfinity(size.Height) || size.Width <= 0 || size.Height <= 0) return 1;
        return Math.Min(size.Width / DesignWidth, size.Height / DesignHeight);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var s = ScaleFor(constraint);
        Child?.Measure(new Size(constraint.Width / s, constraint.Height / s));
        return double.IsInfinity(constraint.Width) || double.IsInfinity(constraint.Height) ? new Size(DesignWidth, DesignHeight) : constraint;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child == null) return arrangeSize;
        var s = ScaleFor(arrangeSize);
        Child.RenderTransform = new ScaleTransform(s, s);
        Child.Arrange(new Rect(0, 0, arrangeSize.Width / s, arrangeSize.Height / s));
        return arrangeSize;
    }
}
