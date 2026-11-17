using System.Text.RegularExpressions;
using System.Windows.Threading;
using Arcadia.Models;

namespace Arcadia.Services;

public sealed class ImportReport
{
    public List<Game> Added { get; } = new();
    public int Duplicates { get; set; }
    public int Ignored { get; set; }
    public List<string> Errors { get; } = new();
}

/// <summary>Result of analysing one file (before deduplication).</summary>
internal sealed class ImportCandidate
{
    public GameSourceKind Kind { get; init; }
    public string SourcePath { get; init; } = "";
    public string LaunchTarget { get; set; } = "";
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? TargetPath { get; init; }
    public int? SteamAppId { get; init; }
    public bool Advertised { get; init; }
    public string? IconSource { get; init; }
    public string Display { get; init; } = "";
    public List<string> Hints { get; init; } = new();
}

/// <summary>
/// Imports .exe / .lnk / .url files and folders. Original files are only read:
/// shortcuts are copied into the data folder so the library keeps working if the desktop is cleaned up.
/// </summary>
public sealed partial class ImportService
{
    private readonly LibraryService _library;
    private readonly MetadataService _metadata;
    private readonly AppSettings _settings;
    private readonly Dispatcher _ui;

    public ImportService(LibraryService library, MetadataService metadata, AppSettings settings, Dispatcher ui)
    {
        _library = library;
        _metadata = metadata;
        _settings = settings;
        _ui = ui;
    }

    public async Task<ImportReport> ImportAsync(IEnumerable<string> paths)
    {
        var input = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var report = new ImportReport();
        var candidates = await Sta.Run(() => Analyze(input, report));

        var keys = new HashSet<string>(_library.Games.Select(DedupKey), StringComparer.OrdinalIgnoreCase);
        var appIds = _library.Games.Where(g => g.SteamAppId != null).Select(g => g.SteamAppId!.Value).ToHashSet();
        var added = new List<(Game Game, string? Icon)>();

        foreach (var c in candidates)
        {
            var key = DedupKey(c.Kind, c.LaunchTarget, c.TargetPath, c.Arguments, c.SourcePath, c.Advertised);
            if (!keys.Add(key) || (c.SteamAppId is int appId && !appIds.Add(appId)))
            {
                report.Duplicates++;
                continue;
            }

            var game = new Game
            {
                Name = c.Display,
                NameHints = c.Hints,
                SourceKind = c.Kind,
                SourcePath = c.SourcePath,
                LaunchTarget = c.LaunchTarget,
                Arguments = c.Arguments,
                WorkingDirectory = c.WorkingDirectory,
                TargetPath = c.TargetPath,
                SteamAppId = c.SteamAppId,
                AddedAt = DateTime.Now,
                MetadataState = MetadataState.Pending,
            };
            game.Store = StoreScanner.Detect(game);

            if (c.Kind != GameSourceKind.Executable)
            {
                try
                {
                    var copy = Path.Combine(AppPaths.ShortcutsDir, game.Id + Path.GetExtension(c.SourcePath).ToLowerInvariant());
                    File.Copy(c.SourcePath, copy, overwrite: true);
                    game.ShortcutCopy = AppPaths.ToRelative(copy);
                    if (c.Advertised) game.LaunchTarget = copy; // Store / advertised shortcuts can only be started through the .lnk itself
                }
                catch (Exception ex) { Log.Warn($"Could not copy shortcut {c.SourcePath}: {ex.Message}"); }
            }

            _library.Add(game);
            report.Added.Add(game);
            added.Add((game, c.IconSource));
        }

        if (added.Count > 0)
        {
            _library.SaveNow();
            _ = ExtractIconsAsync(added);
            foreach (var (game, _) in added)
            {
                if (_settings.AutoFetchMetadata) _metadata.Enqueue(game);
                else game.MetadataState = MetadataState.NotFound;
            }
        }

        Log.Info($"Import: {report.Added.Count} added, {report.Duplicates} duplicates, {report.Ignored} ignored, {report.Errors.Count} errors");
        return report;
    }

