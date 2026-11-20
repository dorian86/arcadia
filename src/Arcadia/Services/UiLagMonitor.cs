using System.Diagnostics;
using System.Windows.Threading;

namespace Arcadia.Services;

/// <summary>
/// Notes in the log when the interface freezes (the UI thread does not answer for 300 ms or more), with the
/// operation that was running when known (<see cref="Step"/>). Helps find what is slow on big libraries.
/// </summary>
public static class UiLagMonitor
{
    private static string? _step;
    private static int _logged;

    /// <summary>Marks what the UI thread is doing: <c>using (UiLagMonitor.Step("Refresh")) { … }</c>, timed in the log when slow.</summary>
    public static IDisposable Step(string name, int logAboveMs = 60) => new Scope(name, logAboveMs);

    public static void Start(Dispatcher ui)
    {
        // every UI task that takes long is named in the log (layout, rendering, a handler of Arcadia…)
        var started = new Dictionary<DispatcherOperation, long>();
        ui.Hooks.OperationStarted += (_, e) => started[e.Operation] = Stopwatch.GetTimestamp();
        ui.Hooks.OperationCompleted += (_, e) =>
        {
            if (!started.Remove(e.Operation, out var t0)) return;
            var ms = (Stopwatch.GetTimestamp() - t0) * 1000 / Stopwatch.Frequency;
            if (ms >= 150 && _logged < 200)
            {
                _logged++;
                Log.Warn($"Slow UI task {ms} ms: {Describe(e.Operation)}{(_step != null ? $" (step {_step})" : "")}");
            }
        };
        ui.Hooks.OperationAborted += (_, e) => started.Remove(e.Operation);

        var thread = new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(250);
                var sw = Stopwatch.StartNew();
                var step = _step;
                try { ui.Invoke(() => { }, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(30)); }
                catch { return; } // shutting down
                var ms = sw.ElapsedMilliseconds;
                if (ms >= 300 && _logged < 200)
                {
                    _logged++;
                    // the step is only named when the whole freeze happened inside it
                    var same = step != null && ReferenceEquals(step, _step);
                    Log.Warn($"UI blocked {ms} ms{(same ? $" (during {step})" : "")}");
                }
            }
        })
        { IsBackground = true, Name = "Arcadia UI lag monitor", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    /// <summary>The method a dispatcher operation runs (read through reflection: diagnostics only).</summary>
    private static string Describe(DispatcherOperation op)
    {
        try
        {
            var field = typeof(DispatcherOperation).GetField("_method", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(op) is Delegate d)
                return $"{d.Method.DeclaringType?.FullName}.{d.Method.Name} [{op.Priority}]";
        }
        catch { /* best effort */ }
        return op.Priority.ToString();
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;
        private readonly int _logAbove;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public Scope(string name, int logAbove)
        {
            _name = name;
            _logAbove = logAbove;
            _previous = _step;
            _step = name;
        }

        public void Dispose()
        {
            _step = _previous;
            if (_sw.ElapsedMilliseconds >= _logAbove) Log.Info($"[timing] {_name}: {_sw.ElapsedMilliseconds} ms");
        }
    }
}
