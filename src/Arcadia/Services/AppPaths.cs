namespace Arcadia.Services;

/// <summary>All files written by Arcadia live in a single data folder (default %APPDATA%\Arcadia).</summary>
public static class AppPaths
{
    public static string DataDir { get; private set; } = "";
    public static string LibraryFile => Path.Combine(DataDir, "library.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ImagesDir => Path.Combine(DataDir, "images");
    public static string IconsDir => Path.Combine(DataDir, "images", "icons");
    public static string ShortcutsDir => Path.Combine(DataDir, "shortcuts");
    public static string TempDir => Path.Combine(DataDir, "temp");
    public static string LogsDir => Path.Combine(DataDir, "logs");

    public static void Initialize(string? overrideDir)
    {
        DataDir = !string.IsNullOrWhiteSpace(overrideDir)
            ? Path.GetFullPath(overrideDir)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arcadia");

        foreach (var d in new[] { DataDir, ImagesDir, IconsDir, ShortcutsDir, TempDir, LogsDir })
            Directory.CreateDirectory(d);

        try
        {
            foreach (var f in Directory.EnumerateFiles(TempDir)) File.Delete(f);
        }
        catch { /* best effort */ }
    }

    public static string? Resolve(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        return Path.IsPathRooted(relative) ? relative : Path.Combine(DataDir, relative);
    }

    public static string ToRelative(string fullPath)
    {
        var rel = Path.GetRelativePath(DataDir, fullPath);
        return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel) ? fullPath : rel;
    }

    /// <summary>True only for files located inside Arcadia's own data folder.</summary>
    public static bool IsManaged(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        try
        {
            var full = Path.GetFullPath(fullPath);
            return full.StartsWith(DataDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Deletes a cached file. Refuses anything outside the data folder (game files are never touched).</summary>
    public static void TryDeleteManaged(string? relative)
    {
        var full = Resolve(relative);
        if (full == null || !IsManaged(full)) return;
        try { if (File.Exists(full)) File.Delete(full); } catch { /* file in use: ignore */ }
    }
}
