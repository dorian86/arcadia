using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Arcadia.Services;

/// <summary>Developer tool (--perf): scrolls the largest visible list for a few seconds and measures the frame rate.</summary>
public static class PerfProbe
{
    public static async Task<string> RunAsync(Window window, double seconds = 4)
    {
        var sv = FindScrollViewers(window)
            .Where(s => s.IsVisible && s.ScrollableHeight > 0)
            .OrderByDescending(s => s.ScrollableHeight)
            .FirstOrDefault();
        if (sv == null) return "PERF no scrollable list";

        var frames = 0;
        var worst = 0.0;
        var last = TimeSpan.Zero;
        var direction = 1.0;
        var tier = RenderCapability.Tier >> 16;
        var tcs = new TaskCompletionSource();
        var start = DateTime.UtcNow;

        void OnRendering(object? s, EventArgs e)
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            if (t == last) return;
            if (last != TimeSpan.Zero) worst = Math.Max(worst, (t - last).TotalMilliseconds);
            last = t;
            frames++;
            var next = sv.VerticalOffset + 22 * direction;
            if (next >= sv.ScrollableHeight || next <= 0) direction = -direction;
            sv.ScrollToVerticalOffset(Math.Clamp(next, 0, sv.ScrollableHeight));
            if ((DateTime.UtcNow - start).TotalSeconds >= seconds) tcs.TrySetResult();
        }

        CompositionTarget.Rendering += OnRendering;
        await tcs.Task;
        CompositionTarget.Rendering -= OnRendering;
        sv.ScrollToVerticalOffset(0);
        var fps = frames / seconds;
        return $"PERF fps={fps:0.0} worstFrame={worst:0}ms renderTier={tier} frames={frames}";
    }

    private static IEnumerable<ScrollViewer> FindScrollViewers(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) yield return sv;
            foreach (var nested in FindScrollViewers(child)) yield return nested;
        }
    }
}
