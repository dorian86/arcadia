using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcadia.Services;

/// <summary>
/// Achievements of games started without their store's launcher. Those versions keep the unlocked achievements
/// in small local files (Steam API emulators: Goldberg / GSE, CODEX, RUNE, Online-Fix, CreamAPI, RLD!, EMPRESS…).
/// Everything is read-only: Arcadia only reads these files to show and announce the achievements.
/// </summary>
public static partial class EmulatorAchievements
{
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Public => Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    /// <summary>Folders holding one sub-folder per Steam app id, and where the achievement file is inside it.</summary>
    internal static IEnumerable<(string Root, string File)> Layouts()
    {
        yield return (Path.Combine(AppData, "Goldberg SteamEmu Saves"), "achievements.json");
        yield return (Path.Combine(AppData, "GSE Saves"), "achievements.json");
        yield return (Path.Combine(AppData, "EMPRESS"), @"remote\{0}\achievements.json");
        yield return (Path.Combine(Public, "Documents", "EMPRESS"), @"remote\{0}\achievements.json");
        yield return (Path.Combine(Public, "Documents", "Steam", "CODEX"), "achievements.ini");
        yield return (Path.Combine(Public, "Documents", "Steam", "RUNE"), "achievements.ini");
        yield return (Path.Combine(AppData, "Steam", "CODEX"), "achievements.ini");
        yield return (Path.Combine(Documents, "Steam", "CODEX"), "achievements.ini");
        yield return (Path.Combine(Public, "Documents", "OnlineFix"), @"Stats\Achievements.ini");
        yield return (Path.Combine(AppData, "CreamAPI"), @"stats\CreamAPI.Achievements.cfg");
        yield return (Path.Combine(ProgramData, "Steam", "RLD!"), @"stats\achievements.ini");
        yield return (Path.Combine(ProgramData, "Steam", "Player"), @"stats\achievements.ini");
    }

    /// <summary>Existing folders to watch for new unlocks.</summary>
    public static List<string> Roots() => Layouts().Select(l => l.Root).Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();

    /// <summary>Is this file one of the achievement files (any format)?</summary>
    public static bool IsAchievementFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("achievements.json", StringComparison.OrdinalIgnoreCase)
               || name.Equals("achievements.ini", StringComparison.OrdinalIgnoreCase)
               || name.Equals("CreamAPI.Achievements.cfg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The Steam app id in the path of an achievement file (the "&lt;appid&gt;" folder), if any.</summary>
    public static int? AppIdFromPath(string path)
    {
        for (var dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            if (int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0) return id;
        return null;
    }

    /// <summary>
    /// What is unlocked for a Steam app (achievement API name → unlock date, MinValue when unknown), merged from
    /// every emulator on the PC and from the game's own folder (emulators set to save next to the game).
    /// </summary>
    public static Dictionary<string, DateTime> ReadUnlocked(int appId, string? gameFolder)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        foreach (var (root, file) in Layouts())
        {
            var path = Path.Combine(root, appId.ToString(CultureInfo.InvariantCulture), string.Format(CultureInfo.InvariantCulture, file, appId));
            if (File.Exists(path)) files.Add(path);
        }
        files.AddRange(LocalSaveFiles(gameFolder, appId));
        foreach (var f in files)
        {
            try
            {
                foreach (var (key, at) in Parse(ReadShared(f)))
                    if (!result.TryGetValue(key, out var known) || known == DateTime.MinValue) result[key] = at;
            }
            catch (Exception ex) { Log.Warn($"Achievement file unreadable ({f}): {ex.Message}"); }
        }
        return result;
    }

    /// <summary>Does the game write achievement files (even with nothing unlocked yet)?</summary>
    public static bool HasFiles(int appId, string? gameFolder) =>
        Layouts().Any(l => File.Exists(Path.Combine(l.Root, appId.ToString(CultureInfo.InvariantCulture), string.Format(CultureInfo.InvariantCulture, l.File, appId))))
        || LocalSaveFiles(gameFolder, appId).Any();

    /// <summary>Achievement files saved inside the game folder ("local save" setups): …\&lt;appid&gt;\achievements.json.</summary>
    internal static IEnumerable<string> LocalSaveFiles(string? gameFolder, int appId)
    {
        if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder)) yield break;
        IEnumerable<string> found;
        try
        {
            found = Directory.EnumerateFiles(gameFolder, "achievements.*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true })
                .Where(f => IsAchievementFile(f) && AppIdFromPath(f) == appId).ToList();
        }
        catch { yield break; }
        foreach (var f in found) yield return f;
    }

    /// <summary>Reads any of the formats: JSON (Goldberg / GSE), INI with one section per achievement, RLD! list.</summary>
    internal static Dictionary<string, DateTime> Parse(string text)
    {
        var t = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        if (t.StartsWith('{')) return ParseJson(t);
        return t.Contains("ACHCount", StringComparison.OrdinalIgnoreCase) ? ParseRld(t) : ParseIni(t);
    }

