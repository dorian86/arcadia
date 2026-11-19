using System.Windows.Threading;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>Steam play time, emulator library, generated covers, progress tracking and the random picker.</summary>
public partial class MainViewModel
{
    private readonly HashSet<Game> _regenQueue = new();
    private DispatcherTimer? _regenTimer;

    /// <summary>Raised when a game is set to "close Arcadia" on launch.</summary>
    public event Action? CloseRequested;

    private void InitializeFeatures()
    {
        // newly identified Steam games: pick up their Steam play time as soon as identification is done
        Metadata.QueueEmptied += () => { if (Settings.ImportSteamPlaytime) _ = SyncSteamPlaytimeAsync(silent: true); };
        _ = GenerateMissingCoversAsync();
        if (Settings.ImportSteamPlaytime)
        {
            _ = SyncSteamPlaytimeAsync(silent: true);
            _ = SyncEpicPlaytimeAsync(silent: true);
        }
        _ = RefreshEmulatorsAsync();
        _ = BuildAllTilesAsync();
        _ = LookUpMissingAchievementsAsync();
        _ = StartTrophiesAsync();
        _ = CheckForUpdatesOnStartupAsync();

        // play time of the games started outside Arcadia (Steam, Epic, EA app, Battle.net, shortcuts…)
        _backgroundPlaytime = new BackgroundPlaytime(() => Library.Games.ToList(), () => Library.ScheduleSave())
        {
            Enabled = Settings.TrackOutsidePlaytime,
        };
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.TrackOutsidePlaytime)) _backgroundPlaytime.Enabled = Settings.TrackOutsidePlaytime;
        };
    }

    private BackgroundPlaytime? _backgroundPlaytime;

    // ------------------------------------------------------------------------------------
    // Tiles of the console themes
    // ------------------------------------------------------------------------------------

    private readonly HashSet<Game> _tileQueue = new();
    private bool _tileWorkerRunning;

    /// <summary>What the tile worker knows about one game (read on the UI thread).</summary>
    private sealed record TileJob(Game Game, string Id, TileSources Sources, string? Known, string? KnownSquare, string? KnownWide)
    {
        public string Signature { get; set; } = "";
        public string? Square { get; set; }
        public string? Wide { get; set; }
    }

    /// <summary>
    /// Every game gets square and wide tiles shaped for the themes. A tile is built once and kept on disk
    /// (<see cref="TileArt.FileFor"/>): later launches only check that the source images did not change.
    /// </summary>
    private async Task BuildAllTilesAsync()
    {
        await Task.Delay(800);
        foreach (var g in Library.Games) _tileQueue.Add(g);
        var ok = true;
        if (!_tileWorkerRunning) ok = await RunTileWorkerAsync(wait: false);
        else while (_tileWorkerRunning) await Task.Delay(300); // the running worker takes the queue too
        // only once every game was matched with its tiles, otherwise reusable tiles would be thrown away
        if (ok && _tileQueue.Count == 0) CleanTileCache();
    }

    /// <summary>Called when a cover, illustration or logo changes, and for new games.</summary>
    private void QueueTiles(Game game)
    {
        _tileQueue.Add(game);
        if (!_tileWorkerRunning) _ = RunTileWorkerAsync(wait: true);
    }

    private async Task<bool> RunTileWorkerAsync(bool wait)
    {
        _tileWorkerRunning = true;
        var ok = true;
        try
        {
            if (wait) await Task.Delay(1500); // a metadata match writes cover, illustration and logo one after the other
            while (_tileQueue.Count > 0)
            {
                var jobs = _tileQueue.Where(g => Library.Games.Contains(g))
                    .Select(g => new TileJob(g, g.Id, new TileSources(g.CoverFullPath, g.HeroFullPath, g.LogoFullPath), g.TileSignature, g.TileSquare, g.TileWide))
                    .ToList();
                _tileQueue.Clear();

                // 1. cheap check, off the UI thread: unchanged images, or tiles already built by an earlier launch
                var toBuild = await Task.Run(() =>
                {
                    var missing = new List<TileJob>();
                    foreach (var j in jobs)
                    {
                        j.Signature = TileArt.Signature(j.Sources);
                        if (j.Signature == j.Known && Exists(j.KnownSquare) && Exists(j.KnownWide)) { j.Signature = j.Known; continue; }
                        var sq = TileArt.FileFor(j.Id, j.Signature, "sq");
                        var wide = TileArt.FileFor(j.Id, j.Signature, "wide");
                        if (File.Exists(AppPaths.Resolve(sq)) && File.Exists(AppPaths.Resolve(wide))) { j.Square = sq; j.Wide = wide; }
                        else missing.Add(j);
                    }
                    return missing;
                });
                foreach (var j in jobs.Where(j => j.Square != null && !toBuild.Contains(j))) ApplyTiles(j);

                // 2. only new or changed artwork is rendered, on several worker threads (the interface never stutters)
                if (toBuild.Count == 0) continue;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
                var pending = new Queue<TileJob>(toBuild);
                var running = new List<Task<List<TileJob>>>();
                while (pending.Count > 0 || running.Count > 0)
                {
                    while (running.Count < threads && pending.Count > 0)
                    {
                        var chunk = new List<TileJob>();
                        while (chunk.Count < 3 && pending.Count > 0) chunk.Add(pending.Dequeue());
                        running.Add(Sta.Run(() => { foreach (var j in chunk) RenderTiles(j); return chunk; }));
                    }
                    var done = await Task.WhenAny(running);
                    running.Remove(done);
                    foreach (var j in await done) ApplyTiles(j); // tiles appear as soon as they are ready
                }
                Log.Info($"Tiles built for {toBuild.Count} game(s) in {watch.Elapsed.TotalSeconds:0.0}s");
            }
        }
        catch (Exception ex) { Log.Warn($"Tile artwork worker failed: {ex.Message}"); ok = false; }
        finally { _tileWorkerRunning = false; }
        return ok;
    }

    /// <summary>A tile path that is unset (no artwork) or whose file is still on disk.</summary>
    private static bool Exists(string? rel) => rel == null || File.Exists(AppPaths.Resolve(rel));

    /// <summary>Renders and writes both tiles of a game (STA worker thread).</summary>
    private static void RenderTiles(TileJob j)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Resolve(TileArt.Folder)!);
            j.Square = WriteTile(TileArt.Square(j.Sources), TileArt.FileFor(j.Id, j.Signature, "sq"));
            j.Wide = WriteTile(TileArt.Wide(j.Sources), TileArt.FileFor(j.Id, j.Signature, "wide"));
        }
        catch (Exception ex) { Log.Warn($"Tile artwork failed for '{j.Game.Name}': {ex.Message}"); }
    }

    private static string? WriteTile(byte[]? bytes, string rel)
    {
        if (bytes == null) return null;
        var full = AppPaths.Resolve(rel)!;
        var part = full + ".part"; // never leave a half-written tile behind (it would be reused as is)
        File.WriteAllBytes(part, bytes);
        File.Move(part, full, overwrite: true);
        return rel;
    }

    private void ApplyTiles(TileJob j)
    {
        if (!Library.Games.Contains(j.Game)) return;
        var game = j.Game;
        if (game.TileSquare != null && game.TileSquare != j.Square) AppPaths.TryDeleteManaged(game.TileSquare);
        if (game.TileWide != null && game.TileWide != j.Wide) AppPaths.TryDeleteManaged(game.TileWide);
        game.TileSquare = j.Square;
        game.TileWide = j.Wide;
        game.TileSignature = j.Signature;
    }

    /// <summary>Deletes tiles no game uses any more (old artwork, removed games, tiles of older versions).</summary>
    private void CleanTileCache()
    {
        var used = Library.Games.SelectMany(g => new[] { g.TileSquare, g.TileWide })
            .Where(p => p != null).Select(p => Path.GetFileName(p!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var started = DateTime.UtcNow.AddSeconds(-5);
        _ = Task.Run(() =>
        {
            var removed = 0;
            try
            {
                var dir = AppPaths.Resolve(TileArt.Folder)!;
                var files = Directory.Exists(dir) ? Directory.EnumerateFiles(dir).ToList() : new List<string>();
                // tiles of Arcadia 1.0.0 were stored next to the covers ("<id>_tile_sq_<ticks>.jpg")
                files.AddRange(Directory.EnumerateFiles(AppPaths.ImagesDir, "*_tile_*.jpg"));
                // files written meanwhile belong to a tile being built right now
                foreach (var f in files.Where(f => !used.Contains(Path.GetFileName(f)) && File.GetLastWriteTimeUtc(f) < started))
                {
                    try { File.Delete(f); removed++; } catch { /* in use: next launch */ }
                }
            }
            catch (Exception ex) { Log.Warn($"Tile cache cleanup failed: {ex.Message}"); }
            if (removed > 0) Log.Info($"Tile cache: {removed} unused file(s) deleted");
        });
    }

    public void SaveSettings() => _settingsService.SaveNow();

    // ------------------------------------------------------------------------------------
    // Generated covers
    // ------------------------------------------------------------------------------------

    private async Task GenerateMissingCoversAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        foreach (var g in Library.Games.Where(g => !g.HasCover && g.MetadataState is MetadataState.NotFound or MetadataState.Manual).ToList())
        {
            await Metadata.GenerateCoverAsync(g);
            await Task.Delay(20); // keep the UI responsive
        }
    }

    /// <summary>A generated cover shows the name and icon: rebuild it when they change.</summary>
    private void QueueCoverRegeneration(Game game)
    {
        _regenQueue.Add(game);
        if (_regenTimer == null)
        {
            _regenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _regenTimer.Tick += async (_, _) =>
            {
                _regenTimer.Stop();
                foreach (var g in _regenQueue.ToList())
                {
                    _regenQueue.Remove(g);
                    if (g.CoverGenerated) await Metadata.GenerateCoverAsync(g);
                }
            };
        }
        _regenTimer.Stop();
        _regenTimer.Start();
    }

    [RelayCommand]
    private void RetryMissingArt()
    {
        var targets = Library.Games.Where(g => !g.HasCover || g.CoverGenerated).ToList();
        if (targets.Count == 0)
        {
            ShowToast("Tous vos jeux ont une jaquette", ToastKind.Success);
            return;
        }
        IsFetchingMetadata = true;
        foreach (var g in targets)
        {
            g.CustomCover = false;
            g.MetadataState = MetadataState.Pending;
            Metadata.Enqueue(g);
        }
        ShowToast($"Nouvelle recherche d'images pour {targets.Count} jeu(x)…", ToastKind.Info);
    }

    // ------------------------------------------------------------------------------------
    // Steam play time
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task ImportSteamPlaytime()
    {
        await SyncSteamPlaytimeAsync(silent: false);
        await SyncEpicPlaytimeAsync(silent: false);
    }

    private async Task SyncSteamPlaytimeAsync(bool silent)
    {
        try
        {
            var data = await Task.Run(SteamPlaytime.Read);
            if (data.Count == 0)
            {
                if (!silent) ShowToast("Aucune donnée de temps de jeu Steam trouvée sur ce PC", ToastKind.Warning);
                return;
            }
            var updated = 0;
            long added = 0;
            foreach (var g in Library.Games)
            {
                if (g.SteamAppId is not int id || !data.TryGetValue(id, out var usage)) continue;
                g.SteamPlayTimeSeconds = usage.Seconds;
                // Steam also counts sessions started from Arcadia: keep the larger value, never add both
                if (usage.Seconds > g.PlayTimeSeconds)
                {
                    added += usage.Seconds - g.PlayTimeSeconds;
                    g.PlayTimeSeconds = usage.Seconds;
                    updated++;
                }
                if (usage.LastPlayed is DateTime last && (g.LastPlayedAt == null || last > g.LastPlayedAt.Value.AddMinutes(1)))
                    g.LastPlayedAt = last;
            }
            if (!silent || added >= 3600)
                ShowToast(updated == 0
                    ? "Temps de jeu Steam déjà à jour"
                    : $"Temps de jeu Steam importé : +{Format.Duration(added)} sur {updated} jeu(x)", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Warn($"Steam play time import failed: {ex.Message}");
            if (!silent) ShowToast("Lecture du temps de jeu Steam impossible : " + ex.Message, ToastKind.Error);
        }
    }

    /// <summary>
    /// Play time recorded by the new Epic Games launcher (it keeps it on the PC). Matched by the game's Epic id
    /// (store scan) or by the ".egstore" folder of games imported from their exe.
    /// </summary>
    private async Task SyncEpicPlaytimeAsync(bool silent)
    {
        try
        {
            var games = Library.Games.Where(g => g.Store == GameStore.Epic || g.Store == GameStore.Local).ToList();
            var (data, ids) = await Task.Run(() =>
            {
                var playtime = EpicLauncher.ReadPlaytime();
                var map = new Dictionary<Game, string>();
                if (playtime.Count == 0) return (playtime, map);
                foreach (var g in games)
                {
                    var app = g.Store == GameStore.Epic && g.StoreId != null && playtime.ContainsKey(g.StoreId) ? g.StoreId
                              : EpicLauncher.FromGameFolder(!string.IsNullOrWhiteSpace(g.InstallDir) ? g.InstallDir
                                                            : g.TargetPath is { Length: > 0 } t ? t : g.LaunchTarget)?.AppName;
                    if (app != null) map[g] = app;
                }
                return (playtime, map);
            });
            if (data.Count == 0)
            {
                if (!silent) ShowToast("Aucune donnée de temps de jeu Epic trouvée sur ce PC", ToastKind.Warning);
                return;
            }
            var updated = 0;
            long added = 0;
            foreach (var (g, app) in ids)
            {
                if (!data.TryGetValue(app, out var seconds) || seconds <= g.PlayTimeSeconds) continue;
                // Epic also counts the sessions started from Arcadia: keep the larger value, never add both
                added += seconds - g.PlayTimeSeconds;
                g.PlayTimeSeconds = seconds;
                updated++;
            }
            if (updated > 0) Log.Info($"Epic play time imported: +{added}s on {updated} game(s)");
            if (!silent || added >= 3600)
                ShowToast(updated == 0 ? "Temps de jeu Epic déjà à jour"
                    : $"Temps de jeu Epic importé : +{Format.Duration(added)} sur {updated} jeu(x)", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Warn($"Epic play time import failed: {ex.Message}");
            if (!silent) ShowToast("Lecture du temps de jeu Epic impossible : " + ex.Message, ToastKind.Error);
        }
    }

    // ------------------------------------------------------------------------------------
    // Emulators
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private void ManageEmulators() => Dialogs.Add(new EmulatorsViewModel(this));

    /// <summary>First run: emulators already in the library become profiles; folders are rescanned for new ROMs.</summary>
    public async Task RefreshEmulatorsAsync(bool delay = true)
    {
        try
        {
            if (delay) await Task.Delay(TimeSpan.FromSeconds(3));
            var detected = EmulatorService.DetectFromLibrary(Library.Games, Settings.Emulators);
            if (detected.Count > 0)
            {
                Settings.Emulators.AddRange(detected);
                SaveSettings();
                Log.Info($"Emulators detected: {string.Join(", ", detected.Select(d => d.Name))}");
            }
            var count = await ScanEmulatorProfilesAsync(Settings.Emulators);
            if (count > 0)
                ShowToast($"{count} jeu(x) émulé(s) ajouté(s) depuis vos dossiers de ROM", ToastKind.Success, "Voir",
                    () => { CurrentPlatform = GameStore.Emulator; Section = Section.Platform; });
        }
        catch (Exception ex) { Log.Warn($"Emulator refresh failed: {ex.Message}"); }
    }

    public async Task<int> ScanEmulatorProfilesAsync(IEnumerable<EmulatorProfile> profiles)
    {
        var list = profiles.Where(p => !string.IsNullOrWhiteSpace(p.RomFolder) && File.Exists(p.ExePath)).ToList();
        if (list.Count == 0) return 0;
        var roms = await Task.Run(() => list.SelectMany(p => EmulatorService.FindRoms(p).Select(r => (Profile: p, Rom: r))).ToList());
        var added = new List<Game>();
        foreach (var (profile, rom) in roms)
        {
            if (EmulatorService.IsRomInLibrary(Library.Games, rom)) continue;
            var game = EmulatorService.CreateGame(profile, rom);
            Library.Add(game);
            added.Add(game);
        }
        if (added.Count == 0) return 0;
        Library.SaveNow();
        if (Settings.AutoFetchMetadata)
        {
            IsFetchingMetadata = true;
            foreach (var g in added) Metadata.Enqueue(g);
        }
        Log.Info($"Emulator scan: {added.Count} ROM(s) added");
        return added.Count;
    }

    // ------------------------------------------------------------------------------------
    // Progress
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private void SetStatus(object? arg)
    {
        if (DetailGame is not { } g) return;
        var status = arg is GameStatus s ? s : Enum.TryParse<GameStatus>(arg?.ToString(), out var p) ? p : GameStatus.None;
        g.Status = g.Status == status ? GameStatus.None : status;
        Sound.Play(Sfx.Toggle);
    }

    [RelayCommand]
    private void SetRating(object? arg)
    {
        if (DetailGame is not { } g || !int.TryParse(arg?.ToString(), out var r)) return;
        g.Rating = g.Rating == r ? 0 : Math.Clamp(r, 0, 5);
        Sound.Play(Sfx.Tick);
    }

    [RelayCommand]
    private void OpenStatus(NavEntry? entry)
    {
        if (entry?.Status is not { } st) return;
        Sound.Play(Sfx.Navigate);
        DetailGame = null;
        CurrentStatus = st;
        if (Section == Section.Status) { ClearSelection(); Refresh(); OnPropertyChanged(nameof(Section)); }
        else Section = Section.Status;
    }

    // ------------------------------------------------------------------------------------
    // Random picker
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private void OpenRoulette()
    {
        if (AllCount == 0) return;
        Dialogs.Add(new RouletteViewModel(this));
    }
}
