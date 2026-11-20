using Arcadia.Models;

namespace Arcadia.Services;

/// <summary>
/// Finds the games that are in the library twice (imported from two folders, from a shortcut and from the
/// launcher, two copies of the same game…) and merges them without losing play time, sessions or notes.
/// </summary>
public static class Duplicates
{
    /// <summary>Key of a pair the user said is not a duplicate.</summary>
    public static string PairKey(Game a, Game b) =>
        string.CompareOrdinal(a.Id, b.Id) < 0 ? $"{a.Id}|{b.Id}" : $"{b.Id}|{a.Id}";

    /// <summary>Groups of games that are the same game (2 or more each, best one to keep first).</summary>
    public static List<List<Game>> Find(IReadOnlyList<Game> games, ICollection<string>? notDuplicates = null)
    {
        var parent = Enumerable.Range(0, games.Count).ToArray();
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
        void Join(int a, int b)
        {
            if (a == b || notDuplicates?.Contains(PairKey(games[a], games[b])) == true) return;
            parent[Root(a)] = Root(b);
        }

        // same value of a key → same game
        void ByKey(Func<Game, string?> key)
        {
            var first = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < games.Count; i++)
            {
                var k = key(games[i]);
                if (string.IsNullOrEmpty(k)) continue;
                if (first.TryGetValue(k, out var j)) Join(j, i);
                else first[k] = i;
            }
        }

        ByKey(g => ExeOf(g) is { } exe ? "exe|" + exe : null);
        ByKey(g => LaunchHelper.IsUri(g.LaunchTarget) ? $"uri|{g.LaunchTarget.Trim()}|{g.Arguments?.Trim()}" : null);
        ByKey(g => g.Store != GameStore.Emulator && g.SteamAppId is int id ? "steam|" + id : null);
        ByKey(g => g.StoreId != null && g.Store is not (GameStore.Local or GameStore.Emulator or GameStore.Unknown) ? $"store|{g.Store}|{g.StoreId}" : null);
        ByKey(g => FolderOf(g) is { } dir ? "dir|" + dir : null);
        // same name, except two emulated games (the same title on two consoles)
        ByKey(g => g.Store != GameStore.Emulator && NameMatcher.Compact(g.Name) is { Length: >= 3 } n ? "name|" + n : null);

