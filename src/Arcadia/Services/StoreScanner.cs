using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Arcadia.Models;
using Microsoft.Win32;

namespace Arcadia.Services;

/// <summary>A game installed through a store launcher, found by <see cref="StoreScanner"/>.</summary>
public sealed class DetectedGame
{
    public string Name { get; init; } = "";
    public GameStore Store { get; init; }
    public string LaunchTarget { get; init; } = "";
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? InstallDir { get; init; }
    public string? Executable { get; init; }
    public int? SteamAppId { get; init; }
    public string? StoreId { get; init; }

    /// <summary>Stable key used to remember ignored detections.</summary>
    public string Key => $"{Store}:{StoreId ?? SteamAppId?.ToString() ?? LaunchTarget}".ToLowerInvariant();
}

/// <summary>
/// Finds games installed with Steam, Epic Games, GOG Galaxy, Ubisoft Connect and the Xbox app.
/// Everything is read-only (manifests and registry); nothing is launched or modified.
/// </summary>
public static partial class StoreScanner
{
    public static List<DetectedGame> ScanAll()
    {
        var all = new List<DetectedGame>();
        foreach (var (name, scan) in new (string, Func<IEnumerable<DetectedGame>>)[]
                 {
                     ("Steam", ScanSteam), ("Epic", ScanEpic), ("GOG", ScanGog), ("Ubisoft", ScanUbisoft), ("Xbox", ScanXbox),
                 })
        {
            try
            {
                var found = scan().ToList();
                Log.Info($"Store scan {name}: {found.Count} game(s)");
                all.AddRange(found);
            }
            catch (Exception ex) { Log.Warn($"Store scan {name} failed: {ex.Message}"); }
        }
        return all.Where(g => !string.IsNullOrWhiteSpace(g.Name))
                  .DistinctBy(g => g.Key)
                  .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                  .ToList();
    }

    // ------------------------------------------------------------------------------------
    // Steam
    // ------------------------------------------------------------------------------------

    private static readonly HashSet<int> SteamTools = new()
    {
        480, 228980, 1070560, 1391110, 1493710, 1628350, 1826330, 2180100, 250820, 1007, 323910,
        858280, 961940, 930400, 1054830, 1113280, 1245040, 1420170, 1580130, 1887720, 2348590, 2805730, 3658110,
    };

    public static IEnumerable<string> SteamLibraries()
    {
        var root = SteamRoot();
        if (root == null) yield break;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        var paths = new List<string> { root };
        if (File.Exists(vdf))
            foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
                paths.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        foreach (var p in paths)
        {
            var steamapps = Path.Combine(p, "steamapps");
            if (Directory.Exists(steamapps) && seen.Add(steamapps)) yield return steamapps;
        }
    }

