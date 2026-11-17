using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Arcadia.Services;

/// <summary>Everything known about an imported file that can help naming the game.</summary>
public sealed class NameContext
{
    /// <summary>The .lnk / .url file (its file name is often the best name).</summary>
    public string? ShortcutFile { get; init; }
    /// <summary>The executable started by the game (target of the .lnk or the imported .exe).</summary>
    public string? ExePath { get; init; }
    /// <summary>ProductName from the executable's version resource.</summary>
    public string? ProductName { get; init; }
    /// <summary>A ROM found in the shortcut's arguments (emulator shortcuts).</summary>
    public string? RomPath { get; init; }
}

/// <summary>
/// Turns file names such as "GTA5.exe - Raccourci" or "Getting.Over.It.v1.7-P2P"
/// into human readable titles, and builds name candidates used for online identification.
/// </summary>
public static partial class NameCleaner
{
    private static readonly string[] ShortcutSuffixes =
    {
        "- raccourci", "– raccourci", "- shortcut", "- verknüpfung", "- acceso directo",
        "- collegamento", "- atalho", "- snelkoppeling", "- skrót", "- ярлык",
    };

    private static readonly string[] KnownExtensions = { ".lnk", ".url", ".exe", ".bat", ".cmd" };

    private static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "games", "jeu", "jeux", "jeux pc", "pc games", "bin", "bin64", "bin32", "binaries", "win64", "win32",
        "x64", "x86", "windows", "windowsnoeditor", "windowsclient", "publish", "release", "debug", "app", "data",
        "common", "steamapps", "steamlibrary", "steam", "program files", "program files (x86)", "launcher", "client",
        "build", "builds", "dist", "content", "retail", "shipping", "exe", "base", "system", "files", "versions",
        "emu", "emulators", "emulateurs", "tout", "appdata", "local", "roaming", "users", "desktop", "bureau",
        "downloads", "téléchargements", "documents", "epic games", "gog games", "gog galaxy", "ubisoft", "ea games",
        "origin games", "xboxgames", "windowsapps", "programs", "applications", "apps", "portable", "main",
        "current", "live", "final", "engine", "nw", "package", "packages", "system32", "syswow64", "temp", "tmp",
        "program data", "programdata", "public", "onedrive", "games library", "library", "launchers",
    };

    private static readonly string[] GenericProductWords =
    {
        "unity", "unreal", "godot", "gamemaker", "game maker", "microsoft", "windows", "nw.js", "node-webkit",
        "electron", "chromium", "java", "python", "ren'py", "renpy", "love2d", "löve", "adobe", "mono", "setup",
        "install", "crash", "bootstrap", "stub", "directx", "visual c++", "redistributable", "rpg maker",
        "construct", "defold", "monogame", "xna", "fna", "launcher", "cef", "qt ", "helper", "updater",
    };

    private static readonly HashSet<string> Emulators = new(StringComparer.OrdinalIgnoreCase)
    {
        "yuzu", "ryujinx", "cemu", "dolphin", "pcsx2", "pcsx2-qt", "rpcs3", "retroarch", "citra", "citra-qt",
        "duckstation", "duckstation-qt-x64-releaseltcg", "ppsspp", "ppssppwindows64", "project64", "xenia",
        "xenia_canary", "mgba", "melonds", "snes9x", "snes9x-x64", "desmume", "lime3ds", "suyu", "sudachi",
        "eden", "citron", "azahar", "vita3k", "shadps4", "xemu", "flycast", "mame", "mesen", "bsnes", "fceux",
    };

    private static readonly string[] RomExtensions =
    {
        "xci", "nsp", "nsz", "xcz", "iso", "wbfs", "rvz", "wud", "wux", "rpx", "wua", "z64", "n64", "v64", "nds",
        "3ds", "cia", "cci", "gba", "gbc", "gb", "sfc", "smc", "nes", "gcm", "gcz", "chd", "cue", "cso", "pbp",
        "xex", "pkg",
    };

    /// <summary>Common abbreviations (compact keys) mapped to official titles.</summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["gta"] = "Grand Theft Auto",
        ["gta5"] = "Grand Theft Auto V", ["gtav"] = "Grand Theft Auto V", ["gta5enhanced"] = "Grand Theft Auto V",
        ["gta4"] = "Grand Theft Auto IV", ["gtaiv"] = "Grand Theft Auto IV",
        ["gtasa"] = "Grand Theft Auto: San Andreas", ["gtasanandreas"] = "Grand Theft Auto: San Andreas",
        ["gtavc"] = "Grand Theft Auto: Vice City", ["gta3"] = "Grand Theft Auto III",
        ["rdr2"] = "Red Dead Redemption 2", ["rdr"] = "Red Dead Redemption",
        ["csgo"] = "Counter-Strike: Global Offensive", ["cs2"] = "Counter-Strike 2",
        ["hl2"] = "Half-Life 2", ["tf2"] = "Team Fortress 2", ["l4d2"] = "Left 4 Dead 2", ["l4d"] = "Left 4 Dead",
        ["ds3"] = "Dark Souls III", ["darksoulsiii"] = "Dark Souls III", ["ds2"] = "Dark Souls II",
        ["eldenring"] = "ELDEN RING", ["sekiro"] = "Sekiro: Shadows Die Twice",
        ["tw3"] = "The Witcher 3: Wild Hunt",
        ["cp2077"] = "Cyberpunk 2077",
        ["bf1"] = "Battlefield 1", ["bf4"] = "Battlefield 4", ["bf2042"] = "Battlefield 2042", ["bfv"] = "Battlefield V",
        ["bo3"] = "Call of Duty: Black Ops III", ["blackops3"] = "Call of Duty: Black Ops III",
        ["bo2"] = "Call of Duty: Black Ops II", ["mw2"] = "Call of Duty: Modern Warfare 2", ["mw3"] = "Call of Duty: Modern Warfare 3",
        ["aco"] = "Assassin's Creed Odyssey", ["acodyssey"] = "Assassin's Creed Odyssey",
        ["acu"] = "Assassin's Creed Unity", ["acvalhalla"] = "Assassin's Creed Valhalla", ["acorigins"] = "Assassin's Creed Origins",
        ["fh4"] = "Forza Horizon 4", ["fh5"] = "Forza Horizon 5", ["fh6"] = "Forza Horizon 6",
        ["ts4"] = "The Sims 4", ["sims4"] = "The Sims 4", ["ts3"] = "The Sims 3",
        ["ktane"] = "Keep Talking and Nobody Explodes",
        ["nsuns4"] = "NARUTO SHIPPUDEN: Ultimate Ninja STORM 4",
        ["superhotvr"] = "SUPERHOT VR",
        ["tjoc"] = "The Joy of Creation", ["tjocr"] = "The Joy of Creation: Reborn", ["tjocsm"] = "The Joy of Creation: Story Mode",
        ["fnaf"] = "Five Nights at Freddy's", ["ddlc"] = "Doki Doki Literature Club",
        ["poe"] = "Path of Exile", ["poe2"] = "Path of Exile 2", ["lol"] = "League of Legends",
        ["pubg"] = "PUBG: BATTLEGROUNDS", ["tslgame"] = "PUBG: BATTLEGROUNDS",
        ["r6s"] = "Tom Clancy's Rainbow Six Siege", ["rainbowsix"] = "Tom Clancy's Rainbow Six Siege",
        ["eft"] = "Escape from Tarkov", ["bg3"] = "Baldur's Gate 3", ["nms"] = "No Man's Sky",
        ["ets2"] = "Euro Truck Simulator 2", ["eurotrucks2"] = "Euro Truck Simulator 2", ["amtrucks"] = "American Truck Simulator",
        ["re2"] = "Resident Evil 2", ["re3"] = "Resident Evil 3", ["re4"] = "Resident Evil 4", ["re8"] = "Resident Evil Village",
        ["mhw"] = "Monster Hunter: World", ["monsterhunterworld"] = "Monster Hunter: World", ["mhr"] = "Monster Hunter Rise",
        ["ffxiv"] = "Final Fantasy XIV", ["ffxiv_dx11"] = "Final Fantasy XIV", ["ff7remake"] = "Final Fantasy VII Remake",
        ["dmc5"] = "Devil May Cry 5", ["sottr"] = "Shadow of the Tomb Raider", ["rottr"] = "Rise of the Tomb Raider",
        ["kcd"] = "Kingdom Come: Deliverance", ["kcd2"] = "Kingdom Come: Deliverance II",
        ["sf6"] = "Street Fighter 6", ["mcc"] = "Halo: The Master Chief Collection",
        ["robloxplayerbeta"] = "Roblox", ["robloxplayer"] = "Roblox", ["minecraftlauncher"] = "Minecraft",
        ["javaw"] = "Minecraft", ["hollowknight"] = "Hollow Knight", ["geometrydash"] = "Geometry Dash",
        ["jobsimulator"] = "Job Simulator", ["smb3d"] = "Super Meat Boy 3D",
        ["fortniteclientwin64shipping"] = "Fortnite", ["fortnitelauncher"] = "Fortnite",
        ["valorant"] = "VALORANT", ["rocketleague"] = "Rocket League", ["deadbydaylight"] = "Dead by Daylight",
        ["amongus"] = "Among Us", ["stardewvalley"] = "Stardew Valley", ["terraria"] = "Terraria",
    };

    // ------------------------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------------------------

    /// <summary>"Subnautica.exe - Raccourci.lnk" → "Subnautica"; "Cyberpunk2077.exe" → "Cyberpunk 2077".</summary>
    public static string CleanFileName(string pathOrName)
    {
        var s = StripShortcutDecorations(Path.GetFileName(pathOrName.TrimEnd('\\', '/')));
        return CleanCore(s);
    }

    /// <summary>Removes ™ ® © and extra spaces from an official title.</summary>
    public static string CleanOfficialName(string name)
    {
        var s = Trademarks().Replace(name, "");
        s = MultiSpace().Replace(s, " ").Trim();
        s = s.Replace(" :", ":");
        return s.Length == 0 ? name.Trim() : s;
    }

    /// <summary>Builds the display name and the list of search candidates for an imported file.</summary>
    public static (string Display, List<string> Hints) Suggest(NameContext ctx)
    {
        var hints = new List<string>();
        void Add(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            var key = NameMatcher.Compact(candidate);
            if (key.Length == 0) return;
            if (hints.Any(h => NameMatcher.Compact(h) == key)) return;
            hints.Add(candidate.Trim());
        }

        string? exeRaw = ctx.ExePath != null ? Path.GetFileNameWithoutExtension(ctx.ExePath) : null;
        string? exeClean = ctx.ExePath != null ? CleanFileName(ctx.ExePath) : null;
        string? folderClean = ctx.ExePath != null ? FolderCandidate(ctx.ExePath) : null;
        string? product = CleanProduct(ctx.ProductName);
        string? rom = ctx.RomPath != null ? CleanCore(Path.GetFileNameWithoutExtension(ctx.RomPath)) : null;
        bool isEmulator = exeRaw != null && Emulators.Contains(exeRaw);

        string? shortcutClean = null;
        bool renamed = false;
        if (ctx.ShortcutFile != null)
        {
            var rawShortcut = Path.GetFileNameWithoutExtension(ctx.ShortcutFile);
            shortcutClean = CleanFileName(ctx.ShortcutFile);
            renamed = exeClean == null
                      || (!rawShortcut.Contains(".exe", StringComparison.OrdinalIgnoreCase)
                          && NameMatcher.Compact(shortcutClean) != NameMatcher.Compact(exeClean));
        }

        if (renamed) Add(shortcutClean);
        Add(rom);

        // The user named this shortcut: trust that name, never the target's folder (e.g. a shortcut to cmd.exe).
        if (renamed) return (hints[0], hints.ToList());

        if (isEmulator && exeClean != null)
        {
            Add(Capitalize(exeRaw!));
        }
        else if (exeClean != null && folderClean != null)
        {
            var ce = NameMatcher.Compact(exeClean);
            var cf = NameMatcher.Compact(folderClean);
            bool exeFirst;
            if (ce == cf) exeFirst = false;                     // same name: folder usually keeps the real casing
            else if (cf.Contains(ce)) exeFirst = false;         // "Getting Over It" ⊂ "Getting Over It with Bennett Foddy"
            else if (ce.Contains(cf)) exeFirst = true;
            else exeFirst = exeRaw!.Contains(' ');              // "Doki Doki Literature Club Plus.exe" is human-named; "ttwu.exe" is not
            if (exeFirst) { Add(exeClean); Add(product); Add(folderClean); }
            else { Add(folderClean); Add(product); Add(exeClean); }
        }
        else
        {
            Add(product);
            Add(exeClean);
            Add(folderClean);
        }

        Add(shortcutClean);

        if (hints.Count == 0)
            hints.Add(Path.GetFileNameWithoutExtension(ctx.ShortcutFile ?? ctx.ExePath ?? "Jeu"));

        return (hints[0], hints.Take(5).ToList());
    }

    public static string? FindRomInArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        var ext = string.Join('|', RomExtensions);
        var quoted = Regex.Match(arguments, $"\"([^\"]+\\.(?:{ext}))\"", RegexOptions.IgnoreCase);
        if (quoted.Success) return quoted.Groups[1].Value;
        var bare = Regex.Match(arguments, $@"(\S+\.(?:{ext}))(?:\s|$)", RegexOptions.IgnoreCase);
        return bare.Success ? bare.Groups[1].Value : null;
    }

    public static string? ReadProductName(string? exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var name = string.IsNullOrWhiteSpace(info.ProductName) ? info.FileDescription : info.ProductName;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------------------------

    internal static string StripShortcutDecorations(string name)
    {
        var s = name.Trim();
        for (var pass = 0; pass < 3; pass++)
        {
            s = StripKnownExtension(s);
            s = CopySuffix().Replace(s, "");
            foreach (var suffix in ShortcutSuffixes)
                if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    s = s[..^suffix.Length].TrimEnd();
            s = s.Trim();
        }
        return s;
    }

    private static string StripKnownExtension(string s)
    {
        foreach (var ext in KnownExtensions)
            if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return s[..^ext.Length].TrimEnd();
        return s;
    }

    /// <summary>Core cleaning pipeline shared by file names, folder names and ROM names.</summary>
    public static string CleanCore(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var s = input.Trim();
        s = Trademarks().Replace(s, "");
        s = Brackets().Replace(s, " ");
        s = Versions().Replace(s, " ");
        s = VPrefixed().Replace(s, " ");
        s = BuildTag().Replace(s, " ");
        s = LongNumbers().Replace(s, " ");
        s = s.Replace('_', ' ');
        s = AcronymDots().Replace(s, m => m.Value.Replace(".", ""));
        s = s.Replace('.', ' ');
        s = SceneTags().Replace(s, " ");
        s = TechTags().Replace(s, " ");
        s = CamelSplit(s);

        // Hyphen clean-up: "TJoC- R" → "TJoC - R", "foo- -" → "foo"
        s = HyphenRuns().Replace(s, " - ");
        s = SpacedHyphen().Replace(s, " - ");
        s = MultiSpace().Replace(s, " ").Trim(' ', '-', '–', ',', '+', '&');
        for (var i = 0; i < 3; i++)
        {
            var before = s;
            s = TrailingNoise().Replace(s, "").Trim(' ', '-', '–', ',');
            if (s == before) break;
        }
        s = MultiSpace().Replace(s, " ").Trim();

        if (s.Length == 0) return StripShortcutDecorations(input).Trim();

        if (Aliases.TryGetValue(NameMatcher.Compact(s), out var alias)) return alias;
        return FixCase(s);
    }

    private static string? FolderCandidate(string exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            for (var depth = 0; depth < 5 && !string.IsNullOrEmpty(dir); depth++)
            {
                var name = Path.GetFileName(dir.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) break;               // drive root
                if (!IsGenericFolder(name))
                {
                    var cleaned = CleanCore(name);
                    if (cleaned.Length >= 2 && !IsGenericFolder(cleaned)) return cleaned;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { /* malformed path */ }
        return null;
    }

    private static bool IsGenericFolder(string name)
    {
        var n = name.Trim();
        if (GenericFolders.Contains(n)) return true;
        if (VersionFolder().IsMatch(n)) return true;
        return n.Length <= 1;
    }

    private static string? CleanProduct(string? product)
    {
        if (string.IsNullOrWhiteSpace(product)) return null;
        var lower = product.ToLowerInvariant();
        if (GenericProductWords.Any(w => lower.Contains(w))) return null;
        var cleaned = CleanCore(product);
        return cleaned.Length < 2 ? null : cleaned;
    }

    private static string CamelSplit(string s)
    {
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++) tokens[i] = SplitToken(tokens[i]);
        return string.Join(' ', tokens);
    }

    private static string SplitToken(string token)
    {
        if (token.Length < 4) return token;
        var x = LetterDigit().Replace(token, " ");
        x = DigitLetters().Replace(x, " ");
        var parts = x.Split(' ');
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (!p.Any(char.IsLower) || !p.Any(char.IsUpper)) continue;
            var candidate = CamelBoundary().Replace(p, " ");
            var pieces = candidate.Split(' ');
            if (pieces[0].Length >= 3 && pieces.All(piece => piece.Length >= 2)) parts[i] = candidate;
        }
        return string.Join(' ', parts);
    }

    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    { "of", "the", "and", "a", "an", "in", "on", "to", "for", "at", "by", "de", "du", "des", "le", "la", "les", "et", "no" };

    private static string FixCase(string s)
    {
        if (s.Any(char.IsUpper)) return s;
        var words = s.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0 && SmallWords.Contains(words[i])) continue;
            words[i] = Capitalize(words[i]);
        }
        return string.Join(' ', words);
    }

    private static string Capitalize(string w) =>
        w.Length == 0 ? w : char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..];

    // ------------------------------------------------------------------------------------
    // Regexes (source generated)
    // ------------------------------------------------------------------------------------

    [GeneratedRegex(@"[™®©]")] private static partial Regex Trademarks();
    [GeneratedRegex(@"\[[^\]]*\]|\{[^}]*\}|\([^)]*\)")] private static partial Regex Brackets();
    [GeneratedRegex(@"\s*\(\d+\)\s*$")] private static partial Regex CopySuffix();
    [GeneratedRegex(@"(?<![A-Za-z0-9])v?\d+(?:[._]\d+)+[a-z]?(?![A-Za-z0-9])", RegexOptions.IgnoreCase)] private static partial Regex Versions();
    [GeneratedRegex(@"(?<![A-Za-z0-9])v\d+[a-z]?(?![A-Za-z0-9])", RegexOptions.IgnoreCase)] private static partial Regex VPrefixed();
    [GeneratedRegex(@"(?<![A-Za-z0-9])build[\s._-]*\d+(?![A-Za-z0-9])", RegexOptions.IgnoreCase)] private static partial Regex BuildTag();
    [GeneratedRegex(@"(?<![A-Za-z0-9])\d{6,}(?![A-Za-z0-9])")] private static partial Regex LongNumbers();
    [GeneratedRegex(@"(?<![A-Za-z])(?:[A-Za-z]\.){2,}[A-Za-z]?(?![A-Za-z])")] private static partial Regex AcronymDots();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:p2p|codex|skidrow|plaza|flt|rune|tenoke|doge|elamigos|fitgirl|dodi|reloaded|hoodlum|prophet|cpy|empress|tinyiso|darksiders|razor1911|simplex|dinobytes|anomaly|repack|repacks|steamrip|igg|igggames|game3rb|ofme|gog|goldberg|online-fix|onlinefix|portable|multi\d*|eng|fr|nosteam|cracked|crack)(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex SceneTags();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:win64|win32|win10|win11|win|windows64|x64|x86|amd64|dx9|dx10|dx11|dx12|d3d9|d3d11|d3d12|vulkan|opengl|64bit|32bit|64-bit|32-bit|shipping|windowsnoeditor|beta|msvc|release|setup)(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex TechTags();

    [GeneratedRegex(@"\s+(?:launcher|full|client|player|beta|demo build|win|pc)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingNoise();

    [GeneratedRegex(@"(?:\s*-\s*){2,}")] private static partial Regex HyphenRuns();
    [GeneratedRegex(@"\s+-\s*|\s*-\s+")] private static partial Regex SpacedHyphen();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex MultiSpace();
    [GeneratedRegex(@"(?<=[A-Za-z]{2})(?=\d)")] private static partial Regex LetterDigit();
    [GeneratedRegex(@"(?<=\d)(?=[A-Za-z]{3,})")] private static partial Regex DigitLetters();
    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")] private static partial Regex CamelBoundary();
    [GeneratedRegex(@"^(?:version|ver|v)[-_ ]?[0-9a-f._-]+$", RegexOptions.IgnoreCase)] private static partial Regex VersionFolder();
}
