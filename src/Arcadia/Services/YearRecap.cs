using Arcadia.Models;

namespace Arcadia.Services;

/// <summary>
/// "Ton année de jeu": what was played during a year, from the sessions recorded by Arcadia (games launched
/// from it), plus the games added, achievements and trophies obtained that year.
/// </summary>
public sealed class YearRecap
{
    public int Year { get; init; }
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public long Seconds { get; init; }
    public int Sessions { get; init; }
    public List<(Game Game, long Seconds, int Sessions)> Games { get; init; } = new();
    public long[] Months { get; init; } = new long[12];
    public (Game Game, PlaySession Session)? Longest { get; init; }
    public DayOfWeek? FavoriteDay { get; init; }
    /// <summary>0 night (0-6 h), 1 morning, 2 afternoon, 3 evening (18-24 h).</summary>
    public int? FavoriteMoment { get; init; }
    public int GamesAdded { get; init; }
    public int AchievementsUnlocked { get; init; }
    public int TrophiesUnlocked { get; init; }

    public bool HasPlay => Sessions > 0;

    public static YearRecap Build(IEnumerable<Game> games, int year, IReadOnlyDictionary<string, DateTime>? trophies = null, DateTime? today = null)
    {
        var now = today ?? DateTime.Now;
        var from = new DateTime(year, 1, 1);
        var to = year == now.Year ? now.Date : new DateTime(year, 12, 31);
        var list = games.Where(g => g.IsShown).ToList();
        var sessions = list.SelectMany(g => g.Sessions.Where(s => s.Start.Year == year && s.Seconds > 0).Select(s => (Game: g, Session: s))).ToList();

        var months = new long[12];
        foreach (var (_, s) in sessions) months[s.Start.Month - 1] += s.Seconds;

        var byDay = sessions.GroupBy(s => s.Session.Start.DayOfWeek).Select(g => (Day: g.Key, Seconds: g.Sum(x => x.Session.Seconds))).ToList();
        var byMoment = sessions.GroupBy(s => Moment(s.Session.Start.Hour)).Select(g => (Moment: g.Key, Seconds: g.Sum(x => x.Session.Seconds))).ToList();
        var longest = sessions.OrderByDescending(s => s.Session.Seconds).FirstOrDefault();

        return new YearRecap
        {
            Year = year,
            From = from,
            To = to,
            Seconds = sessions.Sum(s => s.Session.Seconds),
            Sessions = sessions.Count,
            Games = sessions.GroupBy(s => s.Game)
                .Select(g => (g.Key, g.Sum(x => x.Session.Seconds), g.Count()))
                .OrderByDescending(x => x.Item2).ToList(),
            Months = months,
            Longest = longest.Game != null ? (longest.Game, longest.Session) : null,
            FavoriteDay = byDay.Count > 0 ? byDay.MaxBy(d => d.Seconds).Day : null,
            FavoriteMoment = byMoment.Count > 0 ? byMoment.MaxBy(m => m.Seconds).Moment : null,
            GamesAdded = list.Count(g => g.AddedAt.Year == year),
            AchievementsUnlocked = list.Where(g => g.HasAchievements).Sum(g => g.Achievements!.Count(a => a.UnlockedAt is DateTime d && d.Year == year)),
            TrophiesUnlocked = trophies?.Values.Count(d => d.Year == year) ?? 0,
        };
    }

    private static int Moment(int hour) => hour switch { < 6 => 0, < 12 => 1, < 18 => 2, _ => 3 };

    /// <summary>Years that have at least one session, plus the current one, most recent first.</summary>
    public static List<int> Years(IEnumerable<Game> games, DateTime? today = null)
    {
        var now = today ?? DateTime.Now;
        return games.SelectMany(g => g.Sessions).Select(s => s.Start.Year).Append(now.Year)
            .Distinct().Where(y => y <= now.Year).OrderByDescending(y => y).ToList();
    }
}