        // an exe inside another game's folder (walks up the exe's folders: no game × game comparison)
        var folders = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < games.Count; i++)
            if (FolderOf(games[i]) is { } dir)
            {
                if (!folders.TryGetValue(dir, out var list)) folders[dir] = list = new List<int>();
                list.Add(i);
            }
        for (var i = 0; i < games.Count && folders.Count > 0; i++)
        {
            if (ExeOf(games[i]) is not { } exe) continue;
            var up = Path.GetDirectoryName(exe);
            for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(up); depth++, up = Path.GetDirectoryName(up))
                if (folders.TryGetValue(up.TrimEnd('\\'), out var owners))
                    foreach (var j in owners)
                        if (j != i) Join(j, i);
        }

        return Enumerable.Range(0, games.Count)
            .GroupBy(Root)
            .Where(g => g.Count() > 1)
            .Select(g => g.Select(i => games[i]).OrderByDescending(Score).ToList())
            .ToList();
    }

    /// <summary>The copy to keep by default: the one that works, installed, the most played, with artwork.</summary>
    public static double Score(Game g) =>
        (g.IsMissing ? 0 : 1_000_000_000_000)
        + (g.IsNotInstalled ? 0 : 100_000_000_000)
        + g.PlayTimeSeconds * 10
        + (g.HasCover && !g.CoverGenerated ? 5 : 0)
        + (g.Store is GameStore.Local or GameStore.Unknown ? 0 : 2)
        - (g.AddedAt - DateTime.UnixEpoch).TotalDays / 1_000_000;

    private static string? ExeOf(Game g)
    {
        var path = g.TargetPath is { Length: > 0 } t ? t : g.LaunchTarget;
        if (string.IsNullOrWhiteSpace(path) || LaunchHelper.IsUri(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        // launchers shared by many games are not the game
        var name = Path.GetFileName(path);
        if (name.Equals("Battle.net.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase)
            || name.Equals("steam.exe", StringComparison.OrdinalIgnoreCase) || g.Store == GameStore.Emulator) return null;
        try { return Path.GetFullPath(path).TrimEnd('\\'); } catch { return path.Trim(); }
    }

    private static string? FolderOf(Game g)
    {
        if (string.IsNullOrWhiteSpace(g.InstallDir) || g.Store == GameStore.Emulator) return null;
        var dir = g.InstallDir.TrimEnd('\\');
        // games started from a shortcut can carry the shortcut's folder (e.g. Arcadia's own copies): not a game folder
        if (g.LaunchTarget.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || g.LaunchTarget.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.IsNullOrEmpty(AppPaths.DataDir) && (dir + "\\").StartsWith(AppPaths.DataDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return null;
        return ProcessWatcher.IsWatchable(dir) ? dir : null;
    }

    /// <summary>
    /// Merges <paramref name="others"/> into <paramref name="keep"/>: play time (sessions of both, without counting a
    /// session twice), favourite, collections, notes, progress, rating, achievements and missing artwork.
    /// The other entries can then be removed from the library (their artwork moved over is not deleted).
    /// </summary>
    public static void Merge(Game keep, IEnumerable<Game> others)
    {
        foreach (var o in others)
        {
            var extra = 0L;
            foreach (var s in o.Sessions)
            {
                if (keep.Sessions.Any(k => Math.Abs((k.Start - s.Start).TotalSeconds) < 120)) continue;
                keep.Sessions.Add(s);
                extra += s.Seconds;
            }
            keep.Sessions.Sort((a, b) => a.Start.CompareTo(b.Start));
            keep.PlayTimeSeconds = Math.Max(Math.Max(keep.PlayTimeSeconds, o.PlayTimeSeconds), keep.PlayTimeSeconds + extra);
            keep.PlayCount += o.PlayCount;
            if (o.LastPlayedAt is DateTime last && (keep.LastPlayedAt == null || last > keep.LastPlayedAt)) keep.LastPlayedAt = last;
            if (o.AddedAt < keep.AddedAt) keep.AddedAt = o.AddedAt;
            keep.IsFavorite |= o.IsFavorite;
            var collections = keep.Collections.Union(o.Collections, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (collections.Count != keep.Collections.Count) keep.Collections = collections;
            if (!string.IsNullOrWhiteSpace(o.Notes) && keep.Notes?.Contains(o.Notes.Trim()) != true)
                keep.Notes = string.IsNullOrWhiteSpace(keep.Notes) ? o.Notes : keep.Notes.TrimEnd() + "\n\n" + o.Notes.Trim();
            if (o.Status > keep.Status && keep.Status != GameStatus.Finished) keep.Status = o.Status;
            if (keep.Rating == 0) keep.Rating = o.Rating;
            keep.SteamAppId ??= o.SteamAppId;
            MergeAchievements(keep, o);

            // artwork the kept game does not have
            if ((!keep.HasCover || keep.CoverGenerated) && o.HasCover && !o.CoverGenerated) { keep.Cover = o.Cover; keep.CoverGenerated = false; keep.CustomCover = o.CustomCover; o.Cover = null; }
            if (keep.Hero == null && o.Hero != null) { keep.Hero = o.Hero; o.Hero = null; }
            if (keep.Logo == null && o.Logo != null) { keep.Logo = o.Logo; o.Logo = null; }
            if (keep.Icon == null && o.Icon != null) { keep.Icon = o.Icon; o.Icon = null; }
        }
    }

    private static void MergeAchievements(Game keep, Game other)
    {
        if (other.Achievements is not { Count: > 0 } theirs) return;
        if (keep.Achievements is not { Count: > 0 } mine)
        {
            keep.AchievementsAppId ??= other.AchievementsAppId;
            keep.Achievements = theirs;
            return;
        }
        var changed = false;
        foreach (var a in mine)
        {
            if (a.UnlockedAt != null) continue;
            var match = theirs.FirstOrDefault(t => t.Key == a.Key && t.UnlockedAt != null);
            if (match == null) continue;
            a.UnlockedAt = match.UnlockedAt;
            changed = true;
        }
        if (changed) keep.RefreshAchievementProgress();
    }
}
