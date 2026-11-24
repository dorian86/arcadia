using Arcadia.Models;

namespace Arcadia.Services;

/// <summary>
/// Play time kept by the platforms. Steam and Epic Games record the hours of their games on this PC: for those games
/// Arcadia copies the platform's figure as it is (even when it is lower) and never adds its own measure to it.
/// The other games (exe, emulators, and the platforms that keep nothing locally: Xbox, EA, Ubisoft, Battle.net,
/// Riot, GOG) are measured by Arcadia.
/// </summary>
public static class PlatformPlaytime
{
    /// <summary>
    /// A copy installed or listed by Steam: its hours are Steam's. Another copy of a game also sold on Steam
    /// (identified by its Steam id for its artwork) stays measured by Arcadia.
    /// </summary>
    public static bool IsSteamCopy(Game g) =>
        SteamIdOf(g) is not null
        && (g.Store == GameStore.Steam
            || g.LaunchTarget.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)
            || InSteamLibrary(g.InstallDir) || InSteamLibrary(g.TargetPath) || InSteamLibrary(g.LaunchTarget));

    /// <summary>The Steam id of the game, or the one of its "steam://rungameid/&lt;id&gt;" link.</summary>
    public static int? SteamIdOf(Game g)
    {
        if (g.SteamAppId is int id) return id;
        const string prefix = "steam://rungameid/";
        var target = g.LaunchTarget ?? "";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var digits = string.Concat(target[prefix.Length..].TakeWhile(char.IsDigit));
        return int.TryParse(digits, out var fromLink) && fromLink > 0 ? fromLink : null;
    }

    private static bool InSteamLibrary(string? path) =>
        !string.IsNullOrEmpty(path) && path.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Takes the platform's hours as they are. Returns the change, in seconds.</summary>
    public static long Apply(Game g, GameStore source, long seconds)
    {
        g.PlaytimeSource = source;
        var change = seconds - g.PlayTimeSeconds;
        if (change != 0) g.PlayTimeSeconds = seconds;
        return change;
    }
}
