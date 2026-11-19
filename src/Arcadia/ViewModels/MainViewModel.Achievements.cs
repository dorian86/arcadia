using Arcadia.Models;
using Arcadia.Services;
using Arcadia.Services.Online;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>
/// Achievements: the official list is fetched from Steam as soon as a game is added and identified.
/// Steam games then show what Steam recorded as unlocked on this PC (read-only, synced after each session);
/// games launched outside a store (.exe, shortcuts) are ticked by the player in the game page.
/// </summary>
public partial class MainViewModel
{
    private readonly AchievementService _achievementService = new();
    private readonly HashSet<Game> _achievementQueue = new();
    private bool _achievementWorkerRunning;

    /// <summary>
    /// Games of the library that were never looked up (first launch of this version, or offline before), and
    /// what was unlocked in Steam games since the last launch.
    /// </summary>
    private async Task LookUpMissingAchievementsAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(6)); // after the startup work (store scan, identification)
        // e.g. an Xbox game once taken for a Steam game: its Steam list does not apply
        foreach (var g in Library.Games.Where(g => !g.CanHaveAchievements && g.Achievements != null).ToList())
        {
            g.Achievements = null;
            g.AchievementsAppId = null;
        }
        foreach (var g in Library.Games.Where(NeedsAchievements).ToList()) QueueAchievements(g);
        foreach (var g in Library.Games.Where(g => g.AchievementsSynced && g.HasAchievements).ToList())
            await SyncSteamAchievementsAsync(g);
    }

    /// <summary>Reads what is unlocked in a Steam game from Steam's files on this PC (cheap, offline).</summary>
    public async Task<int> SyncSteamAchievementsAsync(Game game)
    {
        if (!game.AchievementsSynced || !game.HasAchievements || (game.AchievementsAppId ?? game.SteamAppId) is not int appId) return 0;
        var unlocked = await Task.Run(() => SteamLocalAchievements.ReadUnlocked(appId));
        if (unlocked == null || game.Achievements == null) return 0;
        var (changed, newly) = AchievementService.ApplyUnlocks(game.Achievements, unlocked);
        if (changed > 0) game.RefreshAchievementProgress();
        return newly;
    }

    /// <summary>Steam writes the achievements of a session when the game quits: read them a little after.</summary>
    private async Task SyncSteamAchievementsAfterSessionAsync(Game game)
    {
        if (!game.AchievementsSynced) return;
        var newly = 0;
        foreach (var wait in new[] { 5, 25 })
        {
            await Task.Delay(TimeSpan.FromSeconds(wait));
            newly += await SyncSteamAchievementsAsync(game);
        }
        if (newly > 0)
            ShowToast(newly == 1 ? $"1 nouveau succès dans « {game.Name} »" : $"{newly} nouveaux succès dans « {game.Name} »", ToastKind.Success);
    }

    /// <summary>Identified (or given up on), eligible and never looked up.</summary>
    private static bool NeedsAchievements(Game g) =>
        g.CanHaveAchievements && g.Achievements == null && g.MetadataState != MetadataState.Pending;

    /// <summary>Called by <see cref="OnGameChanged"/>: a new game finished its identification, or its Steam match changed.</summary>
    private void OnGameChangedForAchievements(Game game, string? property)
    {
        switch (property)
        {
            case nameof(Game.MetadataState):
            case nameof(Game.Store):
                if (NeedsAchievements(game)) QueueAchievements(game);
                break;
            case nameof(Game.SteamAppId):
                // re-identified as another game: its list changes too
                if (game.CanHaveAchievements && game.Achievements != null && game.SteamAppId != null && game.SteamAppId != game.AchievementsAppId)
                    QueueAchievements(game);
                break;
            case nameof(Game.Name):
                // renamed after a failed lookup by name: try the new name
                if (game.CanHaveAchievements && game.SteamAppId == null && game.Achievements is { Count: 0 })
                    QueueAchievements(game);
                break;
        }
    }

    private void QueueAchievements(Game game)
    {
        _achievementQueue.Add(game);
        if (!_achievementWorkerRunning) _ = RunAchievementWorkerAsync();
    }

    private async Task RunAchievementWorkerAsync()
    {
        _achievementWorkerRunning = true;
        try
        {
            await Task.Delay(500); // identification writes the name, app id and state one after the other
            while (_achievementQueue.Count > 0)
            {
                var game = _achievementQueue.First();
                _achievementQueue.Remove(game);
                if (!Library.Games.Contains(game) || !game.CanHaveAchievements) continue;
                try
                {
                    var appId = await _achievementService.FindAppIdAsync(game);
                    var list = appId is int id ? await _achievementService.FetchAsync(id) : new List<Achievement>();
                    if (!Library.Games.Contains(game)) continue;
                    game.Achievements = AchievementService.Merge(list, game.Achievements);
                    game.AchievementsAppId = appId;
                    if (game.AchievementsSynced) await SyncSteamAchievementsAsync(game);
                    Log.Info(list.Count > 0
                        ? $"Achievements of '{game.Name}': {list.Count} (Steam app {appId})"
                        : $"No achievements found for '{game.Name}'");
                }
                catch (Exception ex) when (Http.IsNetworkError(ex))
                {
                    // offline: everything still to look up is retried at the next launch
                    Log.Warn($"Achievements unavailable (offline?): {ex.Message}");
                    _achievementQueue.Clear();
                }
                catch (Exception ex)
                {
                    Log.Warn($"Achievements lookup failed for '{game.Name}': {ex.Message}");
                }
            }
        }
        finally { _achievementWorkerRunning = false; }
    }

    [RelayCommand]
    private void ToggleAchievement(Achievement? achievement)
    {
        if (achievement == null) return;
        var game = DetailGame?.Achievements?.Contains(achievement) == true
            ? DetailGame
            : Library.Games.FirstOrDefault(g => g.Achievements?.Contains(achievement) == true);
        if (game == null || game.AchievementsSynced) return; // Steam games: Steam says what is unlocked

        achievement.UnlockedAt = achievement.IsUnlocked ? null : DateTime.Now;
        game.RefreshAchievementProgress();
        Sound.Play(achievement.IsUnlocked ? Sfx.FavoriteOn : Sfx.FavoriteOff);
        if (achievement.IsUnlocked && game.AchievementsUnlocked == game.Achievements!.Count)
            ShowToast($"Tous les succès de « {game.Name} » sont obtenus ! Bravo !", ToastKind.Success);
    }

    [RelayCommand]
    private void RefreshAchievements(object? arg)
    {
        if (arg is not Game game || !game.CanHaveAchievements) return;
        QueueAchievements(game);
        ShowToast($"Recherche des succès de « {game.Name} »…", ToastKind.Info);
    }
}
