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
        if (Settings.ImportSteamPlaytime) _ = SyncSteamPlaytimeAsync(silent: true);
        _ = RefreshEmulatorsAsync();
        _ = BuildAllTilesAsync();
        _ = CheckForUpdatesOnStartupAsync();
    }

    // ------------------------------------------------------------------------------------
    // Tiles of the console themes
    // ------------------------------------------------------------------------------------

    private readonly HashSet<Game> _tileQueue = new();
    private bool _tileWorkerRunning;

    /// <summary>Every game gets square and wide tiles shaped for the themes (rebuilt only when its images changed).</summary>
    private async Task BuildAllTilesAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        foreach (var g in Library.Games) QueueTiles(g);
    }

    /// <summary>Called when a cover, illustration or logo changes, and for new games.</summary>
    private void QueueTiles(Game game)
    {
        _tileQueue.Add(game);
        if (!_tileWorkerRunning) _ = RunTileWorkerAsync();
    }

    private async Task RunTileWorkerAsync()
    {
        _tileWorkerRunning = true;
        try
        {
            await Task.Delay(1500); // a metadata match writes cover, illustration and logo one after the other
            while (_tileQueue.Count > 0)
            {
                var batch = _tileQueue.Take(6).ToList();
                foreach (var g in batch) _tileQueue.Remove(g);
                var jobs = batch.Where(g => Library.Games.Contains(g))
                    .Select(g => (Game: g, Sources: new TileSources(g.CoverFullPath, g.HeroFullPath, g.LogoFullPath), Known: g.TileSignature))
                    .ToList();
                // rendered on a worker thread: the interface never stutters, even for a whole library
                var results = await Sta.Run(() => jobs.Select(j =>
                {
                    var signature = TileArt.Signature(j.Sources);
                    if (signature == j.Known) return (j.Game, Signature: (string?)null, Square: (byte[]?)null, Wide: (byte[]?)null);
                    try { return (j.Game, Signature: signature, Square: TileArt.Square(j.Sources), Wide: TileArt.Wide(j.Sources)); }
                    catch (Exception ex)
                    {
                        Log.Warn($"Tile artwork failed for '{j.Game.Name}': {ex.Message}");
                        return (j.Game, Signature: signature, Square: (byte[]?)null, Wide: (byte[]?)null);
                    }
                }).ToList());
                var built = results.Count(r => r.Signature != null);
                if (built > 0) Log.Info($"Tiles built for {built} game(s)");
                foreach (var r in results)
                {
                    if (r.Signature == null || !Library.Games.Contains(r.Game)) continue;
                    r.Game.TileSquare = await SaveTileAsync(r.Game, r.Game.TileSquare, r.Square, "sq");
                    r.Game.TileWide = await SaveTileAsync(r.Game, r.Game.TileWide, r.Wide, "wide");
                    r.Game.TileSignature = r.Signature;
                }
                await Task.Delay(50);
            }
        }
        catch (Exception ex) { Log.Warn($"Tile artwork worker failed: {ex.Message}"); }
        finally { _tileWorkerRunning = false; }
    }

    private static async Task<string?> SaveTileAsync(Game game, string? old, byte[]? bytes, string kind)
    {
        string? rel = null;
        if (bytes != null)
        {
            // a new name each time: image caches never show an outdated tile
            rel = Path.Combine("images", $"{game.Id}_tile_{kind}_{DateTime.UtcNow.Ticks:x}.jpg");
            await File.WriteAllBytesAsync(AppPaths.Resolve(rel)!, bytes);
        }
        if (old != null && old != rel) AppPaths.TryDeleteManaged(old);
        return rel;
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
    private Task ImportSteamPlaytime() => SyncSteamPlaytimeAsync(silent: false);

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
