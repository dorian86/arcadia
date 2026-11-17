using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;

namespace Arcadia.Services;

/// <summary>
/// Developer check (--mouse "move 300 400; click; wheel 2; back"): drives the real mouse over the snapshot window
/// so clicks go through the whole input pipeline (window title-bar hit testing included). The pointer is put
/// back where it was afterwards. Coordinates are window-relative, in device-independent pixels.
/// </summary>
public static class DevMouse
{
    public static async Task RunAsync(Window window, string script)
    {
        if (_original == null && GetCursorPos(out var original)) _original = original;
        window.Activate();
        await Task.Delay(300);
        try
        {
            foreach (var raw in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (parts[0].ToLowerInvariant())
                {
                    case "move":
                        var p = window.PointToScreen(new Point(Num(parts[1]), Num(parts[2])));
                        // a few intermediate points, like a real hand
                        GetCursorPos(out var from);
                        for (var i = 1; i <= 6; i++)
                        {
                            SetCursorPos((int)(from.X + (p.X - from.X) * i / 6), (int)(from.Y + (p.Y - from.Y) * i / 6));
                            await Task.Delay(15);
                        }
                        break;
                    case "click": Send(LeftDown); Send(LeftUp); break;
                    case "dclick": Send(LeftDown); Send(LeftUp); await Task.Delay(60); Send(LeftDown); Send(LeftUp); break;
                    case "rclick": Send(RightDown); Send(RightUp); break;
                    case "back": Send(XDown, 1); Send(XUp, 1); break;
                    case "wheel":
                        var notches = (int)Num(parts[1]);
                        for (var i = 0; i < Math.Abs(notches); i++) { Send(Wheel, notches > 0 ? -120 : 120); await Task.Delay(120); }
                        break;
                    case "wait": await Task.Delay((int)Num(parts[1])); continue;
                }
                await Task.Delay(350);
            }
        }
        catch (Exception ex) { Log.Warn($"Dev mouse script failed: {ex.Message}"); }
        await Task.Delay(500);
    }

    private static POINT? _original;

    /// <summary>Puts the pointer back where it was (called after the snapshot, so it cannot hover anything first).</summary>
    public static void Restore()
    {
        if (_original is { } p) SetCursorPos(p.X, p.Y);
        _original = null;
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private const uint LeftDown = 0x2, LeftUp = 0x4, RightDown = 0x8, RightUp = 0x10, XDown = 0x80, XUp = 0x100, Wheel = 0x800;

    private static void Send(uint flags, int data = 0) => mouse_event(flags, 0, 0, data, IntPtr.Zero);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);
}
