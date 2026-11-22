using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>
/// Games of the Steam / Epic libraries that are not installed: added apart ("Non installés") with their hours,
/// moved back with the installed games as soon as they are installed (and the other way round).
/// </summary>
public partial class MainViewModel
{
    private bool _ownedSyncRunning;

    private string NotInstalledSubtitle(string games)
    {
        var hours = Library.Games.Where(g => g.IsNotInstalled && !g.IsHidden).Sum(g => g.PlayTimeSeconds);
        return hours >= 3600 ? $"{games} dans vos launchers  ·  {Format.Duration(hours)} de jeu"
                             : $"{games} dans vos launchers";
    }

    /// <summary>Statistics by day / week / month / year; the chosen period stays while Arcadia is open.</summary>
    public PeriodStatsViewModel PeriodStats { get; } = new();

    private StatsViewModel BuildStats(List<Game> games)
    {
        PeriodStats.Update(games);
        return new StatsViewModel(games, BuildTrophyItems()) { Period = PeriodStats };
    }

    /// <summary>Key remembered when a game of a launcher's library is removed (never added again).</summary>
    public static string OwnedKey(Game g) => $"{g.Store}:{g.StoreId ?? g.SteamAppId?.ToString()}".ToLowerInvariant();

    /// <summary>At startup: the launchers' libraries, then the duplicates (after the first-launch assistant).</summary>
    private async Task StartLibraryChecksAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        while (!Settings.OnboardingDone || Dialogs.OfType<OnboardingViewModel>().Any()) await Task.Delay(TimeSpan.FromSeconds(2));
        if (Settings.ImportOwnedGames) await SyncOwnedGamesAsync(silent: true);
        await Task.Delay(TimeSpan.FromSeconds(3));
        AnnounceDuplicates();
    }

    /// <summary>The games that are in the library twice (pairs the user said are different left out).</summary>
    public List<List<Game>> FindDuplicates()
    {
        using var _ = UiLagMonitor.Step("Duplicate check", 150);
        return Duplicates.Find(Library.Games.ToList(), Settings.NotDuplicates);
    }

    [RelayCommand]
    private void ShowDuplicates()
    {
        var groups = FindDuplicates();
        if (groups.Count == 0)
        {
            ShowToast("Aucun jeu en double dans votre bibliothèque", ToastKind.Success);
            return;
        }
        if (!Dialogs.OfType<DuplicatesViewModel>().Any()) Dialogs.Add(new DuplicatesViewModel(this, groups));
    }

    /// <summary>A few seconds after an import (when the names are identified), looks for duplicates.</summary>
    public void ScheduleDuplicateCheck()
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!Dialogs.OfType<DuplicatesViewModel>().Any()) AnnounceDuplicates();
        };
        timer.Start();
    }

    /// <summary>After an import or at startup: tells the player when some games are there twice.</summary>
    public void AnnounceDuplicates()
    {
        try
        {
            var groups = FindDuplicates();
            if (groups.Count == 0) return;
            ShowToast(groups.Count == 1 ? $"« {groups[0][0].Name} » est en double dans votre bibliothèque"
                                        : $"{groups.Count} jeux sont en double dans votre bibliothèque",
                ToastKind.Warning, "Voir", ShowDuplicates, seconds: 12);
        }
        catch (Exception ex) { Log.Warn($"Duplicate check failed: {ex.Message}"); }
    }

    /// <summary>Removes a game; a game of a launcher's library that is not installed is not added back later.</summary>
    public void RemoveFromLibrary(Game game)
    {
        if (game.IsNotInstalled && (game.StoreId != null || game.SteamAppId != null))
        {
            var key = OwnedKey(game);
            if (!Settings.IgnoredDetections.Contains(key)) Settings.IgnoredDetections.Add(key);
            SaveSettings();
        }
        if (DetailGame == game) DetailGame = null;
        Library.Remove(game);
    }

    [RelayCommand]
    private Task SyncOwnedGames() => SyncOwnedGamesAsync(silent: false);

    public async Task SyncOwnedGamesAsync(bool silent)
    {
        if (_ownedSyncRunning) return;
        _ownedSyncRunning = true;
        try
        {
            var (owned, steamInstalled, epicInstalled) = await Task.Run(() =>
            {
                var o = OwnedGames.ReadAll();
                HashSet<int> s;
                try { s = OwnedGames.InstalledSteamApps(); } catch { s = new HashSet<int>(); }
                List<DetectedGame> e;
                try { e = StoreScanner.ScanEpicInstalled(); } catch { e = new List<DetectedGame>(); }
                return (o, s, e);
            });
            OwnedSyncResult result;
            using (UiLagMonitor.Step("Owned games: apply", 150)) result = ApplyOwned(owned, steamInstalled, epicInstalled);
            if (result.Added.Count > 0 || result.Moved > 0) Library.SaveNow();
            if (result.Added.Count > 0 && Settings.AutoFetchMetadata)
                foreach (var g in result.Added) Metadata.Enqueue(g);
            else foreach (var g in result.Added) g.MetadataState = MetadataState.NotFound;

            if (result.Added.Count > 0)
                ShowToast(result.Added.Count == 1
                        ? $"« {result.Added[0].Name} » (non installé) ajouté dans « Non installés »"
                        : $"{result.Added.Count} jeux non installés de vos bibliothèques Steam et Epic sont rangés dans « Non installés »",
                    ToastKind.Info, "Voir", () => Navigate(Section.NotInstalled), seconds: 10);
            else if (!silent)
                ShowToast(result.Updated + result.Moved > 0 ? $"Bibliothèques synchronisées ({result.Updated + result.Moved} jeu(x) mis à jour)"
                                                             : "Vos bibliothèques Steam et Epic sont déjà à jour", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Warn($"Owned games sync failed: {ex.Message}");
            if (!silent) ShowToast("Lecture des bibliothèques impossible : " + ex.Message, ToastKind.Error);
        }
        finally { _ownedSyncRunning = false; }
    }

    internal sealed record OwnedSyncResult(List<Game> Added, int Updated, int Moved);

    /// <summary>
    /// Applies what the launchers say: hours of the games not installed, installed ⇄ not installed, new games.
    /// A game already in the library under another form (an Xbox copy, a shortcut…) is never added twice.
    /// </summary>
    internal OwnedSyncResult ApplyOwned(List<OwnedGame> owned, HashSet<int> steamInstalled, List<DetectedGame> epicInstalled)
    {
        var added = new List<Game>();
        int updated = 0, moved = 0;
        var ownedSteam = owned.Where(o => o.SteamAppId != null).GroupBy(o => o.SteamAppId!.Value).ToDictionary(g => g.Key, g => g.First());
        var ownedEpic = owned.Where(o => o.Store == GameStore.Epic && o.StoreId != null)
                             .GroupBy(o => o.StoreId!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var epicByApp = epicInstalled.Where(d => d.StoreId != null).GroupBy(d => d.StoreId!, StringComparer.OrdinalIgnoreCase)
                                     .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var g in Library.Games.ToList())
        {
            var steamLaunch = g.SteamAppId is int && g.Store == GameStore.Steam && LaunchHelper.IsUri(g.LaunchTarget);
            var epicLaunch = g.Store == GameStore.Epic && g.StoreId != null && LaunchHelper.IsUri(g.LaunchTarget);

            // installed (again)
            if (g.IsNotInstalled)
            {
                if (steamLaunch && steamInstalled.Contains(g.SteamAppId!.Value))
                {
                    g.IsNotInstalled = false;
                    g.InstallDir = StoreScanner.SteamInstallDir(g.SteamAppId.Value);
                    moved++;
                }
                else if (epicLaunch && epicByApp.TryGetValue(g.StoreId!, out var d))
                {
                    g.IsNotInstalled = false;
                    g.InstallDir = d.InstallDir;
                    g.TargetPath = d.Executable;
                    moved++;
                }
            }
            // uninstalled since (still in the launcher's library)
            else if (steamLaunch && steamInstalled.Count > 0 && !steamInstalled.Contains(g.SteamAppId!.Value) && ownedSteam.ContainsKey(g.SteamAppId.Value)
                     || epicLaunch && !epicByApp.ContainsKey(g.StoreId!) && ownedEpic.ContainsKey(g.StoreId!))
            {
                g.IsNotInstalled = true;
                moved++;
            }

            // hours kept by the launcher (the larger value: Arcadia's own sessions are included in it)
            OwnedGame? source = g.IsNotInstalled && steamLaunch ? ownedSteam.GetValueOrDefault(g.SteamAppId!.Value)
                              : g.IsNotInstalled && epicLaunch ? ownedEpic.GetValueOrDefault(g.StoreId!)
                              : null;
            if (source != null)
            {
                if (source.Seconds > g.PlayTimeSeconds) { g.PlayTimeSeconds = source.Seconds; updated++; }
                if (source.LastPlayed is DateTime last && (g.LastPlayedAt == null || last > g.LastPlayedAt)) g.LastPlayedAt = last;
            }
        }

        // new ones: never a game the library already has (same Steam app, same Epic app, same name)
        var steamIds = Library.Games.SelectMany(g => new[] { g.SteamAppId, g.AchievementsAppId }).Where(i => i != null).Select(i => i!.Value).ToHashSet();
        var storeIds = Library.Games.Where(g => g.StoreId != null).Select(g => g.StoreId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = Library.Games.Select(g => NameMatcher.Compact(g.Name)).Where(n => n.Length > 0).ToHashSet();
        foreach (var o in owned)
        {
            if (Settings.IgnoredDetections.Contains(o.Key)) continue;
            if (o.SteamAppId is int id && !steamIds.Add(id)) continue;
            if (o.Store == GameStore.Epic && o.StoreId != null && !storeIds.Add(o.StoreId)) continue;
            var compact = NameMatcher.Compact(o.Name);
            if (compact.Length == 0 || !names.Add(compact)) continue;
            var game = new Game
            {
                Name = o.Name,
                NameHints = new List<string> { o.Name },
                SourceKind = GameSourceKind.InternetShortcut,
                SourcePath = o.LaunchTarget,
                LaunchTarget = o.LaunchTarget,
                SteamAppId = o.SteamAppId,
                Store = o.Store,
                StoreId = o.StoreId,
                IsNotInstalled = true,
                PlayTimeSeconds = o.Seconds,
                LastPlayedAt = o.LastPlayed,
                AddedAt = DateTime.Now,
                MetadataState = MetadataState.Pending,
            };
            Library.Add(game);
            added.Add(game);
        }
        if (added.Count > 0 || moved > 0) Log.Info($"Owned games: {added.Count} added, {moved} moved between installed / not installed, {updated} hours updated");
        return new OwnedSyncResult(added, updated, moved);
    }
}
