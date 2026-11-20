using Arcadia.Models;
using Arcadia.Services;
using Arcadia.Views;

namespace Arcadia.ViewModels;

/// <summary>
/// Live achievements of games started without their store's launcher: their unlocks are read from the local files
/// written by the game (see <see cref="EmulatorAchievements"/>), followed while Arcadia runs, and announced over
/// the game with a small popup.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Game → Steam app id of its achievement files and its folder.</summary>
    private readonly Dictionary<Game, (int AppId, string? Folder, string? ExeFolder)> _liveAchievements = new();
    /// <summary>Steam app id written in the game's folder (more reliable than a match by name).</summary>
    private readonly Dictionary<Game, int> _folderAppIds = new();
    private AchievementFileWatcher? _achievementWatcher;

    private static bool CanFollowAchievements(Game g) =>
        g.CanHaveAchievements && !g.AchievementsSynced && !g.IsNotInstalled && !LaunchHelper.IsUri(g.LaunchTarget);

    private static string? FolderOf(Game g)
    {
        if (!string.IsNullOrWhiteSpace(g.InstallDir) && Directory.Exists(g.InstallDir) && ProcessWatcher.IsWatchable(g.InstallDir)) return g.InstallDir;
        var exe = g.TargetPath is { Length: > 0 } t ? t : g.LaunchTarget;
        return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ProcessWatcher.GameFolderOf(exe) : null;
    }

    private static string? ExeFolderOf(Game g)
    {
        var exe = g.TargetPath is { Length: > 0 } t ? t : g.LaunchTarget;
        return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(exe) : null;
    }

    /// <summary>At startup: finds which games write achievement files, catches up on what was unlocked, then watches.</summary>
    private async Task StartLiveAchievementsAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        try
        {
            var games = Library.Games.Where(CanFollowAchievements).ToList();
            var found = await Task.Run(() => games
                .Select(g => (Game: g, Folder: FolderOf(g)))
                .Select(x => (x.Game, x.Folder, ExeFolder: ExeFolderOf(x.Game), AppId: EmulatorAchievements.AppIdFromFolder(x.Folder),
                              LocalSave: x.Folder != null && HasLocalSave(x.Folder, ExeFolderOf(x.Game))))
                .ToList());

            var localFolders = new List<string>();
            foreach (var (game, folder, exeFolder, folderId, localSave) in found)
            {
                if (folderId is int id)
                {
                    _folderAppIds[game] = id;
                    // the list was looked up by name (or not found): the folder knows the real game
                    if (game.AchievementsAppId != id) QueueAchievements(game);
                }
                if ((folderId ?? game.AchievementsAppId ?? game.SteamAppId) is int appId) _liveAchievements[game] = (appId, folder, exeFolder);
                if (localSave && folder != null) localFolders.Add(folder);
            }

            var newly = 0;
            foreach (var game in _liveAchievements.Keys.ToList()) newly += await SyncLiveAchievementsAsync(game, notify: false);
            Log.Info($"Live achievements: {_liveAchievements.Count(kv => kv.Key.AchievementsTracked)} game(s) followed, {newly} unlock(s) caught up");
            if (newly > 0)
                ShowToast(newly == 1 ? "1 succès obtenu en jouant hors launcher a été ajouté" : $"{newly} succès obtenus en jouant hors launcher ont été ajoutés",
                    ToastKind.Success, seconds: 8);

            _achievementWatcher = new AchievementFileWatcher((appId, path) => _ui.InvokeAsync(() => OnAchievementFileChanged(appId, path)));
            foreach (var root in EmulatorAchievements.Roots()) _achievementWatcher.Watch(root);
            foreach (var folder in localFolders) _achievementWatcher.Watch(folder);
        }
        catch (Exception ex) { Log.Warn($"Live achievements unavailable: {ex.Message}"); }
    }

    /// <summary>Emulators that save next to the game (Goldberg "local_save.txt", TENOKE "SteamData").</summary>
    private static bool HasLocalSave(string folder, string? exeFolder)
    {
        try
        {
            return EmulatorAchievements.TenokeFiles(folder, exeFolder).Any()
                   || Directory.EnumerateFiles(folder, "local_save.txt", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true }).Any();
        }
        catch { return false; }
    }

    private async void OnAchievementFileChanged(int? appId, string path)
    {
        var games = _liveAchievements
            .Where(kv => appId == kv.Value.AppId
                         || kv.Value.Folder != null && path.StartsWith(kv.Value.Folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key).ToList();
        foreach (var game in games)
        {
            try { await SyncLiveAchievementsAsync(game, notify: true); }
            catch (Exception ex) { Log.Warn($"Live achievements of '{game.Name}': {ex.Message}"); }
        }
    }

    /// <summary>
    /// Adds the unlocks found in the game's files (never removes what the player ticked by hand). Returns how many
    /// are new; with <paramref name="notify"/>, each one pops up over the game.
    /// </summary>
    private async Task<int> SyncLiveAchievementsAsync(Game game, bool notify)
    {
        if (!_liveAchievements.TryGetValue(game, out var source)) return 0;
        var (unlocked, hasFiles) = await Task.Run(() =>
            (EmulatorAchievements.ReadUnlocked(source.AppId, source.Folder, source.ExeFolder), EmulatorAchievements.HasFiles(source.AppId, source.Folder, source.ExeFolder)));
        if (hasFiles) game.AchievementsTracked = true;
        if (game.Achievements is not { Count: > 0 } list || unlocked.Count == 0) return 0;

        var newly = new List<Achievement>();
        foreach (var a in list)
        {
            if (a.UnlockedAt != null || !unlocked.TryGetValue(a.Key, out var at)) continue;
            a.UnlockedAt = at == DateTime.MinValue ? DateTime.Now : at;
            newly.Add(a);
        }
        if (newly.Count == 0) return 0;
        game.RefreshAchievementProgress();
        Log.Info($"Achievements unlocked in '{game.Name}': {string.Join(", ", newly.Select(a => a.Name))}");

        if (notify)
        {
            foreach (var a in newly.Take(5))
            {
                var rarity = a.Percent is double p ? $"  ·  {p.ToString(p < 10 ? "0.#" : "0", Format.Fr)} % des joueurs" : "";
                if (Settings.AchievementPopups) AchievementPopup.Enqueue(new AchievementNotice(a.Name, game.Name + rarity, a.Icon));
            }
            if (newly.Count > 5 && Settings.AchievementPopups)
                AchievementPopup.Enqueue(new AchievementNotice($"+ {newly.Count - 5} autres succès", game.Name, null));
            Sound.Play(Sfx.Trophy);
            if (game.AchievementsUnlocked == list.Count)
                ShowToast($"Tous les succès de « {game.Name} » sont obtenus ! Bravo !", ToastKind.Success);
        }
        return newly.Count;
    }
}
