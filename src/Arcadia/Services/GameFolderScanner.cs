using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Arcadia.Models;

namespace Arcadia.Services;

/// <summary>What the header of an .exe says: window or console program, linked DLLs.</summary>
internal sealed class PeInfo
{
    public bool IsDll { get; private set; }
    public bool IsConsole { get; private set; }
    public bool IsDotNet { get; private set; }
    public HashSet<string> Imports { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the PE headers and the import table (a few KB, the file is never run). Null if not a valid exe.</summary>
    public static PeInfo? Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
            var dos = ReadAt(fs, 0, 64);
            if (dos.Length < 64 || dos[0] != 'M' || dos[1] != 'Z') return null;
            var peOffset = BitConverter.ToInt32(dos, 0x3C);
            var h = ReadAt(fs, peOffset, 4200);
            if (h.Length < 24 + 96 || h[0] != 'P' || h[1] != 'E' || h[2] != 0 || h[3] != 0) return null;

            int sections = U16(h, 6), optSize = U16(h, 20), characteristics = U16(h, 22);
            const int opt = 24;
            var magic = U16(h, opt);
            var plus = magic == 0x20B;
            if (magic != 0x10B && !plus) return null;
            var info = new PeInfo
            {
                IsDll = (characteristics & 0x2000) != 0,
                IsConsole = U16(h, opt + 68) == 3,
            };
            var dirCount = U32(h, opt + (plus ? 108 : 92));
            var dirs = opt + (plus ? 112 : 96);
            uint Dir(int i) => i < dirCount && dirs + i * 8 + 4 <= h.Length ? U32(h, dirs + i * 8) : 0;
            info.IsDotNet = Dir(14) != 0;

            var table = new List<(uint Va, uint Size, uint Raw)>();
            for (var i = 0; i < sections && opt + optSize + (i + 1) * 40 <= h.Length; i++)
            {
                var s = opt + optSize + i * 40;
                table.Add((U32(h, s + 12), Math.Max(U32(h, s + 8), U32(h, s + 16)), U32(h, s + 20)));
            }
            long Offset(uint rva)
            {
                foreach (var (va, size, raw) in table)
                    if (rva >= va && rva < va + size) return raw + (rva - va);
                return -1;
            }

            var imports = Offset(Dir(1));
            if (Dir(1) != 0 && imports >= 0)
            {
                var desc = ReadAt(fs, imports, 20 * 256);
                for (var i = 0; (i + 1) * 20 <= desc.Length; i++)
                {
                    if (desc.AsSpan(i * 20, 20).IndexOfAnyExcept((byte)0) < 0) break;
                    var nameAt = Offset(U32(desc, i * 20 + 12));
                    if (nameAt < 0) continue;
                    var raw = ReadAt(fs, nameAt, 64);
                    var end = Array.IndexOf(raw, (byte)0);
                    var name = Encoding.ASCII.GetString(raw, 0, end < 0 ? raw.Length : end);
                    if (name.Length > 0) info.Imports.Add(name);
                }
            }
            return info;
        }
        catch { return null; }
    }

    private static byte[] ReadAt(FileStream fs, long offset, int count)
    {
        if (offset < 0 || offset >= fs.Length) return Array.Empty<byte>();
        count = (int)Math.Min(count, fs.Length - offset);
        var buffer = new byte[count];
        fs.Position = offset;
        var read = fs.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
        return read == count ? buffer : buffer[..read];
    }

    private static int U16(byte[] b, int at) => at + 2 <= b.Length ? BitConverter.ToUInt16(b, at) : 0;
    private static uint U32(byte[] b, int at) => at + 4 <= b.Length ? BitConverter.ToUInt32(b, at) : 0;
}

/// <summary>One executable examined by the folder search, and why it does (not) look like a game.</summary>
internal sealed class ExeVerdict
{
    public required string Path { get; init; }
    public required string Dir { get; init; }
    public double Score { get; set; }
    public bool Excluded { get; set; }
    public string? Engine { get; set; }
    public string? Product { get; set; }
    public List<string> Reasons { get; } = new();
    public override string ToString() => $"{System.IO.Path.GetFileName(Path)} {(Excluded ? "EXCLU" : Score.ToString("0.#"))} [{string.Join(", ", Reasons)}]";
}