    public static string? SteamRoot()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                   ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Replace('/', '\\');
        return Directory.Exists(path) ? path : null;
    }

    private static IEnumerable<DetectedGame> ScanSteam()
    {
        foreach (var steamapps in SteamLibraries())
        {
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                string text;
                try { text = File.ReadAllText(acf); } catch { continue; }
                var id = AcfField(text, "appid");
                var name = AcfField(text, "name");
                var dir = AcfField(text, "installdir");
                if (!int.TryParse(id, out var appId) || name == null || dir == null || SteamTools.Contains(appId)) continue;
                if (Regex.IsMatch(name, "Redistributable|Proton|Steam Linux Runtime|Steamworks|Dedicated Server|SDK$|Soundtrack", RegexOptions.IgnoreCase)) continue;
                yield return new DetectedGame
                {
                    Name = NameCleaner.CleanOfficialName(name),
                    Store = GameStore.Steam,
                    LaunchTarget = $"steam://rungameid/{appId}",
                    InstallDir = Path.Combine(steamapps, "common", dir),
                    SteamAppId = appId,
                    StoreId = appId.ToString(),
                };
            }
        }
    }

    /// <summary>Install folder of a Steam game (for play time tracking of steam:// shortcuts).</summary>
    public static string? SteamInstallDir(int appId)
    {
        try
        {
            foreach (var steamapps in SteamLibraries())
            {
                var acf = Path.Combine(steamapps, $"appmanifest_{appId}.acf");
                if (!File.Exists(acf)) continue;
                var dir = AcfField(File.ReadAllText(acf), "installdir");
                if (dir != null) return Path.Combine(steamapps, "common", dir);
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static string? AcfField(string text, string key)
    {
        var m = Regex.Match(text, $"\"{key}\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // ------------------------------------------------------------------------------------
    // Epic Games
    // ------------------------------------------------------------------------------------

    private static IEnumerable<DetectedGame> ScanEpic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            DetectedGame? game = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                string? S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
                if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array
                    && !cats.EnumerateArray().Any(c => c.GetString() == "games")) continue;
                var install = S("InstallLocation");
                var appName = S("AppName");
                var ns = S("CatalogNamespace");
                var item = S("CatalogItemId");
                var exe = S("LaunchExecutable");
                if (install == null || appName == null || !Directory.Exists(install)) continue;
                var target = ns != null && item != null
                    ? $"com.epicgames.launcher://apps/{ns}%3A{item}%3A{appName}?action=launch&silent=true"
                    : $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true";
                game = new DetectedGame
                {
                    Name = NameCleaner.CleanOfficialName(S("DisplayName") ?? appName),
                    Store = GameStore.Epic,
                    LaunchTarget = target,
                    InstallDir = install,
                    Executable = exe != null ? Path.Combine(install, exe) : null,
                    StoreId = appName,
                };
            }
            catch { /* malformed manifest */ }
            if (game != null) yield return game;
        }
    }

    // ------------------------------------------------------------------------------------
    // GOG
    // ------------------------------------------------------------------------------------

    private static IEnumerable<DetectedGame> ScanGog()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (key == null) yield break;
        foreach (var sub in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(sub);
            if (g == null) continue;
            var name = g.GetValue("gameName") as string;
            var exe = g.GetValue("exe") as string;
            var path = g.GetValue("path") as string;
            if (name == null || exe == null || !File.Exists(exe)) continue;
            yield return new DetectedGame
            {
                Name = NameCleaner.CleanOfficialName(name),
                Store = GameStore.GOG,
                LaunchTarget = exe,
                Arguments = g.GetValue("launchParam") as string,
                WorkingDirectory = g.GetValue("workingDir") as string ?? path,
                InstallDir = path,
                Executable = exe,
                StoreId = g.GetValue("gameID") as string ?? sub,
            };
        }
    }

    // ------------------------------------------------------------------------------------
    // Ubisoft Connect
    // ------------------------------------------------------------------------------------

    private static IEnumerable<DetectedGame> ScanUbisoft()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (key == null) yield break;
        foreach (var id in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(id);
            var dir = (g?.GetValue("InstallDir") as string)?.Replace('/', '\\').TrimEnd('\\');
            if (dir == null || !Directory.Exists(dir)) continue;
            var name = Registry.GetValue($@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Uplay Install {id}", "DisplayName", null) as string
                       ?? Path.GetFileName(dir);
            yield return new DetectedGame
            {
                Name = NameCleaner.CleanOfficialName(name),
                Store = GameStore.Ubisoft,
                LaunchTarget = $"uplay://launch/{id}/0",
                InstallDir = dir,
                StoreId = id,
            };
        }
    }

    // ------------------------------------------------------------------------------------
    // Xbox app / Game Pass (XboxGames folders)
    // ------------------------------------------------------------------------------------

    private static IEnumerable<DetectedGame> ScanXbox()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var root = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
            if (!Directory.Exists(root)) continue;
            foreach (var gameDir in Directory.EnumerateDirectories(root))
            {
                var config = Path.Combine(gameDir, "Content", "MicrosoftGame.config");
                if (!File.Exists(config)) continue;
                DetectedGame? game = null;
                try
                {
                    var doc = XDocument.Load(config);
                    var identity = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
                    var exeNode = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable");
                    var visuals = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals");
                    var pkg = identity?.Attribute("Name")?.Value;
                    var publisher = identity?.Attribute("Publisher")?.Value;
                    var appId = exeNode?.Attribute("Id")?.Value ?? "Game";
                    if (pkg == null || publisher == null) continue;
                    var display = visuals?.Attribute("DefaultDisplayName")?.Value;
                    if (string.IsNullOrWhiteSpace(display) || display.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase))
                        display = Path.GetFileName(gameDir);
                    var pfn = $"{pkg}_{PublisherId(publisher)}";
                    var exe = exeNode?.Attribute("Name")?.Value;
                    game = new DetectedGame
                    {
                        Name = NameCleaner.CleanOfficialName(display),
                        Store = GameStore.Xbox,
                        LaunchTarget = $"shell:AppsFolder\\{pfn}!{appId}",
                        InstallDir = gameDir,
                        Executable = exe != null ? Path.Combine(gameDir, "Content", exe) : null,
                        StoreId = pfn,
                    };
                }
                catch { /* unreadable config */ }
                if (game != null) yield return game;
            }
        }
    }

    /// <summary>The 13-character publisher hash used in package family names (e.g. 8wekyb3d8bbwe).</summary>
    public static string PublisherId(string publisher)
    {
        const string alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        var bits = new StringBuilder();
        for (var i = 0; i < 8; i++) bits.Append(Convert.ToString(hash[i], 2).PadLeft(8, '0'));
        bits.Append('0');
        var sb = new StringBuilder();
        for (var i = 0; i < 65; i += 5) sb.Append(alphabet[Convert.ToInt32(bits.ToString(i, 5), 2)]);
        return sb.ToString();
    }

    // ------------------------------------------------------------------------------------
    // Platform detection for games imported from files
    // ------------------------------------------------------------------------------------

    private static readonly string[] EmulatorNames =
    {
        "yuzu", "ryujinx", "cemu", "dolphin", "pcsx2", "pcsx2-qt", "rpcs3", "retroarch", "citra", "citra-qt", "duckstation",
        "ppsspp", "ppssppwindows64", "project64", "xenia", "xenia_canary", "mgba", "melonds", "snes9x", "snes9x-x64", "desmume",
        "lime3ds", "suyu", "sudachi", "eden", "citron", "azahar", "vita3k", "shadps4", "xemu", "flycast", "lichii",
    };

    /// <summary>
    /// Platform of a game imported from a file, from how it is launched. Never from the Steam app id alone:
    /// identification finds one for any game sold on Steam, including copies bought on Xbox, Epic...
    /// </summary>
    public static GameStore Detect(Game g)
    {
        var t = g.LaunchTarget ?? "";
        var target = g.TargetPath ?? t;
        if (LaunchesThroughSteam(g)) return GameStore.Steam;
        if (t.StartsWith("uplay:", StringComparison.OrdinalIgnoreCase)) return GameStore.Ubisoft;
        if (t.StartsWith("com.epicgames", StringComparison.OrdinalIgnoreCase)) return GameStore.Epic;
        if (t.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase)) return GameStore.Xbox;
        if (target.Contains(@"\Epic Games\", StringComparison.OrdinalIgnoreCase)) return GameStore.Epic;
        if (target.Contains(@"\GOG Galaxy\Games\", StringComparison.OrdinalIgnoreCase) || target.Contains(@"\GOG Games\", StringComparison.OrdinalIgnoreCase)) return GameStore.GOG;
        if (target.Contains(@"\XboxGames\", StringComparison.OrdinalIgnoreCase) || target.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) return GameStore.Xbox;
        if (target.Contains(@"\Ubisoft Game Launcher\", StringComparison.OrdinalIgnoreCase)) return GameStore.Ubisoft;
        if (target.Contains(@"\EA Games\", StringComparison.OrdinalIgnoreCase)) return GameStore.EA;
        var exe = Path.GetFileNameWithoutExtension(target);
        if (EmulatorNames.Contains(exe, StringComparer.OrdinalIgnoreCase) || NameCleaner.FindRomInArguments(g.Arguments) != null) return GameStore.Emulator;
        if (g.SourceKind == GameSourceKind.Shortcut && string.IsNullOrWhiteSpace(g.TargetPath)) return GameStore.Xbox; // advertised (Store) shortcut
        return LaunchHelper.IsUri(t) ? GameStore.Other : GameStore.Local;
    }

    /// <summary>
    /// Platform of a game loaded from library.json: unknown (old libraries), or wrongly "Steam" because older
    /// versions marked so any game identified on Steam (e.g. Xbox / Game Pass copies). Returns true when changed.
    /// </summary>
    public static bool CorrectStore(Game g)
    {
        if (g.Store != GameStore.Unknown && !(g.Store == GameStore.Steam && !LaunchesThroughSteam(g))) return false;
        var store = Detect(g);
        if (store == g.Store) return false;
        if (g.Store != GameStore.Unknown) Log.Info($"Platform of '{g.Name}' corrected: {g.Store} -> {store}");
        g.Store = store;
        return true;
    }

    /// <summary>Started by Steam: a steam:// link, an exe of a Steam library, or steam.exe -applaunch.</summary>
    public static bool LaunchesThroughSteam(Game g)
    {
        var t = g.LaunchTarget ?? "";
        var target = g.TargetPath ?? t;
        return t.StartsWith("steam:", StringComparison.OrdinalIgnoreCase)
               || target.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase)
               || g.SteamAppId != null && string.Equals(Path.GetFileName(target), "steam.exe", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPath();
}