    /// <summary>{"ACH_NAME": {"earned": true, "earned_time": 1700000000}}</summary>
    internal static Dictionary<string, DateTime> ParseJson(string json)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object) continue;
            var earned = p.Value.TryGetProperty("earned", out var e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.Number && e.GetDouble() > 0);
            if (!earned) continue;
            long time = 0;
            if (p.Value.TryGetProperty("earned_time", out var t) && t.ValueKind == JsonValueKind.Number) t.TryGetInt64(out time);
            result[p.Name] = FromUnix(time);
        }
        return result;
    }

    /// <summary>
    /// [ACH_NAME] with Achieved=1 / achieved=true / State=… and UnlockTime= / timestamp= / unlocktime=.
    /// </summary>
    internal static Dictionary<string, DateTime> ParseIni(string text)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        bool achieved = false;
        long time = 0;
        void Flush()
        {
            if (section != null && achieved && !IgnoredSections.Contains(section)) result[section] = FromUnix(time);
            achieved = false;
            time = 0;
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line.EndsWith(']'))
            {
                Flush();
                section = line[1..^1].Trim();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || section == null) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Equals("Achieved", StringComparison.OrdinalIgnoreCase) || key.Equals("State", StringComparison.OrdinalIgnoreCase)
                || key.Equals("HaveAchieved", StringComparison.OrdinalIgnoreCase))
                achieved = value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (key.Equals("UnlockTime", StringComparison.OrdinalIgnoreCase) || key.Equals("timestamp", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("HaveAchievedTime", StringComparison.OrdinalIgnoreCase))
                long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out time);
        }
        Flush();
        return result;
    }

    /// <summary>[Steam] ACH001 = NAME / ACHG001 = 1 (RLD!), sometimes ACHT001 = time.</summary>
    internal static Dictionary<string, DateTime> ParseRld(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var eq = raw.IndexOf('=');
            if (eq > 0) values[raw[..eq].Trim()] = raw[(eq + 1)..].Trim();
        }
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, name) in values)
        {
            var m = RldName().Match(key);
            if (!m.Success) continue;
            var n = m.Groups[1].Value;
            if (!values.TryGetValue("ACHG" + n, out var got) || got is not ("1" or "true")) continue;
            values.TryGetValue("ACHT" + n, out var t);
            long.TryParse(t, out var time);
            result[name] = FromUnix(time);
        }
        return result;
    }

    private static readonly HashSet<string> IgnoredSections = new(StringComparer.OrdinalIgnoreCase) { "Steam", "SteamAchievements", "Stats", "Main" };

    /// <summary>Unix seconds (CreamAPI sometimes writes thousands of seconds); 0 → unknown.</summary>
    internal static DateTime FromUnix(long t)
    {
        if (t <= 0) return DateTime.MinValue;
        if (t > 100_000_000_000) t /= 1000;           // milliseconds
        else if (t < 100_000_000) t *= 1000;          // thousands of seconds
        try { return DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime; }
        catch { return DateTime.MinValue; }
    }

    // ------------------------------------------------------------------------------------
    // The Steam app id of a game folder
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// The real Steam app id written next to the game by its emulator: steam_appid.txt, OnlineFix.ini (RealAppId),
    /// steam_emu.ini / SmartSteamEmu.ini (AppId), cream_api.ini (appid). 480 (Valve's test app) is ignored.
    /// </summary>
    public static int? AppIdFromFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder) || !ProcessWatcher.IsWatchable(folder)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.*", options))
            {
                var name = Path.GetFileName(file).ToLowerInvariant();
                int? id = name switch
                {
                    "steam_appid.txt" => FirstNumber(ReadShared(file)),
                    "onlinefix.ini" => IniNumber(ReadShared(file), "RealAppId"),
                    "steam_emu.ini" or "smartsteamemu.ini" => IniNumber(ReadShared(file), "AppId"),
                    "cream_api.ini" => IniNumber(ReadShared(file), "appid"),
                    _ => null,
                };
                if (id is > 0 and not 480) return id;
            }
        }
        catch { /* inaccessible */ }
        return null;
    }

    private static int? FirstNumber(string text) => FirstNumberRx().Match(text) is { Success: true } m && int.TryParse(m.Value, out var n) ? n : null;

    private static int? IniNumber(string text, string key)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith(';') || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return FirstNumber(line[(eq + 1)..]);
        }
        return null;
    }

    private static string ReadShared(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^ACH(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RldName();

    [GeneratedRegex(@"\d+")]
    private static partial Regex FirstNumberRx();
}

/// <summary>Watches the achievement files and reports which Steam app changed (debounced: a game writes them several times).</summary>
public sealed class AchievementFileWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Action<int?, string> _changed;
    private readonly Dictionary<string, Timer> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <param name="changed">Called on a worker thread with the app id (when the path tells it) and the file.</param>
    public AchievementFileWatcher(Action<int?, string> changed) => _changed = changed;

    public void Watch(string folder)
    {
        if (!Directory.Exists(folder) || _watchers.Any(w => string.Equals(w.Path, folder, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            var w = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            w.Changed += OnEvent;
            w.Created += OnEvent;
            w.Renamed += (_, e) => OnFile(e.FullPath);
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
        catch (Exception ex) { Log.Warn($"Cannot watch {folder}: {ex.Message}"); }
    }

    private void OnEvent(object sender, FileSystemEventArgs e) => OnFile(e.FullPath);

    private void OnFile(string path)
    {
        if (!EmulatorAchievements.IsAchievementFile(path)) return;
        lock (_lock)
        {
            if (_pending.TryGetValue(path, out var timer)) timer.Change(700, Timeout.Infinite);
            else _pending[path] = new Timer(_ =>
            {
                lock (_lock)
                {
                    if (_pending.Remove(path, out var t)) t.Dispose();
                }
                try { _changed(EmulatorAchievements.AppIdFromPath(path), path); }
                catch (Exception ex) { Log.Warn($"Achievement update failed: {ex.Message}"); }
            }, null, 700, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        lock (_lock)
        {
            foreach (var t in _pending.Values) t.Dispose();
            _pending.Clear();
        }
    }
}
