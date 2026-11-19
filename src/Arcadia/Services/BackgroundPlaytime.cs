using System.Windows.Threading;
using Arcadia.Models;

namespace Arcadia.Services;

/// <summary>
/// Play time of games started outside Arcadia (from Steam, Epic, the EA app, the Xbox app, a desktop shortcut…):
/// every 20 seconds, looks at the running programs and recognises the games of the library by their folder or
/// their executable. A session is recorded when the game closes (or when Arcadia quits). Games started by
/// Arcadia are measured by <see cref="GameLauncher"/> and left alone here.
/// </summary>
public sealed class BackgroundPlaytime : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    private const int MinSeconds = 60;

    private readonly Func<IReadOnlyList<Game>> _games;
    private readonly Action _changed;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Game, DateTime> _active = new();
    private bool _busy;

    public BackgroundPlaytime(Func<IReadOnlyList<Game>> games, Action changed)
    {
        _games = games;
        _changed = changed;
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public bool Enabled
    {
        get => _timer.IsEnabled;
        set
        {
            if (value) _timer.Start();
            else
            {
                _timer.Stop();
                FlushAll();
            }
        }
    }

    /// <summary>A game being played outside Arcadia right now (shown as "En cours" would mislead: only for stats).</summary>
    public IReadOnlyCollection<Game> Playing => _active.Keys;

    private async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var games = _games().Where(g => g.Store != GameStore.Emulator).ToList();
            var paths = await Task.Run(ProcessWatcher.RunningPaths);
            var running = Match(games, paths);
            var now = DateTime.Now;

            foreach (var game in running)
            {
                if (game.IsRunning) continue; // launched by Arcadia: measured by the launcher
                if (!_active.ContainsKey(game))
                {
                    // just launched from Arcadia (the launcher has not seen the game yet): not ours
                    if (game.LastPlayedAt is DateTime last && now - last < TimeSpan.FromMinutes(4)) continue;
                    _active[game] = now - Interval / 2; // started somewhere since the last look
                    Log.Info($"'{game.Name}' started outside Arcadia: measuring its play time");
                }
            }
            foreach (var (game, start) in _active.ToList())
            {
                if (game.IsRunning)
                {
                    _active.Remove(game); // Arcadia took over (launched through it)
                    continue;
                }
                if (!running.Contains(game)) End(game, start, now - Interval / 2);
            }
        }
        catch (Exception ex) { Log.Warn($"Background play time check failed: {ex.Message}"); }
        finally { _busy = false; }
    }

    /// <summary>
    /// The games whose program is running. A folder shared by several games (e.g. "D:\Jeux" holding loose
    /// exe) only counts through the exact executable.
    /// </summary>
    internal static HashSet<Game> Match(IReadOnlyList<Game> games, IReadOnlyList<string> paths)
    {
        var result = new HashSet<Game>();
        if (paths.Count == 0) return result;
        var watched = games.Select(g => (Game: g, Folder: FolderOf(g), Exe: ExeOf(g))).ToList();
        var shared = watched.Where(w => w.Folder != null).GroupBy(w => w.Folder!, StringComparer.OrdinalIgnoreCase)
                            .Where(group => group.Count() > 1).Select(group => group.Key)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (game, folder, exe) in watched)
        {
            foreach (var path in paths)
            {
                if (IsHelper(path)) continue;
                var hit = exe != null && path.Equals(exe, StringComparison.OrdinalIgnoreCase)
                          || folder != null && !shared.Contains(folder) && path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
                result.Add(game);
                break;
            }
        }
        return result;
    }

    private static string? FolderOf(Game g)
    {
        var dir = !string.IsNullOrWhiteSpace(g.InstallDir) ? g.InstallDir
                  : ProcessWatcher.GameFolderOf(ExeOf(g));
        return dir != null && ProcessWatcher.IsWatchable(dir) ? dir.TrimEnd('\\') : null;
    }

    private static string? ExeOf(Game g)
    {
        var exe = g.TargetPath is { Length: > 0 } t ? t : g.LaunchTarget;
        if (string.IsNullOrWhiteSpace(exe) || LaunchHelper.IsUri(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        var name = Path.GetFileName(exe);
        // launchers shared by many games are not the game
        if (name.Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return exe;
    }

    private static bool IsHelper(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Contains("crash", StringComparison.OrdinalIgnoreCase) || name.Contains("unins", StringComparison.OrdinalIgnoreCase)
               || name.Contains("redist", StringComparison.OrdinalIgnoreCase) || name.Contains("setup", StringComparison.OrdinalIgnoreCase)
               || name.Contains("updater", StringComparison.OrdinalIgnoreCase);
    }

    private void End(Game game, DateTime start, DateTime end)
    {
        _active.Remove(game);
        var seconds = (long)(end - start).TotalSeconds;
        if (seconds < MinSeconds) return;
        game.PlayTimeSeconds += seconds;
        game.Sessions.Add(new PlaySession { Start = start, Seconds = seconds });
        if (game.Sessions.Count > 1000) game.Sessions.RemoveAt(0);
        if (game.LastPlayedAt == null || start > game.LastPlayedAt) game.LastPlayedAt = start;
        game.PlayCount++;
        Log.Info($"Session outside Arcadia for '{game.Name}': {seconds}s");
        _changed();
    }

    /// <summary>Arcadia closes: the sessions in progress are recorded up to now.</summary>
    public void FlushAll()
    {
        var now = DateTime.Now;
        foreach (var (game, start) in _active.ToList()) End(game, start, now);
    }

    public void Dispose()
    {
        _timer.Stop();
        FlushAll();
    }
}