/// <summary>Files and sub-folders of one folder (read once).</summary>
internal sealed class DirFacts
{
    public required string Path { get; init; }
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Path, long Size)> Exes { get; } = new();
    public List<string> Shortcuts { get; } = new();
    public bool Has(string file) => Files.Contains(file);
    public bool Any(Func<string, bool> test) => Files.Any(test);
}

public sealed class FolderScanResult
{
    public List<DetectedGame> Games { get; } = new();
    public int ExecutablesSeen { get; set; }
    public int FoldersSeen { get; set; }
}

/// <summary>
/// Finds the games inside a messy folder: walks the sub-folders, recognises the engine or the files that come with
/// games (Unity, Unreal, GameMaker, Godot, Ren'Py, RPG Maker, Steam / GOG DLLs, DirectX…), sets aside installers,
/// tools and ordinary software, and keeps one executable per game. Read-only: nothing is launched or modified.
/// </summary>
public static partial class GameFolderScanner
{
    /// <summary>From this score the game is proposed checked; between <see cref="MaybeScore"/> and this, unchecked.</summary>
    internal const double GameScore = 5;
    internal const double MaybeScore = 3;
    private const int MaxDepth = 8;
    private const int MaxFolders = 60_000;

    public static FolderScanResult Scan(string root, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        root = System.IO.Path.GetFullPath(root).TrimEnd('\\');
        if (root.EndsWith(':')) root += "\\";
        var result = new FolderScanResult();
        var facts = Walk(root, progress, ct);
        result.FoldersSeen = facts.Count;

        // 1. every executable gets a score
        var verdicts = new List<ExeVerdict>();
        foreach (var dir in facts.Values.ToList())
            foreach (var (exe, size) in dir.Exes)
            {
                ct.ThrowIfCancellationRequested();
                result.ExecutablesSeen++;
                verdicts.Add(Judge(exe, size, root, facts));
            }
        var kept = verdicts.Where(v => !v.Excluded && v.Score >= MaybeScore).ToList();
        Log.Info($"Folder search in {root}: {facts.Count} folders, {verdicts.Count} exe, {kept.Count} possible game(s)");

        // 2. one game per folder (an Unreal game has 3 exe, a game can have x86 and x64 copies…)
        var found = new List<(string? InstallDir, ExeVerdict Best)>();
        Partition(root, root, kept, isRoot: true, root, found);
        foreach (var (installDir, best) in found)
        {
            var (display, hints) = NameCleaner.Suggest(new NameContext { ExePath = best.Path, ProductName = best.Product });
            if (installDir != null && !string.Equals(installDir, root, StringComparison.OrdinalIgnoreCase))
            {
                var folder = NameCleaner.CleanCore(System.IO.Path.GetFileName(installDir));
                if (folder.Length >= 2 && !hints.Any(h => NameMatcher.Compact(h) == NameMatcher.Compact(folder))) hints.Add(folder);
            }
            var game = new Game { Name = display, SourceKind = GameSourceKind.Executable, LaunchTarget = best.Path, TargetPath = best.Path };
            result.Games.Add(new DetectedGame
            {
                Name = display,
                Hints = hints,
                Store = StoreScanner.Detect(game),
                LaunchTarget = best.Path,
                WorkingDirectory = best.Dir,
                InstallDir = installDir,
                Executable = best.Path,
                Note = best.Engine,
                Uncertain = best.Score < GameScore,
                Score = best.Score,
            });
        }

        // 3. shortcuts lying in the folder (a folder of shortcuts keeps working like before)
        var gameDirs = found.Where(f => f.InstallDir != null).Select(f => f.InstallDir!.TrimEnd('\\') + "\\").ToList();
        var exes = found.Select(f => f.Best.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in facts.Values.SelectMany(d => d.Shortcuts).ToList())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (ShortcutGame(file, root, facts, gameDirs, exes) is { } shortcut) result.Games.Add(shortcut);
            }
            catch (Exception ex) { Log.Warn($"Folder search: shortcut {file} ignored ({ex.Message})"); }
        }

        // the same shortcut copied twice, or a shortcut and its game
        var unique = result.Games.DistinctBy(g => (g.Executable ?? g.LaunchTarget).ToLowerInvariant()).ToList();
        result.Games.Clear();
        result.Games.AddRange(unique.OrderBy(g => g.Uncertain).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
        return result;
    }

    // ------------------------------------------------------------------------------------
    // Walking the folders
    // ------------------------------------------------------------------------------------

    private static Dictionary<string, DirFacts> Walk(string root, IProgress<string>? progress, CancellationToken ct)
    {
        var all = new Dictionary<string, DirFacts>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        var clock = Stopwatch.StartNew();
        while (queue.Count > 0 && all.Count < MaxFolders)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = queue.Dequeue();
            var facts = new DirFacts { Path = dir };
            all[dir] = facts;
            if (clock.ElapsedMilliseconds > 120)
            {
                progress?.Report(dir);
                clock.Restart();
            }
            try
            {
                var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
                {
                    if (entry is DirectoryInfo sub)
                    {
                        facts.Dirs.Add(sub.Name);
                        if (depth < MaxDepth && !sub.Attributes.HasFlag(FileAttributes.ReparsePoint) && !SkipFolder(sub.Name, depth))
                            queue.Enqueue((sub.FullName, depth + 1));
                    }
                    else if (entry is FileInfo file)
                    {
                        facts.Files.Add(file.Name);
                        var ext = file.Extension.ToLowerInvariant();
                        if (ext == ".exe") facts.Exes.Add((file.FullName, file.Length));
                        else if (ext is ".lnk" or ".url" && depth <= 3) facts.Shortcuts.Add(file.FullName);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return all;
    }

    private static DirFacts? FactsOf(string dir, Dictionary<string, DirFacts> cache)
    {
        if (cache.TryGetValue(dir, out var known)) return known;
        if (!Directory.Exists(dir)) return null;
        var facts = new DirFacts { Path = dir };
        try
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true }))
                if (entry is DirectoryInfo d) facts.Dirs.Add(d.Name);
                else facts.Files.Add(entry.Name);
        }
        catch { /* unreadable: no markers */ }
        cache[dir] = facts;
        return facts;
    }

    /// <summary>Folders that never hold the game itself: redistributables, anti-cheat, the engine's tools, Steam libraries…</summary>
    private static bool SkipFolder(string name, int depth)
    {
        if (SkippedFolders.Contains(name)) return true;
        return depth == 0 && RootOnlySkipped.Contains(name);
    }

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "Windows", "WindowsApps", "steamapps", "node_modules", ".git", ".svn",
        "_CommonRedist", "CommonRedist", "Redist", "_Redist", "Redistributables", "Redistributable", "__Installer", "Installer",
        "Installers", "DirectX", "DXSETUP", "vcredist", "dotnet", "dotNetFx", "Prerequisites", "Prereqs", "_Prereqs",
        "PrereqSetup", "Support", "EasyAntiCheat", "EasyAntiCheat_EOS", "BattlEye", "Engine", "CrashReporter", "Crashpad",
        "ThirdParty", "__pycache__", "Soundtrack", "OST", "Artbook", "Manuals", "Documentation", "MonoBleedingEdge",
    };

    private static readonly HashSet<string> RootOnlySkipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProgramData", "AppData", "PerfLogs", "Recovery", "MSOCache", "$WINDOWS.~BT", "$WINDOWS.~WS", "Intel", "AMD", "NVIDIA",
    };

    // ------------------------------------------------------------------------------------
    // Is this exe a game?
    // ------------------------------------------------------------------------------------

    internal static ExeVerdict Judge(string exe, long size, string root, Dictionary<string, DirFacts> cache)
    {
        var dir = System.IO.Path.GetDirectoryName(exe)!;
        var v = new ExeVerdict { Path = exe, Dir = dir };
        var name = System.IO.Path.GetFileNameWithoutExtension(exe);
        var factor = 1.0;
        void Add(double points, string why)
        {
            points *= factor;
            v.Score += points;
            v.Reasons.Add($"{why} {points:+0.#;-0.#}");
        }
        bool Exclude(string why) { v.Excluded = true; v.Reasons.Add(why); return true; }

        if (ImportService.IsJunkExe(exe) || JunkName().IsMatch(name)) { Exclude("nom d'outil ou d'installeur"); return v; }
        if (Launchers().IsMatch(name) || StoreScanner.EmulatorNames.Contains(name, StringComparer.OrdinalIgnoreCase)) { Exclude("launcher ou émulateur"); return v; }

        var pe = PeInfo.Read(exe);
        if (pe is { IsDll: true }) { Exclude("bibliothèque"); return v; }

        var facts = FactsOf(dir, cache) ?? new DirFacts { Path = dir };
        DirFacts? Child(string sub) => facts.Dirs.Contains(sub) ? FactsOf(System.IO.Path.Combine(dir, sub), cache) : null;

        // Loose exe at the root of the search, next to other ones: the files of the folder belong to whichever of
        // them is a game (a data.win at the root says nothing about "Remove-Edge.exe" lying beside it). They are
        // credited in full to an exe that draws with DirectX / OpenGL / Vulkan itself, by half to another window
        // program, not at all to a console tool.
        var draws = pe != null && pe.Imports.Any(i => GraphicsDlls().IsMatch(i));
        var shared = draws || !string.Equals(dir, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                     || facts.Exes.Count(e => !IsJunk(e.Path)) <= 1;
        var folderWeight = shared ? 1.0 : pe is { IsConsole: false } ? 0.5 : 0;

        // ---- the engine (exe-specific when possible) ----
        if (facts.Dirs.Contains(name + "_Data")) { Add(7, "Unity"); v.Engine = "Unity"; }
        else if (folderWeight > 0 && facts.Has("UnityPlayer.dll")) { factor = folderWeight; Add(4, "Unity"); factor = 1; v.Engine = "Unity"; }
        bool HasBinaries(string d) => FactsOf(System.IO.Path.Combine(dir, d), cache)?.Dirs.Contains("Binaries") == true;
        if (ShippingExe().IsMatch(name)) { Add(6, "Unreal"); v.Engine = "Unreal Engine"; }
        else if (facts.Dirs.Contains("Engine")
                 && (shared ? HasBinaries("Engine") || facts.Dirs.Any(HasBinaries)
                            : facts.Dirs.Contains(name) && (HasBinaries(name) || HasBinaries("Engine")))) // at the root: "Game.exe" next to its "Game" folder
        { Add(6, "Unreal"); v.Engine = "Unreal Engine"; }
        if (facts.Has(name + ".pck")) { Add(7, "Godot"); v.Engine = "Godot"; }
        factor = folderWeight;
        if (folderWeight > 0)
        {
            if (facts.Has("data.win")) { Add(7, "GameMaker"); v.Engine = "GameMaker"; }
            if (facts.Dirs.Contains("renpy")) { Add(7, "Ren'Py"); v.Engine = "Ren'Py"; }
            if (facts.Has("Game.ini") && (facts.Any(f => f.StartsWith("RGSS", StringComparison.OrdinalIgnoreCase))
                                           || Child("System")?.Any(f => f.StartsWith("RGSS", StringComparison.OrdinalIgnoreCase)) == true))
            { Add(7, "RPG Maker"); v.Engine = "RPG Maker"; }
            if ((facts.Dirs.Contains("www") || facts.Has("package.json")) && (facts.Has("nw.dll") || facts.Has("nw_elf.dll")))
            { Add(6, "RPG Maker MV/MZ"); v.Engine = "RPG Maker"; }
            if (facts.Has("FNA.dll") || facts.Has("MonoGame.Framework.dll") || facts.Any(f => f.StartsWith("Microsoft.Xna.Framework", StringComparison.OrdinalIgnoreCase)))
            { Add(6, "XNA / MonoGame"); v.Engine = "MonoGame"; }
            if (facts.Has("love.dll")) { Add(6, "LÖVE"); v.Engine = "LÖVE"; }

            // ---- shop and game SDKs, next to the exe or a level or two above (bin\x64\game.exe) ----
            var chain = new List<DirFacts> { facts };
            var up = System.IO.Path.GetDirectoryName(dir);
            for (var i = 0; i < 2 && up != null && up.Length >= root.Length && up.StartsWith(root, StringComparison.OrdinalIgnoreCase); i++)
            {
                if (FactsOf(up, cache) is { } f) chain.Add(f);
                up = System.IO.Path.GetDirectoryName(up);
            }
            string? store = null;
            if (chain.Any(f => f.Has("steam_api.dll") || f.Has("steam_api64.dll") || f.Has("steam_appid.txt"))) store = "Steam";
            else if (chain.Any(f => f.Any(n => n.StartsWith("goggame-", StringComparison.OrdinalIgnoreCase)) || f.Has("Galaxy.dll") || f.Has("Galaxy64.dll"))) store = "GOG";
            else if (chain.Any(f => f.Any(n => n.StartsWith("EOSSDK-", StringComparison.OrdinalIgnoreCase)))) store = "Epic";
            if (store != null) { Add(4, "SDK " + store); v.Engine ??= store; }

            var middleware = facts.Files.Count(f => Middleware().IsMatch(f));
            if (middleware > 0) Add(Math.Min(4, 2 * middleware), "DLL de jeu");
            if (facts.Any(IsGameData) || facts.Dirs.Any(d => DataFolders.Contains(d) && FactsOf(System.IO.Path.Combine(dir, d), cache)?.Any(IsGameData) == true))
                Add(2, "archives de jeu");
        }
        factor = 1;

        // ---- what the exe links to ----
        if (pe != null)
        {
            if (pe.IsConsole) Add(-3, "programme console");
            if (pe.Imports.Any(i => GraphicsDlls().IsMatch(i))) Add(3, "DirectX / OpenGL / Vulkan");
            var io = pe.Imports.Count(i => InputAudioDlls().IsMatch(i));
            if (io > 0) Add(Math.Min(2, io), "manette / son");
            if (pe.Imports.Any(i => i.StartsWith("steam_api", StringComparison.OrdinalIgnoreCase) || i.StartsWith("galaxy", StringComparison.OrdinalIgnoreCase)
                                    || i.StartsWith("EOSSDK", StringComparison.OrdinalIgnoreCase)))
                Add(2, "lié à Steam / GOG / Epic");
            if (pe.Imports.Contains("UnityPlayer.dll")) { Add(3, "lanceur Unity"); v.Engine ??= "Unity"; }
        }

        // ---- what the exe says about itself ----
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            v.Product = string.IsNullOrWhiteSpace(info.ProductName) ? info.FileDescription?.Trim() : info.ProductName.Trim();
            var text = $"{info.FileDescription} {info.ProductName}";
            if (InstallerText().IsMatch(text)) { Exclude("installeur"); return v; }
            if (SoftwareText().IsMatch(text)) Add(-4, "description de logiciel");
            var company = info.CompanyName?.Trim() ?? "";
            if (SoftwarePublishers().IsMatch(company)) Add(-5, "éditeur de logiciels");
            else if (company.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) Add(-2, "Microsoft");
        }
        catch { /* no version resource */ }

        // ---- the folder looks like an application ----
        if (v.Engine == null || v.Engine is "Steam" or "GOG" or "Epic")
        {
            if (Child("resources")?.Has("app.asar") == true) Add(-5, "application Electron");
            if (facts.Has("Qt5Core.dll") || facts.Has("Qt6Core.dll")) Add(-3, "application Qt");
            if (facts.Has("libcef.dll")) Add(-1, "navigateur intégré");
            if (facts.Any(f => f.StartsWith("python3", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) Add(-2, "Python");
        }

        // ---- the exe has the name of its folder (jeux\Hollow Knight\hollow_knight.exe) ----
        var top = TopFolder(dir, root);
        var folders = new[] { System.IO.Path.GetFileName(dir), top };
        if (NameMatches(name, folders) || v.Product != null && NameMatches(v.Product, folders)) Add(2, "nom du dossier");
        if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase)) Add(-1, "launcher");
        if (size > 0 && size < 40_000) Add(-2, "très petit");

        // tie-breaks: the 64-bit copy, then the one closest to the game folder
        if (X86().IsMatch(exe)) v.Score -= 0.5;
        else if (X64().IsMatch(exe)) v.Score += 0.5;
        return v;
    }

    private static bool IsJunk(string exe)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(exe);
        return ImportService.IsJunkExe(exe) || JunkName().IsMatch(name) || Launchers().IsMatch(name);
    }

    private static bool IsGameData(string file) => GameDataExtensions.Contains(System.IO.Path.GetExtension(file));

    private static readonly HashSet<string> GameDataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pak", ".bsa", ".ba2", ".vpk", ".big", ".rpf", ".forge", ".assets", ".wad", ".bnk", ".wem", ".ucas", ".utoc", ".rpa",
        ".cpk", ".xp3", ".gcf", ".pck", ".bundle", ".esm", ".dz", ".arc",
    };

    private static readonly HashSet<string> DataFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data", "Content", "Paks", "game", "Assets", "Resources", "Archives", "Packs", "base",
    };

    private static string? TopFolder(string dir, string root)
    {
        if (dir.Length <= root.Length || !dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        var rel = dir[root.Length..].TrimStart('\\');
        var cut = rel.IndexOf('\\');
        return cut < 0 ? rel : rel[..cut];
    }

    private static bool NameMatches(string exeName, IEnumerable<string?> others)
    {
        var e = NameMatcher.Compact(NameCleaner.CleanFileName(exeName));
        if (e.Length < 3) return false;
        foreach (var other in others)
        {
            if (string.IsNullOrWhiteSpace(other)) continue;
            var o = NameMatcher.Compact(NameCleaner.CleanCore(other));
            if (o.Length >= 3 && (o.Contains(e) || e.Contains(o))) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------------------------
    // One game per folder
    // ------------------------------------------------------------------------------------

    private static void Partition(string dir, string top, List<ExeVerdict> exes, bool isRoot, string root, List<(string?, ExeVerdict)> found)
    {
        if (exes.Count == 0) return;
        if (isRoot)
        {
            // loose exe at the root: each one is its own game (and has no folder of its own)
            var loose = exes.Where(e => string.Equals(e.Dir, dir, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var exe in loose)
                found.Add((null, exe));
            // an Unreal game unpacked right at the root: "Game.exe" + "Game\Binaries\Win64\Game-Win64-Shipping.exe" is one game
            var stubs = loose.Select(e => System.IO.Path.GetFileNameWithoutExtension(e.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var group in exes.Where(e => !string.Equals(e.Dir, dir, StringComparison.OrdinalIgnoreCase)).GroupBy(e => ChildOf(dir, e.Dir), StringComparer.OrdinalIgnoreCase))
                if (!stubs.Contains(System.IO.Path.GetFileName(group.Key)))
                    Partition(group.Key, group.Key, group.ToList(), false, root, found);
            return;
        }

        if (exes.Any(e => string.Equals(e.Dir, dir, StringComparison.OrdinalIgnoreCase)))
        {
            found.Add((top, Best(exes, top)));
            return;
        }
        var children = exes.GroupBy(e => ChildOf(dir, e.Dir), StringComparer.OrdinalIgnoreCase).ToList();
        if (children.Count == 1)
        {
            // "The Witcher 3\bin\x64": the game folder stays "The Witcher 3"; "Action\Stray": the game folder is "Stray"
            var only = children[0].Key;
            Partition(only, InnerFolder().IsMatch(System.IO.Path.GetFileName(only)) ? top : only, exes, false, root, found);
            return;
        }
        if (SameGame(children.Select(c => (System.IO.Path.GetFileName(c.Key), Best(c.ToList(), c.Key))).ToList()))
        {
            found.Add((top, Best(exes, top)));
            return;
        }
        foreach (var child in children)
            Partition(child.Key, child.Key, child.ToList(), false, root, found);
    }

    private static string ChildOf(string parent, string descendant)
    {
        var rel = descendant[(parent.TrimEnd('\\').Length + 1)..];
        var cut = rel.IndexOf('\\');
        return System.IO.Path.Combine(parent, cut < 0 ? rel : rel[..cut]);
    }

    /// <summary>The exe to launch: the best score, a little in favour of the one at the top of the game folder.</summary>
    private static ExeVerdict Best(List<ExeVerdict> exes, string top) =>
        exes.MaxBy(e => e.Score - 0.75 * Depth(e.Dir, top))!;

    private static int Depth(string dir, string top) =>
        dir.Length <= top.Length ? 0 : dir[top.Length..].Count(c => c == '\\');

    /// <summary>bin\x64 and bin\x86, or two copies of the same exe: one game, not two.</summary>
    private static bool SameGame(List<(string Folder, ExeVerdict Best)> branches)
    {
        if (branches.All(b => ArchFolder().IsMatch(b.Folder))) return true;
        var names = branches.Select(b => NameMatcher.Compact(ArchSuffix().Replace(System.IO.Path.GetFileNameWithoutExtension(b.Best.Path), ""))).Distinct().Count();
        if (names == 1) return true;
        var products = branches.Select(b => NameMatcher.Compact(b.Best.Product)).ToList();
        return products.All(p => p.Length >= 3) && products.Distinct().Count() == 1;
    }

    // ------------------------------------------------------------------------------------
    // Shortcuts
    // ------------------------------------------------------------------------------------

    private static DetectedGame? ShortcutGame(string file, string root, Dictionary<string, DirFacts> cache, List<string> gameDirs, HashSet<string> exes)
    {
        string? target = null, args = null, workDir = null, url = null;
        if (file.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            var info = ShortcutResolver.ReadUrl(file);
            if (info == null || info.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null; // a web bookmark
            url = info.Url;
        }
        else
        {
            var info = ShortcutResolver.ReadLnk(file);
            if (info == null) return null;
            target = info.TargetPath;
            args = info.Arguments;
            workDir = info.WorkingDirectory;
        }
        if (ImportService.IsJunkShortcut(file, target)) return null;

        var uncertain = false;
        string? engine = null;
        if (target != null && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (exes.Contains(target) || gameDirs.Any(d => target.StartsWith(d, StringComparison.OrdinalIgnoreCase))) return null; // already found
            var isEmulated = NameCleaner.FindRomInArguments(args) != null;
            if (File.Exists(target) && !isEmulated)
            {
                var targetDir = System.IO.Path.GetDirectoryName(target)!;
                var v = Judge(target, new FileInfo(target).Length, System.IO.Path.GetDirectoryName(targetDir) ?? targetDir, cache);
                if (v.Excluded || v.Score < MaybeScore) return null; // a shortcut to a program, not a game
                uncertain = v.Score < GameScore;
                engine = v.Engine;
            }
        }

        var name = NameCleaner.CleanFileName(file);
        return new DetectedGame
        {
            Name = name,
            Store = GameStore.Local,
            LaunchTarget = url ?? target ?? file,
            Arguments = args,
            WorkingDirectory = workDir,
            Executable = target,
            ShortcutFile = file,
            Note = engine ?? "Raccourci",
            Uncertain = uncertain,
        };
    }

    // ------------------------------------------------------------------------------------
    // Patterns
    // ------------------------------------------------------------------------------------

    [GeneratedRegex(@"(?:^|[ _\-.])(setup|install|installer|uninstall|unins\d+|redist|vcredist|crash|report(er)?|updater?|patcher|helper|service|server|dedicated|config|configurator|configtool|settings|options|editor|benchmark|elevate|cleanup|repair|diagnostics?|dxdiag|activation|register|sdk|tools?|toolkit|modmanager|cefprocess|subprocess|webhelper|overlay|touchup|dowser|uplayservice|eac|prereq\w*)(?:$|[ _\-.\d])|^(7z\w*|winrar|unrar|unzip|python\w*|pythonw|java|javaw|node|cmd|powershell|steamcmd|vortex|modorganizer|wine\w*|dosbox|dxwebsetup|oalinst|vc_redist\w*|regsvr32)$|crash|uninst|redist|install|setup|updater|patcher|benchmark|dedicatedserver|configtool|webhelper|subprocess|cefprocess|reportclient|ldplayer|bluestacks|(?:^|[ _\-.])patch(?:$|[ _\-.\d])|^(config|settings)",
        RegexOptions.IgnoreCase)]
    private static partial Regex JunkName();

    [GeneratedRegex(@"^(steam|steamwebhelper|steamservice|epicgameslauncher|epicwebhelper|galaxyclient|goggalaxy|upc|ubisoftconnect|uplay|eadesktop|origin|battle\.net|battlenet|riotclientservices|riotclient|xboxpcapp|gamebar|playnite\.desktopapp|playnite\.fullscreenapp|launchbox|bigbox|heroic|lutris|itch|amazongames|arcadia|discord|parsec|moonlight|sunshine|obs32|obs64|retroarch)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Launchers();

    [GeneratedRegex(@"-(Win64|Win32|WinGDK|WinGame)?-?Shipping$", RegexOptions.IgnoreCase)]
    private static partial Regex ShippingExe();

    [GeneratedRegex(@"^(bink\w*|fmod\w*|openal32|xinput1_\d|d3dx9_\d+|d3dx1\d_\d+|d3dcompiler_4[0-3]|physx\w*|nvngx_dlss\w*|sl\.interposer|amd_fidelityfx\w*|ffx_\w*|sdl[23]|discord_game_sdk|oo2core_\w*|granny2\w*|mss(32|64)|xaudio2_\d|gfsdk\w*|libscepad|aksoundengine\w*|wwise\w*|havok\w*|steam_api(64)?|galaxy(64)?|eossdk-\w+)\.dll$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Middleware();

    [GeneratedRegex(@"^(d3d8|d3d9|d3d10(_1)?|d3d11|d3d12|dxgi|ddraw|opengl32|vulkan-1)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex GraphicsDlls();

    [GeneratedRegex(@"^(xinput\w*|dinput8?|dsound|xaudio2_\w+|x3daudio\w*|openal32|fmod\w*|sdl[23]|bink\w*)\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex InputAudioDlls();

    [GeneratedRegex(@"\b(setup|installer|installation|install|uninstall\w*|désinstall\w*|self-extract\w*|sfx|redistributable|runtime)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerText();

    [GeneratedRegex(@"\b(browser|navigateur|media player|video player|music player|lecteur multimédia|editor|éditeur|viewer|visionneuse|driver|pilote|utility|utilitaire|antivirus|backup|sauvegarde|recorder|enregistreur|converter|convertisseur|compressor|archiver|torrent|downloader|messenger|messagerie|terminal|command line|ligne de commande|compiler|debugger|office|spreadsheet|pdf|vpn|monitoring|overclock\w*|control panel|panneau de configuration|mod manager|launcher service)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SoftwareText();

    [GeneratedRegex(@"^(google|mozilla|adobe|oracle|nvidia|intel|advanced micro devices|amd\b|realtek|logitech|razer|corsair|steelseries|igor pavlov|win\.rar|alexander roshal|videolan|python software|jetbrains|discord|spotify|obs project|blender foundation|github|electron|node\.js|openjs|docker|zoom|dropbox|teamviewer|anydesk|malwarebytes|avast|avg\b|kaspersky|bitdefender|eset|norton|gen digital|mcafee|apple|brave|opera|vivaldi|cpuid|techpowerup|micro-star|msi\b|asustek|asus\b|gigabyte|elgato|wacom|hp\b|hewlett|dell|lenovo|canon|seiko epson|qualcomm|samsung|western digital|akeo|qbittorrent|bittorrent|the wireshark|don ho|notepad\+\+|audacity|the gimp|krita|inkscape|handbrake|overwolf|medal|parsec|black tree gaming|nexus mods|unity technologies|tim kosse|dominik reichl|simon tatham|igor|piriform|ccleaner|iobit|glary|winzip|corel|autodesk|cyberlink|nero|sony creative|vmware|oracle|citrix|cisco|slack|zoom video|telegram|whatsapp|signal messenger|notion|figma|postman)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SoftwarePublishers();

    [GeneratedRegex(@"\\(x86|win32|bin32|32bit|i386)\\|[-_ ](x86|32|32bit)\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex X86();

    [GeneratedRegex(@"\\(x64|win64|bin64|64bit|amd64|x86_64)\\|[-_ ](x64|64|64bit)\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex X64();

    [GeneratedRegex(@"^(x64|x86|x86_64|win32|win64|bin32|bin64|32bit|64bit|32|64|amd64|i386)$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchFolder();

    [GeneratedRegex(@"[-_ ]?(x64|x86|32|64|32bit|64bit|win32|win64)$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchSuffix();

    /// <summary>Folders inside a game (never the game's own folder).</summary>
    [GeneratedRegex(@"^(bin|bin32|bin64|binaries|x64|x86|x86_64|amd64|win32|win64|windows|windowsnoeditor|win|pc|game|program|app|retail|release|shipping|build|system|exe|executable)$", RegexOptions.IgnoreCase)]
    private static partial Regex InnerFolder();
}
