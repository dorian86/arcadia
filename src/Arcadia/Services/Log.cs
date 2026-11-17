namespace Arcadia.Services;

public static class Log
{
    private static readonly object Gate = new();
    private static string? _file;

    public static void Init()
    {
        try
        {
            _file = Path.Combine(AppPaths.LogsDir, "arcadia.log");
            var fi = new FileInfo(_file);
            if (fi.Exists && fi.Length > 1_000_000)
            {
                File.Copy(_file, _file + ".old", true);
                File.Delete(_file);
            }
        }
        catch { _file = null; }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message} :: {ex}");

    private static void Write(string level, string message)
    {
        if (_file == null) return;
        try
        {
            lock (Gate)
                File.AppendAllText(_file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
        catch { /* logging must never crash the app */ }
    }
}
