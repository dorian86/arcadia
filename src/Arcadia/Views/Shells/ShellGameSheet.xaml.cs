using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Arcadia.ViewModels;

namespace Arcadia.Views.Shells;

/// <summary>
/// The game page of the console themes (one layout per family: PlayStation, Switch, Wii). Shown over the
/// theme; the controller and keyboard are routed to it while it is open.
/// </summary>
public partial class ShellGameSheet : UserControl
{
    private GameSheetViewModel? _vm;

    public ShellGameSheet()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as GameSheetViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            StartTicker();
        };
        Loaded += (_, _) => StartTicker();
        PreviewMouseMove += OnMouseMove;
    }

    private Point _lastMouse = new(double.NaN, double.NaN);

    /// <summary>
    /// Hovering a button selects it. Only on a real move of the mouse: the page appearing under a still cursor
    /// must not steal the selection from the controller.
    /// </summary>
    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var moved = !double.IsNaN(_lastMouse.X) && (Math.Abs(p.X - _lastMouse.X) > 2 || Math.Abs(p.Y - _lastMouse.Y) > 2);
        _lastMouse = p;
        if (!moved || _vm == null) return;
        for (var d = e.OriginalSource as DependencyObject; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { DataContext: SheetAction action })
            {
                _vm.HoverAction(action);
                return;
            }
            if (d == this) return;
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameSheetViewModel.News)) StartTicker();
    }

    /// <summary>Wii family: the news scroll from right to left, like a news channel.</summary>
    private void StartTicker() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        var ticker = Find(this);
        if (ticker?.Parent is not Canvas canvas || canvas.ActualWidth <= 0) return;
        ticker.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = ticker.DesiredSize.Width;
        if (width <= 0) return;
        var speed = 90.0; // design pixels per second
        var anim = new DoubleAnimation(canvas.ActualWidth, -width, TimeSpan.FromSeconds((canvas.ActualWidth + width) / speed))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        ticker.BeginAnimation(Canvas.LeftProperty, anim);
    });

    private static FrameworkElement? Find(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Tag: "ticker" } fe) return fe;
            if (Find(child) is { } found) return found;
        }
        return null;
    }
}
