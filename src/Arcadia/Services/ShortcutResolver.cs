using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;

namespace Arcadia.Services;

public sealed record LnkInfo(string? TargetPath, string? Arguments, string? WorkingDirectory, string? IconPath, int IconIndex);

public sealed record UrlInfo(string Url, string? IconFile, int IconIndex);

/// <summary>Reads Windows shortcuts (.lnk via IShellLink, .url as INI). Files are opened read-only.</summary>
public static partial class ShortcutResolver
{
    public static LnkInfo? ReadLnk(string path)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLinkCoClass();
            ((IPersistFile)link).Load(path, 0 /* STGM_READ */);

            var sb = new StringBuilder(2048);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            var target = Expand(sb.ToString());

            sb.Clear();
            link.GetArguments(sb, sb.Capacity);
            var args = sb.ToString().Trim();

            sb.Clear();
            link.GetWorkingDirectory(sb, sb.Capacity);
            var wd = Expand(sb.ToString());

            sb.Clear();
            link.GetIconLocation(sb, sb.Capacity, out var iconIndex);
            var icon = Expand(sb.ToString());

            return new LnkInfo(NullIfEmpty(target), NullIfEmpty(args), NullIfEmpty(wd), NullIfEmpty(icon), iconIndex);
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot read shortcut {path}: {ex.Message}");
            return null;
        }
        finally
        {
            if (link != null) Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>Used by tests and to validate the interop layer.</summary>
    internal static void CreateLnk(string path, string target, string? arguments = null, string? workingDir = null)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            link.SetPath(target);
            if (arguments != null) link.SetArguments(arguments);
            if (workingDir != null) link.SetWorkingDirectory(workingDir);
            ((IPersistFile)link).Save(path, true);
        }
        finally { Marshal.ReleaseComObject(link); }
    }

    public static UrlInfo? ReadUrl(string path)
    {
        try
        {
            string? url = null, iconFile = null;
            var iconIndex = 0;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) url ??= line[4..].Trim();
                else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) iconFile = Expand(line[9..].Trim());
                else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)) int.TryParse(line[10..].Trim(), out iconIndex);
            }
            return string.IsNullOrWhiteSpace(url) ? null : new UrlInfo(url, NullIfEmpty(iconFile), iconIndex);
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot read url shortcut {path}: {ex.Message}");
            return null;
        }
    }

    public static int? ExtractSteamAppId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = SteamUrl().Match(text);
        if (!m.Success) m = AppLaunch().Match(text);
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>For executables inside a Steam library (…\steamapps\common\Game\…), reads the appmanifest to get the app id.</summary>
    public static int? SteamAppIdFromInstallPath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        try
        {
            const string marker = @"\steamapps\common\";
            var idx = exePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            var steamapps = exePath[..(idx + @"\steamapps".Length)];
            var folder = exePath[(idx + marker.Length)..].Split('\\')[0];
            if (!Directory.Exists(steamapps)) return null;
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var text = File.ReadAllText(acf);
                var dir = InstallDir().Match(text);
                if (!dir.Success || !string.Equals(dir.Groups[1].Value, folder, StringComparison.OrdinalIgnoreCase)) continue;
                var id = AppIdField().Match(text);
                if (id.Success && int.TryParse(id.Groups[1].Value, out var appId)) return appId;
            }
        }
        catch { /* unreadable library */ }
        return null;
    }

    private static string Expand(string s) => string.IsNullOrWhiteSpace(s) ? "" : Environment.ExpandEnvironmentVariables(s.Trim());
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    [GeneratedRegex(@"steam://(?:rungameid|run|launch|nav/games/details)/(\d+)", RegexOptions.IgnoreCase)] private static partial Regex SteamUrl();
    [GeneratedRegex(@"-applaunch\s+(\d+)", RegexOptions.IgnoreCase)] private static partial Regex AppLaunch();
    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex InstallDir();
    [GeneratedRegex("\"appid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase)] private static partial Regex AppIdField();

    // ---- COM interop -----------------------------------------------------------------

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