    /// <summary>True when a game found by the store scan is already in the library.</summary>
    public bool IsInLibrary(DetectedGame d)
    {
        foreach (var g in _library.Games)
        {
            if (d.SteamAppId != null && g.SteamAppId == d.SteamAppId) return true;
            if (d.StoreId != null && string.Equals(g.StoreId, d.StoreId, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(g.LaunchTarget, d.LaunchTarget, StringComparison.OrdinalIgnoreCase)) return true;
            if (d.Executable != null && string.Equals(g.TargetPath ?? g.LaunchTarget, d.Executable, StringComparison.OrdinalIgnoreCase)) return true;
            if (d.InstallDir != null && g.TargetPath != null && g.TargetPath.StartsWith(d.InstallDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            if (NameMatcher.Compact(g.Name) == NameMatcher.Compact(d.Name) && (g.Store == d.Store || g.SourceKind == GameSourceKind.Shortcut && string.IsNullOrWhiteSpace(g.TargetPath))) return true;
        }
        return false;
    }

    /// <summary>Adds games detected in store launchers (Steam, Epic, GOG, Ubisoft, Xbox).</summary>
    public async Task<List<Game>> ImportDetectedAsync(IEnumerable<DetectedGame> detected)
    {
        var added = new List<(Game, string?)>();
        foreach (var d in detected)
        {
            if (IsInLibrary(d)) continue;
            var isExe = !LaunchHelper.IsUri(d.LaunchTarget);
            var game = new Game
            {
                Name = d.Name,
                NameHints = new List<string> { d.Name },
                SourceKind = isExe ? GameSourceKind.Executable : GameSourceKind.InternetShortcut,
                SourcePath = d.Executable ?? d.LaunchTarget,
                LaunchTarget = d.LaunchTarget,
                Arguments = d.Arguments,
                WorkingDirectory = d.WorkingDirectory,
                TargetPath = d.Executable,
                SteamAppId = d.SteamAppId,
                Store = d.Store,
                StoreId = d.StoreId,
                InstallDir = d.InstallDir,
                AddedAt = DateTime.Now,
                MetadataState = MetadataState.Pending,
            };
            _library.Add(game);
            added.Add((game, d.Executable != null && File.Exists(d.Executable) ? d.Executable : null));
        }
        if (added.Count == 0) return new List<Game>();
        _library.SaveNow();
        await ExtractIconsAsync(added.Where(a => a.Item2 != null).ToList());
        foreach (var (game, _) in added)
            if (_settings.AutoFetchMetadata) _metadata.Enqueue(game);
            else game.MetadataState = MetadataState.NotFound;
        Log.Info($"Store import: {added.Count} game(s) added");
        return added.Select(a => a.Item1).ToList();
    }

    /// <summary>Points an existing game to a new file (used by "Localiser" and the edit dialog).</summary>
    public async Task RelocateAsync(Game game, string path)
    {
        var report = new ImportReport();
        var c = (await Sta.Run(() => Analyze(new List<string> { path }, report, explicitFiles: true))).FirstOrDefault()
                ?? throw new InvalidOperationException("Ce fichier ne peut pas être utilisé pour lancer un jeu.");

        game.SourceKind = c.Kind;
        game.SourcePath = c.SourcePath;
        game.LaunchTarget = c.LaunchTarget;
        game.Arguments = c.Arguments;
        game.WorkingDirectory = c.WorkingDirectory;
        game.TargetPath = c.TargetPath;
        if (c.SteamAppId != null) game.SteamAppId = c.SteamAppId;

        if (c.Kind != GameSourceKind.Executable)
        {
            try
            {
                AppPaths.TryDeleteManaged(game.ShortcutCopy);
                var copy = Path.Combine(AppPaths.ShortcutsDir, $"{game.Id}_{DateTime.UtcNow.Ticks:x}{Path.GetExtension(c.SourcePath).ToLowerInvariant()}");
                File.Copy(c.SourcePath, copy, overwrite: true);
                game.ShortcutCopy = AppPaths.ToRelative(copy);
                if (c.Advertised) game.LaunchTarget = copy;
            }
            catch (Exception ex) { Log.Warn($"Could not copy shortcut {c.SourcePath}: {ex.Message}"); }
        }

        game.IsMissing = !GameLauncher.TargetExists(game);
        if (game.Icon == null) await ExtractIconsAsync(new List<(Game, string?)> { (game, c.IconSource) });
    }

    /// <summary>
    /// For missing games imported from a .lnk that still exists, re-reads the original shortcut:
    /// some launchers (Roblox, itch...) update their shortcut when the game moves to a new version folder.
    /// </summary>
    public async Task<int> RepairFromShortcutsAsync(IEnumerable<Game> games)
    {
        var candidates = games.Where(g => g.IsMissing && g.SourceKind == GameSourceKind.Shortcut
                                          && g.SourcePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                                          && File.Exists(g.SourcePath)).ToList();
        if (candidates.Count == 0) return 0;

        var fixes = await Sta.Run(() => candidates
            .Select(g => (Game: g, Info: ShortcutResolver.ReadLnk(g.SourcePath)))
            .Where(x => x.Info?.TargetPath is { } t && File.Exists(t)
                        && !string.Equals(t, x.Game.LaunchTarget, StringComparison.OrdinalIgnoreCase))
            .ToList());

        foreach (var (game, info) in fixes)
        {
            Log.Info($"'{game.Name}' relocated from its shortcut: {game.LaunchTarget} -> {info!.TargetPath}");
            game.LaunchTarget = info.TargetPath!;
            game.TargetPath = info.TargetPath;
            game.Arguments = info.Arguments;
            game.WorkingDirectory = info.WorkingDirectory;
            game.IsMissing = false;
            try
            {
                var copy = AppPaths.Resolve(game.ShortcutCopy);
                if (copy != null && AppPaths.IsManaged(copy)) File.Copy(game.SourcePath, copy, overwrite: true);
            }
            catch { /* the copy is only a backup */ }
        }
        return fixes.Count;
    }

    /// <summary>Extracts icons for games that do not have one yet (e.g. after an upgrade).</summary>
    public Task RepairIconsAsync()
    {
        var missing = _library.Games.Where(g => g.Icon == null || !File.Exists(g.IconFullPath))
                                    .Select(g => (g, IconSourceFor(g)))
                                    .ToList();
        return missing.Count == 0 ? Task.CompletedTask : ExtractIconsAsync(missing);
    }

    private static string? IconSourceFor(Game g)
    {
        if (g.TargetPath is { Length: > 0 } t && File.Exists(t)) return t;
        if (!g.IsUrl && File.Exists(g.LaunchTarget)) return g.LaunchTarget;
        var copy = AppPaths.Resolve(g.ShortcutCopy);
        return copy != null && File.Exists(copy) ? copy : null;
    }

    private async Task ExtractIconsAsync(List<(Game Game, string? Source)> items)
    {
        var results = await Sta.Run(() =>
        {
            var list = new List<(Game, string)>();
            foreach (var (game, source) in items)
            {
                if (source == null) continue;
                var dest = Path.Combine(AppPaths.IconsDir, game.Id + ".png");
                if (IconExtractor.TryExtract(source, dest)) list.Add((game, AppPaths.ToRelative(dest)));
            }
            return list;
        });
        await _ui.InvokeAsync(() =>
        {
            foreach (var (game, icon) in results)
            {
                game.Icon = null; // force refresh even if the path is identical
                game.Icon = icon;
            }
        });
    }

    // ------------------------------------------------------------------------------------
    // Analysis (runs on an STA thread)
    // ------------------------------------------------------------------------------------

    private static List<ImportCandidate> Analyze(List<string> inputs, ImportReport report, bool explicitFiles = false)
    {
        var files = new List<(string Path, bool Explicit)>();
        foreach (var p in inputs)
        {
            try
            {
                if (Directory.Exists(p)) files.AddRange(ScanFolder(p).Select(f => (f, false)));
                else if (File.Exists(p)) files.Add((p, true));
                else report.Ignored++;
            }
            catch (Exception ex) { report.Errors.Add($"{p}: {ex.Message}"); }
        }

        var result = new List<ImportCandidate>();
        foreach (var (file, isExplicit) in files.DistinctBy(f => f.Path.ToLowerInvariant()))
        {
            try
            {
                var c = AnalyzeFile(file, isExplicit || explicitFiles);
                if (c != null) result.Add(c);
                else report.Ignored++;
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                Log.Error($"Import analysis failed for {file}", ex);
            }
        }
        return result;
    }

    private static IEnumerable<string> ScanFolder(string dir)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
        foreach (var f in Directory.EnumerateFiles(dir, "*", options))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".lnk" or ".url") yield return f;
        }
        foreach (var exe in Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
            if (!IsJunkExe(exe)) yield return exe;
    }

    private static ImportCandidate? AnalyzeFile(string file, bool isExplicit)
    {
        switch (Path.GetExtension(file).ToLowerInvariant())
        {
            case ".lnk":
            {
                var info = ShortcutResolver.ReadLnk(file);
                if (info == null) return null;
                var target = info.TargetPath;
                if (!isExplicit && IsJunkShortcut(file, target)) return null;
                var advertised = string.IsNullOrWhiteSpace(target);
                var targetIsExe = target != null && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                var appId = ShortcutResolver.ExtractSteamAppId(info.Arguments)
                            ?? ShortcutResolver.ExtractSteamAppId(target)
                            ?? (targetIsExe ? ShortcutResolver.SteamAppIdFromInstallPath(target) : null);
                var (display, hints) = NameCleaner.Suggest(new NameContext
                {
                    ShortcutFile = file,
                    ExePath = targetIsExe ? target : null,
                    ProductName = targetIsExe ? NameCleaner.ReadProductName(target) : null,
                    RomPath = NameCleaner.FindRomInArguments(info.Arguments),
                });
                string? icon = null;
                if (info.IconPath != null && File.Exists(info.IconPath) && !string.Equals(info.IconPath, target, StringComparison.OrdinalIgnoreCase))
                    icon = info.IconPath;
                else if (!advertised && File.Exists(target)) icon = target;
                icon ??= file;

                return new ImportCandidate
                {
                    Kind = GameSourceKind.Shortcut,
                    SourcePath = file,
                    LaunchTarget = advertised ? file : target!,
                    Arguments = info.Arguments,
                    WorkingDirectory = info.WorkingDirectory,
                    TargetPath = target,
                    SteamAppId = appId,
                    Advertised = advertised,
                    IconSource = icon,
                    Display = display,
                    Hints = hints,
                };
            }
            case ".url":
            {
                var info = ShortcutResolver.ReadUrl(file);
                if (info == null) return null;
                if (!isExplicit && info.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null; // a web bookmark, not a game
                var (display, hints) = NameCleaner.Suggest(new NameContext { ShortcutFile = file });
                return new ImportCandidate
                {
                    Kind = GameSourceKind.InternetShortcut,
                    SourcePath = file,
                    LaunchTarget = info.Url,
                    SteamAppId = ShortcutResolver.ExtractSteamAppId(info.Url),
                    IconSource = info.IconFile != null && File.Exists(info.IconFile) ? info.IconFile : file,
                    Display = display,
                    Hints = hints,
                };
            }
            case ".exe":
            {
                if (!isExplicit && IsJunkExe(file)) return null;
                var (display, hints) = NameCleaner.Suggest(new NameContext
                {
                    ExePath = file,
                    ProductName = NameCleaner.ReadProductName(file),
                });
                return new ImportCandidate
                {
                    Kind = GameSourceKind.Executable,
                    SourcePath = file,
                    LaunchTarget = file,
                    WorkingDirectory = Path.GetDirectoryName(file),
                    TargetPath = file,
                    SteamAppId = ShortcutResolver.SteamAppIdFromInstallPath(file),
                    IconSource = file,
                    Display = display,
                    Hints = hints,
                };
            }
            default:
                return null;
        }
    }

    private static string DedupKey(Game g) =>
        DedupKey(g.SourceKind, g.LaunchTarget, g.TargetPath, g.Arguments, g.SourcePath,
                 g.SourceKind == GameSourceKind.Shortcut && string.IsNullOrWhiteSpace(g.TargetPath));

    private static string DedupKey(GameSourceKind kind, string launchTarget, string? targetPath, string? args, string sourcePath, bool advertised)
    {
        if (kind == GameSourceKind.InternetShortcut || LaunchHelper.IsUri(launchTarget))
            return "url|" + launchTarget.Trim().ToLowerInvariant();
        if (advertised)
            return "adv|" + NameMatcher.Compact(NameCleaner.CleanFileName(sourcePath));
        return "exe|" + NormalizePath(targetPath ?? launchTarget) + "|" + (args ?? "").Trim().ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant(); }
        catch { return path.Trim().ToLowerInvariant(); }
    }

    private static bool IsJunkShortcut(string file, string? target)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (JunkShortcutName().IsMatch(name)) return true;
        return target != null && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && IsJunkExe(target);
    }

    private static bool IsJunkExe(string exe) => JunkExeName().IsMatch(Path.GetFileNameWithoutExtension(exe));

    [GeneratedRegex(@"uninstal|désinstal|desinstal|readme|lisez|manual|manuel|website|site web|support|help|aide|license|licence|eula|changelog|patch notes", RegexOptions.IgnoreCase)]
    private static partial Regex JunkShortcutName();

    [GeneratedRegex(@"^(unins\d*|uninstall.*|setup.*|install.*|vc_?redist.*|vcredist.*|dxsetup|dxwebsetup|directx.*|unitycrashhandler.*|crashreporter.*|crashpad_handler|crashhandler.*|ue4prereqsetup.*|ueprereqsetup.*|easyanticheat.*|beservice.*|dotnetfx.*|oalinst|ndp\d+.*|updater|update|notification_helper|qtwebengineprocess|cefsharp.*|vulkan.*|physx.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex JunkExeName();
}
