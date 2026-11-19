using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using Arcadia.Models;

namespace Arcadia.Services;

public enum LaunchOutcome { Started, NotFound, Cancelled, Failed }

public sealed record LaunchResult(LaunchOutcome Outcome, string? Error = null);

/// <summary>Starts games exactly like Explorer would (ShellExecute) and tracks play time when possible.</summary>
public sealed class GameLauncher
{
    private readonly Dispatcher _ui;

    public event Action<Game>? GameExited;

    public GameLauncher(Dispatcher ui) => _ui = ui;

    public static bool TargetExists(Game game)
    {
        var target = game.LaunchTarget;
        if (string.IsNullOrWhiteSpace(target)) return false;
        if (LaunchHelper.IsUri(target)) return true;
        try
        {
            if (!File.Exists(target) && !Directory.Exists(target)) return false;
            // emulated game: the ROM must exist too
            if (game.Store == GameStore.Emulator && game.TargetPath is { Length: > 0 } rom
                && !string.Equals(rom, target, StringComparison.OrdinalIgnoreCase) && !File.Exists(rom)) return false;
            return true;
        }
        catch { return false; }
    }

    public LaunchResult Launch(Game game)
    {
        var target = game.LaunchTarget;
        if (!TargetExists(game)) return new LaunchResult(LaunchOutcome.NotFound);

        RunCommand(game, game.PreLaunchCommand, wait: true);

        var psi = new ProcessStartInfo(target) { UseShellExecute = true };
        if (game.RunAsAdmin && !LaunchHelper.IsUri(target)) psi.Verb = "runas";
        if (!LaunchHelper.IsUri(target))
        {
            if (!string.IsNullOrWhiteSpace(game.Arguments)) psi.Arguments = game.Arguments;
            var wd = game.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(wd) || !Directory.Exists(wd)) wd = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(wd)) psi.WorkingDirectory = wd;
        }

        try
        {
            var started = DateTime.Now;
            var process = Process.Start(psi);
            game.LastPlayedAt = started;
            game.PlayCount++;
            Log.Info($"Launched '{game.Name}' -> {target} {game.Arguments}");
            var installDir = ResolveInstallDir(game);
            // started through a launcher outside the game folder (Battle.net, Riot Client…): the launcher stays open,
            // so the play time follows the game's own processes instead
            if (process != null && installDir != null && !LaunchHelper.IsUri(target)
                && !Path.GetFullPath(target).StartsWith(installDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                && game.Store is GameStore.BattleNet or GameStore.Riot or GameStore.EA)
            {
                process.Dispose();
                process = null;
            }
            _ = TrackSessionAsync(game, process, started, installDir);
            return new LaunchResult(LaunchOutcome.Started);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new LaunchResult(LaunchOutcome.Cancelled); // UAC prompt declined
        }
        catch (Exception ex)
        {
            Log.Error($"Launch failed for '{game.Name}'", ex);
            return new LaunchResult(LaunchOutcome.Failed, ex.Message);
        }
    }

    /// <summary>Install folder used to follow the game's processes (cached on the game).</summary>
    private static string? ResolveInstallDir(Game game)
    {
        if (!string.IsNullOrWhiteSpace(game.InstallDir) && Directory.Exists(game.InstallDir)) return game.InstallDir;
        string? dir = null;
        if (game.Store == GameStore.Emulator) return ProcessWatcher.GameFolderOf(game.LaunchTarget);
        if (game.SteamAppId is int appId && LaunchHelper.IsUri(game.LaunchTarget)) dir = StoreScanner.SteamInstallDir(appId);
        dir ??= ProcessWatcher.GameFolderOf(game.TargetPath is { Length: > 0 } t && File.Exists(t) ? t
                                            : LaunchHelper.IsUri(game.LaunchTarget) ? null : game.LaunchTarget);
        if (dir != null && !ProcessWatcher.IsWatchable(dir)) dir = null;
        if (dir != null) game.InstallDir = dir;
        return dir;
    }

    /// <summary>
    /// Measures a play session: follows the started process, then any process still running from the
    /// game folder (launcher stubs), or — for steam:// / uplay:// launches — waits for the game to appear.
    /// </summary>
    private async Task TrackSessionAsync(Game game, Process? process, DateTime started, string? installDir)
    {
        var sessionEnded = false;
        try
        {
            DateTime sessionStart = started;
            if (process != null)
            {
                game.IsRunning = true;
                try { await process.WaitForExitAsync(); }
                catch { /* access denied (elevated): fall back to folder watching */ }
                finally { process.Dispose(); }

                if (installDir != null)
                {
                    var tail = await ProcessWatcher.WatchAsync(installDir, TimeSpan.FromSeconds(6), null);
                    if (tail != null) Log.Info($"'{game.Name}': kept tracking after the launcher exited");
                }
            }
            else if (installDir != null)
            {
                var watched = await ProcessWatcher.WatchAsync(installDir, TimeSpan.FromMinutes(3),
                    () => _ui.InvokeAsync(() => game.IsRunning = true));
                if (watched == null)
                {
                    Log.Info($"'{game.Name}': no process seen in {installDir}, play time not measured");
                    return;
                }
                sessionStart = watched.Value.Start;
            }
            else return;

            sessionEnded = true;
            var seconds = (long)Math.Max(0, (DateTime.Now - sessionStart).TotalSeconds);
            await _ui.InvokeAsync(() =>
            {
                game.PlayTimeSeconds += seconds;
                if (seconds >= 10)
                {
                    game.Sessions.Add(new PlaySession { Start = sessionStart, Seconds = seconds });
                    if (game.Sessions.Count > 1000) game.Sessions.RemoveAt(0);
                }
                Log.Info($"Session ended for '{game.Name}': {seconds}s");
            });
        }
        catch (Exception ex) { Log.Warn($"Session tracking failed for '{game.Name}': {ex.Message}"); }
        finally
        {
            await _ui.InvokeAsync(() =>
            {
                game.IsRunning = false;
                GameExited?.Invoke(game);
            });
            // only when we actually saw the game close
            if (sessionEnded) RunCommand(game, game.PostExitCommand, wait: false);
        }
    }

    /// <summary>Runs a user command (before launch / after exit) through cmd.exe, hidden.</summary>
    private static void RunCommand(Game game, string? command, bool wait)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        try
        {
            var wd = game.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(wd) || !Directory.Exists(wd)) wd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = wd,
            });
            Log.Info($"Ran command for '{game.Name}': {command}");
            if (wait && p != null && !p.WaitForExit(15_000)) Log.Warn("Pre-launch command still running after 15 s, launching anyway");
            p?.Dispose();
        }
        catch (Exception ex) { Log.Warn($"Command failed for '{game.Name}': {ex.Message}"); }
    }

    public static void OpenLocation(Game game)
    {
        var path = game.TargetPath is { Length: > 0 } t && File.Exists(t) ? t : game.LaunchTarget;
        if (LaunchHelper.IsUri(path)) return;
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (dir != null && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) { Log.Warn($"Open location failed: {ex.Message}"); }
    }
}
