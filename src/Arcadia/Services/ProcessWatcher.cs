using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Arcadia.Services;

/// <summary>
/// Detects processes running from a game's install folder. Used to measure play time for games
/// started through a launcher (Steam, Epic, Ubisoft...) or through a stub that exits immediately.
/// </summary>
public static class ProcessWatcher
{
    private static readonly int OwnPid = Environment.ProcessId;

    /// <summary>True if a process whose executable lives under <paramref name="folder"/> is running.</summary>
    public static bool AnyRunningIn(string folder)
    {
        var prefix = folder.TrimEnd('\\') + "\\";
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4 || p.Id == OwnPid) continue;
                var path = GetPath(p.Id);
                if (path != null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith("UnityCrashHandler64.exe", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith("crashpad_handler.exe", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            finally { p.Dispose(); }
        }
        return false;
    }

    /// <summary>
    /// Waits for the game to appear (up to <paramref name="startTimeout"/>), then until it exits.
    /// Returns the measured duration, or null if the game never appeared.
    /// </summary>
    public static async Task<(DateTime Start, TimeSpan Duration)?> WatchAsync(string folder, TimeSpan startTimeout,
        Action? onStarted, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder)) return null;
        var deadline = DateTime.Now + startTimeout;
        DateTime? start = null;
        while (DateTime.Now < deadline && !ct.IsCancellationRequested)
        {
            if (await Task.Run(() => AnyRunningIn(folder), ct)) { start = DateTime.Now; break; }
            await Task.Delay(2500, ct);
        }
        if (start == null) return null;
        onStarted?.Invoke();

        var misses = 0;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(3000, ct);
            if (await Task.Run(() => AnyRunningIn(folder), ct)) misses = 0;
            else if (++misses >= 2) break; // tolerate a short gap (launcher -> game)
        }
        return (start.Value, DateTime.Now - start.Value - TimeSpan.FromSeconds(3));
    }

    /// <summary>The folder that best represents a game installation for an executable.</summary>
    public static string? GameFolderOf(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        var dir = Path.GetDirectoryName(exe);
        string[] technical = { "win64", "win32", "x64", "x86", "binaries", "bin", "bin64", "game", "windowsnoeditor", "release" };
        for (var i = 0; i < 3 && dir != null; i++)
        {
            var name = Path.GetFileName(dir);
            if (!technical.Contains(name.ToLowerInvariant())) break;
            dir = Path.GetDirectoryName(dir);
        }
        return IsWatchable(dir) ? dir : null;
    }

    /// <summary>Never watch a drive root, Windows, or a shared container folder (it would never look "closed").</summary>
    public static bool IsWatchable(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        var d = dir.TrimEnd('\\');
        if (Path.GetPathRoot(d)?.TrimEnd('\\') == d) return false;
        var blocked = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };
        foreach (var b in blocked.Where(b => !string.IsNullOrEmpty(b)))
        {
            if (d.Equals(b, StringComparison.OrdinalIgnoreCase)) return false;
            if (b.EndsWith("Windows", StringComparison.OrdinalIgnoreCase) && d.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static string? GetPath(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);
}
