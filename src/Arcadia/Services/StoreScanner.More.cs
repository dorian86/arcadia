using System.Text.RegularExpressions;
using System.Xml.Linq;
using Arcadia.Models;
using Microsoft.Win32;

namespace Arcadia.Services;

/// <summary>EA app, Battle.net and Riot Games: found through the Windows "installed programs" list (read-only).</summary>
public static partial class StoreScanner
{
    /// <summary>One entry of the Windows list of installed programs.</summary>
    internal sealed record InstalledProgram(string Key, string Name, string? Publisher, string? InstallLocation, string? UninstallString, string? DisplayIcon);

    internal static IEnumerable<InstalledProgram> InstalledPrograms()
    {
        var roots = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, path) in roots)
        {
            using var root = hive.OpenSubKey(path);
            if (root == null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                InstalledProgram? program = null;
                try
                {
                    using var k = root.OpenSubKey(name);
                    if (k?.GetValue("DisplayName") is string display && !string.IsNullOrWhiteSpace(display))
                        program = new InstalledProgram(name, display.Trim(), k.GetValue("Publisher") as string,
                            CleanDir(k.GetValue("InstallLocation") as string), k.GetValue("UninstallString") as string, k.GetValue("DisplayIcon") as string);
                }
                catch { /* unreadable key */ }
                if (program != null) yield return program;
            }
        }
    }

    private static string? CleanDir(string? dir) =>
        string.IsNullOrWhiteSpace(dir) ? null : dir.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');

    // ------------------------------------------------------------------------------------
    // EA app (and old Origin installs)
    // ------------------------------------------------------------------------------------

    private static IEnumerable<DetectedGame> ScanEa()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Name, string Dir)>();
        foreach (var p in InstalledPrograms())
        {
            if (p.InstallLocation == null) continue;
            var eaUninstaller = p.UninstallString?.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase) == true;
            if (eaUninstaller || File.Exists(Path.Combine(p.InstallLocation, "__Installer", "installerdata.xml")))
                candidates.Add((p.Name, p.InstallLocation));
        }
        // games installed by EA also register "Install Dir" under SOFTWARE\EA Games
        using (var ea = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\EA Games"))
            foreach (var sub in ea?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var k = ea!.OpenSubKey(sub);
                if (CleanDir(k?.GetValue("Install Dir") as string) is { } dir) candidates.Add((k?.GetValue("DisplayName") as string ?? sub, dir));
            }

        foreach (var (name, dir) in candidates)
        {
            if (!Directory.Exists(dir) || !seen.Add(dir)) continue;
            var xml = Path.Combine(dir, "__Installer", "installerdata.xml");
            EaInstallerData? data = null;
            try { if (File.Exists(xml)) data = ParseEaInstallerData(File.ReadAllText(xml), dir); }
            catch { /* unreadable manifest */ }
            if (data?.ContentIds.Count == 0 && data.Executable == null) data = null;
            var exe = data?.Executable is { } e && File.Exists(e) ? e : null;
            var offer = data?.ContentIds.FirstOrDefault();
            if (offer == null && exe == null) continue; // nothing to start it with
            yield return new DetectedGame
            {
                Name = NameCleaner.CleanOfficialName(data?.Title ?? name),
                Store = GameStore.EA,
                // the EA app starts the game (and handles its login / updates)
                LaunchTarget = offer != null ? $"origin2://game/launch?offerIds={string.Join(",", data!.ContentIds)}" : exe!,
                WorkingDirectory = offer == null ? Path.GetDirectoryName(exe) : null,
                InstallDir = dir,
                Executable = exe,
                StoreId = offer ?? dir,
            };
        }
    }

    internal sealed record EaInstallerData(string? Title, List<string> ContentIds, string? Executable);

    /// <summary>Reads __Installer\installerdata.xml: game title, content (offer) ids and the exe started by the EA app.</summary>
    internal static EaInstallerData ParseEaInstallerData(string xml, string installDir)
    {
        var doc = XDocument.Parse(xml);
        var ids = doc.Descendants().Where(e => e.Name.LocalName == "contentID")
                     .Select(e => e.Value.Trim()).Where(v => v.Length > 0).Distinct().ToList();
        string? title = doc.Descendants().Where(e => e.Name.LocalName == "gameTitle")
                           .FirstOrDefault(e => (string?)e.Attribute("locale") is "fr_FR")?.Value
                        ?? doc.Descendants().Where(e => e.Name.LocalName == "gameTitle")
                              .FirstOrDefault(e => (string?)e.Attribute("locale") is "en_US" or null)?.Value;
        // <launcher><filePath>[HKEY_LOCAL_MACHINE\SOFTWARE\...\Install Dir]bin\game.exe</filePath>
        string? exe = null;
        foreach (var launcher in doc.Descendants().Where(e => e.Name.LocalName == "launcher"))
        {
            if (launcher.Descendants().Any(e => e.Name.LocalName == "trial" && e.Value.Trim() == "1")) continue;
            var path = launcher.Descendants().FirstOrDefault(e => e.Name.LocalName == "filePath")?.Value.Trim();
            if (string.IsNullOrEmpty(path)) continue;
            path = Regex.Replace(path, @"^\[[^\]]*\]", "").TrimStart('\\');
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            exe = Path.Combine(installDir, path);
            break;
        }
        return new EaInstallerData(string.IsNullOrWhiteSpace(title) ? null : title.Trim(), ids, exe);
    }

    // ------------------------------------------------------------------------------------
    // Battle.net
    // ------------------------------------------------------------------------------------

    /// <summary>Battle.net product uid (from its uninstaller) → code accepted by "Battle.net.exe --exec=launch".</summary>
    internal static readonly Dictionary<string, string> BattleNetCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wow"] = "WoW", ["wow_classic"] = "WoWC", ["wow_classic_era"] = "WoWC", ["d3"] = "D3", ["diablo3"] = "D3",
        ["fenris"] = "Fen", ["osi"] = "OSI", ["anbs"] = "ANBS", ["s1"] = "S1", ["s2"] = "S2", ["hs_beta"] = "WTCG",
        ["heroes"] = "Hero", ["prometheus"] = "Pro", ["w3"] = "W3", ["odin"] = "ODIN", ["viper"] = "VIPR", ["zeus"] = "ZEUS",
        ["lazarus"] = "LAZR", ["fore"] = "FORE", ["auks"] = "AUKS", ["wlby"] = "WLBY", ["rtro"] = "RTRO", ["gryphon"] = "GRY",
    };

    /// <summary>The "--uid=xxx" of a Blizzard uninstaller command line.</summary>
    internal static string? BattleNetUid(string? uninstall)
    {
        if (uninstall == null || !uninstall.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase)) return null;
        var m = Regex.Match(uninstall, @"--uid=""?([^""\s]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static IEnumerable<DetectedGame> ScanBattleNet()
    {
        var programs = InstalledPrograms().ToList();
        var client = programs.Where(p => p.Name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase) && p.InstallLocation != null)
                             .Select(p => Path.Combine(p.InstallLocation!, "Battle.net.exe")).FirstOrDefault(File.Exists)
                     ?? new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
                         .Select(f => Path.Combine(Environment.GetFolderPath(f), "Battle.net", "Battle.net.exe")).FirstOrDefault(File.Exists);
        foreach (var p in programs)
        {
            var uid = BattleNetUid(p.UninstallString);
            if (uid == null || uid.StartsWith("battle.net", StringComparison.OrdinalIgnoreCase) || uid.Equals("agent", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.InstallLocation == null || !Directory.Exists(p.InstallLocation)) continue;
            BattleNetCodes.TryGetValue(uid, out var code);
            yield return new DetectedGame
            {
                Name = NameCleaner.CleanOfficialName(p.Name),
                Store = GameStore.BattleNet,
                // the game must be started by Battle.net (login); without a known code, the launcher opens
                LaunchTarget = client ?? (code != null ? $"battlenet://{code}" : "battlenet://"),
                Arguments = client != null && code != null ? $"--exec=\"launch {code}\"" : null,
                InstallDir = p.InstallLocation,
                StoreId = uid,
            };
        }
    }

    // ------------------------------------------------------------------------------------
    // Riot Games
    // ------------------------------------------------------------------------------------

    /// <summary>"--uninstall-product=valorant --uninstall-patchline=live" → (valorant, live).</summary>
    internal static (string Product, string Patchline)? RiotProduct(string? uninstall)
    {
        if (uninstall == null || !uninstall.Contains("RiotClientServices", StringComparison.OrdinalIgnoreCase)) return null;
        var product = Regex.Match(uninstall, @"--uninstall-product=""?([^""\s]+)", RegexOptions.IgnoreCase);
        if (!product.Success || product.Groups[1].Value.Equals("Riot_Client", StringComparison.OrdinalIgnoreCase)) return null;
        var line = Regex.Match(uninstall, @"--uninstall-patchline=""?([^""\s]+)", RegexOptions.IgnoreCase);
        return (product.Groups[1].Value, line.Success ? line.Groups[1].Value : "live");
    }

    private static IEnumerable<DetectedGame> ScanRiot()
    {
        foreach (var p in InstalledPrograms())
        {
            if (RiotProduct(p.UninstallString) is not var (product, patchline)) continue;
            var client = Regex.Match(p.UninstallString!, @"""?([^""]*RiotClientServices\.exe)""?", RegexOptions.IgnoreCase).Groups[1].Value;
            if (!File.Exists(client) || p.InstallLocation == null || !Directory.Exists(p.InstallLocation)) continue;
            yield return new DetectedGame
            {
                Name = NameCleaner.CleanOfficialName(p.Name),
                Store = GameStore.Riot,
                LaunchTarget = client,
                Arguments = $"--launch-product={product} --launch-patchline={patchline}",
                InstallDir = p.InstallLocation,
                StoreId = $"{product}.{patchline}",
            };
        }
    }
}
