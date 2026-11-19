using System.Text.Json;
using Arcadia.Services.Online;

namespace Arcadia.Services;

/// <summary>A game installed with the Epic Games launcher.</summary>
public sealed record EpicInstall(string Dir, string? Namespace, string? ItemId, string AppName, string? LaunchExecutable, string? Title);

/// <summary>
/// Reads what the Epic Games launcher keeps on the PC (read-only). Two generations:
/// <list type="bullet">
/// <item>the classic launcher: one JSON "*.item" per game in C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests;</item>
/// <item>the new launcher (2026, installed per user): %LOCALAPPDATA%\com.epicgames.eglv2, with the installed games
/// as a journal (installations.jsonl: one line per install / update / uninstall), titles in cache\prod\catalog-cache.json
/// and the play time of the account in cache\prod\account\&lt;id&gt;\playtime-cache.json.</item>
/// </list>
/// Games installed by the classic launcher and not taken over by the new one still have their ".egstore" folder.
/// </summary>
public static class EpicLauncher
{
    public static string NewDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "com.epicgames.eglv2");
    public static string LegacyManifests => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");

    /// <summary>Launch link understood by both launchers (the new one still registers com.epicgames.launcher://).</summary>
    public static string LaunchUri(string? ns, string? itemId, string appName) =>
        ns != null && itemId != null
            ? $"com.epicgames.launcher://apps/{ns}%3A{itemId}%3A{appName}?action=launch&silent=true"
            : $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true";

    // ------------------------------------------------------------------------------------
    // New launcher: installations.jsonl
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Replays the journal: the last line of each installation wins, "deleted" removes it. An update in progress
    /// ("complete": false after a finished install) keeps the game; a first install not finished yet does not count.
    /// </summary>
    internal static List<EpicInstall> ParseInstallations(string jsonl, IReadOnlyDictionary<string, string>? titles = null)
    {
        var last = new Dictionary<string, JsonElement>();
        var finished = new HashSet<string>();
        var order = new List<string>();
        foreach (var line in jsonl.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonElement e;
            try { e = JsonDocument.Parse(line).RootElement.Clone(); }
            catch (JsonException) { continue; } // a line being written
            var id = e.Str("installationId");
            if (id == null) continue; // header
            if (e.TryGetProperty("deleted", out var del) && del.ValueKind == JsonValueKind.True)
            {
                last.Remove(id);
                finished.Remove(id);
                continue;
            }
            if (!last.ContainsKey(id)) order.Add(id);
            last[id] = e;
            if (e.TryGetProperty("complete", out var c) && c.ValueKind == JsonValueKind.True || e.Str("version") != null) finished.Add(id);
        }
        var result = new List<EpicInstall>();
        foreach (var id in order)
        {
            if (!last.TryGetValue(id, out var e) || !finished.Contains(id)) continue;
            var dir = e.Str("dir");
            var app = e.Str("artifactId") ?? e.Str("appName");
            if (string.IsNullOrWhiteSpace(dir) || app == null) continue;
            if (e.Str("platform") is { } platform && !platform.Equals("Windows", StringComparison.OrdinalIgnoreCase)) continue;
            var item = e.Str("itemId");
            string? title = null;
            if (item != null) titles?.TryGetValue(item, out title);
            result.Add(new EpicInstall(dir.TrimEnd('\\'), e.Str("namespace"), item, app, NullIfEmpty(e.Str("launchExecutable")), title));
        }
        return result;
    }

    /// <summary>Titles of the catalog items the launcher has seen (itemId → title).</summary>
    internal static Dictionary<string, string> ParseTitles(string catalogJson)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(catalogJson);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object) return titles;
        foreach (var p in items.EnumerateObject())
            if (p.Value.Obj("item").Str("title") is { Length: > 0 } title) titles[p.Name] = title;
        return titles;
    }

    /// <summary>Play time per game (AppName / artifactId → seconds).</summary>
    internal static Dictionary<string, long> ParsePlaytime(string json)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.Arr("items"))
            if (item.Str("artifactId") is { } app && item.Long("totalTime") is long seconds && seconds > 0)
                result[app] = Math.Max(seconds, result.GetValueOrDefault(app));
        return result;
    }

    public static List<EpicInstall> ReadNewLauncherInstalls()
    {
        var file = Path.Combine(NewDataDir, "installations.jsonl");
        if (!File.Exists(file)) return new List<EpicInstall>();
        return ParseInstallations(ReadShared(file), ReadTitles());
    }

    public static Dictionary<string, string> ReadTitles()
    {
        try
        {
            var file = Path.Combine(NewDataDir, "cache", "prod", "catalog-cache.json");
            return File.Exists(file) ? ParseTitles(ReadShared(file)) : new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Log.Warn($"Epic titles unreadable: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Play time recorded by the new Epic launcher, all accounts of this PC together.</summary>
    public static Dictionary<string, long> ReadPlaytime()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var accounts = Path.Combine(NewDataDir, "cache", "prod", "account");
        if (!Directory.Exists(accounts)) return result;
        foreach (var file in Directory.EnumerateFiles(accounts, "playtime-cache.json", SearchOption.AllDirectories))
        {
            try
            {
                foreach (var (app, seconds) in ParsePlaytime(ReadShared(file)))
                    result[app] = Math.Max(seconds, result.GetValueOrDefault(app));
            }
            catch (Exception ex) { Log.Warn($"Epic play time unreadable ({file}): {ex.Message}"); }
        }
        return result;
    }

    // ------------------------------------------------------------------------------------
    // .egstore (inside each game folder installed by Epic)
    // ------------------------------------------------------------------------------------

    /// <summary>The Epic identity of a game folder, read from its ".egstore\*.mancpn" (null if not an Epic install).</summary>
    public static EpicInstall? FromEgstore(string dir)
    {
        var store = Path.Combine(dir, ".egstore");
        if (!Directory.Exists(store)) return null;
        foreach (var mancpn in Directory.EnumerateFiles(store, "*.mancpn"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(mancpn));
                var r = doc.RootElement;
                var app = r.Str("AppName");
                if (app == null) continue;
                // the launch exe is in the manifest when it is JSON (older ones are binary)
                string? exe = null;
                var manifest = Path.ChangeExtension(mancpn, ".manifest");
                if (File.Exists(manifest)) exe = ReadLaunchExe(manifest);
                return new EpicInstall(dir.TrimEnd('\\'), r.Str("CatalogNamespace"), r.Str("CatalogItemId"), app, exe, null);
            }
            catch { /* unreadable */ }
        }
        return null;
    }

    /// <summary>"LaunchExeString" of a JSON manifest, read from its first kilobytes only (they can weigh megabytes).</summary>
    internal static string? ReadLaunchExe(string manifest)
    {
        using var fs = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[8192];
        var n = fs.Read(buffer, 0, buffer.Length);
        if (n == 0 || buffer[0] != (byte)'{') return null;
        var head = System.Text.Encoding.UTF8.GetString(buffer, 0, n);
        var m = System.Text.RegularExpressions.Regex.Match(head, "\"LaunchExeString\"\\s*:\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value.Replace("\\/", "/").Replace('/', '\\') : null;
    }

    /// <summary>
    /// Epic identity of the folder of an installed game, or of its parent folders (Binaries\Win64\game.exe). A
    /// ".egstore" higher up only counts when its manifest names this exe: a folder holding many games can carry
    /// one left by an old install (e.g. "G:\jeux\.egstore").
    /// </summary>
    public static EpicInstall? FromGameFolder(string? exeOrDir)
    {
        if (string.IsNullOrWhiteSpace(exeOrDir)) return null;
        try
        {
            var isDir = Directory.Exists(exeOrDir);
            var exeName = isDir ? null : Path.GetFileName(exeOrDir);
            var dir = isDir ? exeOrDir : Path.GetDirectoryName(exeOrDir);
            for (var i = 0; i < (isDir ? 1 : 4) && !string.IsNullOrEmpty(dir); i++)
            {
                if (Path.GetPathRoot(dir)?.TrimEnd('\\') == dir.TrimEnd('\\')) break; // never a whole drive
                if (FromEgstore(dir) is { } install)
                {
                    var named = install.LaunchExecutable != null && exeName != null
                                && Path.GetFileName(install.LaunchExecutable).Equals(exeName, StringComparison.OrdinalIgnoreCase);
                    if (named || i == 0) return install;
                    return null; // another game's (or a stray) .egstore: stop here
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { /* inaccessible */ }
        return null;
    }

    /// <summary>Folders where Epic installs games by default (the launcher's setting, "Epic Games" on each drive).</summary>
    public static IEnumerable<string> DefaultLibraries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settings = Path.Combine(NewDataDir, "settings", "app-settings.json");
        if (File.Exists(settings))
        {
            string? dir = null;
            try
            {
                using var doc = JsonDocument.Parse(ReadShared(settings));
                dir = doc.RootElement.Str("defaultInstallDir");
            }
            catch { /* unreadable */ }
            if (dir != null && Directory.Exists(dir) && seen.Add(dir)) yield return dir;
        }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            foreach (var sub in new[] { "Epic Games", @"Program Files\Epic Games", @"Games\Epic Games" })
            {
                var dir = Path.Combine(drive.RootDirectory.FullName, sub);
                if (Directory.Exists(dir) && seen.Add(dir)) yield return dir;
            }
    }

    /// <summary>Reads a file the launcher may be writing (shared access).</summary>
    private static string ReadShared(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
