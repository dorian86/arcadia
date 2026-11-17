using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Arcadia.Controls;

/// <summary>Inertial, frame-rate independent mouse-wheel scrolling for ScrollViewers.</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(ScrollState), typeof(SmoothScroll), new PropertyMetadata(null));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        sv.PreviewMouseWheel -= OnWheel;
        if ((bool)e.NewValue) sv.PreviewMouseWheel += OnWheel;
    }

    private static ScrollState GetState(ScrollViewer sv)
    {
        if (sv.GetValue(StateProperty) is not ScrollState s)
        {
            s = new ScrollState(sv);
            sv.SetValue(StateProperty, s);
        }
        return s;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv || e.Handled) return;
        if (Keyboard.Modifiers == ModifierKeys.Control) return;
        e.Handled = true;
        var state = GetState(sv);
        if (Keyboard.Modifiers == ModifierKeys.Shift || sv.ScrollableHeight <= 0 && sv.ScrollableWidth > 0)
            state.ScrollHorizontalBy(-e.Delta * 1.4);
        else
            state.ScrollVerticalBy(-e.Delta * 1.25);
    }

    /// <summary>Animates a horizontal scroll (used by the shelf arrows on the home page).</summary>
    public static void ScrollHorizontalBy(ScrollViewer sv, double delta) => GetState(sv).ScrollHorizontalBy(delta);

    public static void ScrollVerticalBy(ScrollViewer sv, double delta) => GetState(sv).ScrollVerticalBy(delta);

    /// <summary>True while a wheel / arrow scroll animation is running on this viewer.</summary>
    public static bool IsScrolling(ScrollViewer sv) => GetState(sv).IsRunning;

    public static void ScrollToTop(ScrollViewer sv)
    {
        GetState(sv).Stop();
        sv.ScrollToVerticalOffset(0);
    }

    private sealed class ScrollState
    {
        private readonly ScrollViewer _sv;
        private double _targetV = double.NaN, _targetH = double.NaN;
        private bool _running;
        private TimeSpan _lastTime;

        public ScrollState(ScrollViewer sv) => _sv = sv;

        public bool IsRunning => _running;

        public void ScrollVerticalBy(double delta)
        {
            var from = _running && !double.IsNaN(_targetV) ? _targetV : _sv.VerticalOffset;
            _targetV = Math.Clamp(from + delta, 0, _sv.ScrollableHeight);
            Start();
        }

        public void ScrollHorizontalBy(double delta)
        {
            var from = _running && !double.IsNaN(_targetH) ? _targetH : _sv.HorizontalOffset;
            _targetH = Math.Clamp(from + delta, 0, _sv.ScrollableWidth);
            Start();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _targetV = _targetH = double.NaN;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void Start()
        {
            if (_running) return;
            _running = true;
            _lastTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.Zero;
            var dt = _lastTime == TimeSpan.Zero ? 1.0 / 60 : Math.Clamp((now - _lastTime).TotalSeconds, 0.001, 0.05);
            if (now == _lastTime && _lastTime != TimeSpan.Zero) return;
            _lastTime = now;
            var k = 1 - Math.Exp(-dt * 14);
            var done = true;

            if (!double.IsNaN(_targetV))
            {
                var cur = _sv.VerticalOffset;
                var next = cur + (_targetV - cur) * k;
                if (Math.Abs(_targetV - next) < 0.5) { next = _targetV; _targetV = double.NaN; }
                else done = false;
                _sv.ScrollToVerticalOffset(next);
            }

            if (!double.IsNaN(_targetH))
            {
                var cur = _sv.HorizontalOffset;
                var next = cur + (_targetH - cur) * k;
                if (Math.Abs(_targetH - next) < 0.5) { next = _targetH; _targetH = double.NaN; }
                else done = false;
                _sv.ScrollToHorizontalOffset(next);
            }

            if (done) Stop();
        }
    }
}
