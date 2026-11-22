using System.Globalization;
using Arcadia.Models;

namespace Arcadia.Services;

public enum StatsPeriod { Day, Week, Month, Year, All }

/// <summary>A period [Start, End) of the statistics page.</summary>
public sealed record PeriodRange(StatsPeriod Kind, DateTime Start, DateTime End)
{
    public int Days => Math.Max(1, (int)Math.Round((End - Start).TotalDays));
}

/// <summary>One bar of the period chart (an hour, a day, a month or a year).</summary>
public sealed record PeriodBucket(DateTime Start, DateTime End, string Label, string LongLabel)
{
    public long Seconds { get; set; }
    public HashSet<Game> Games { get; } = new();
}

/// <summary>Time played on one game during the period.</summary>
public sealed record GamePeriodTime(Game Game, long Seconds, int Sessions);

/// <summary>Everything the statistics page shows for one period.</summary>
public sealed class PeriodReport
{
    public required PeriodRange Range { get; init; }
    public long Seconds { get; init; }
    public long PreviousSeconds { get; init; }
    public int Sessions { get; init; }
    public int GamesPlayed { get; init; }
    public int NewGames { get; init; }
    public int FirstLaunches { get; init; }
    public int DaysPlayed { get; init; }
    /// <summary>Days of the period up to today (the average per day does not count the days still to come).</summary>
    public int DaysElapsed { get; init; }
    public long LongestSession { get; init; }
    public Game? LongestSessionGame { get; init; }
    public DateTime? BestDay { get; init; }
    public long BestDaySeconds { get; init; }
    public required List<PeriodBucket> Buckets { get; init; }
    public required List<GamePeriodTime> Games { get; init; }
    /// <summary>Seconds per day of the week, Monday first.</summary>
    public required long[] ByWeekday { get; init; }
    /// <summary>Seconds per hour of the day (0-23).</summary>
    public required long[] ByHour { get; init; }
}

/// <summary>
/// Statistics per day, week, month or year, computed from the play sessions measured by Arcadia (launches from
/// Arcadia and games started outside it). A session over several hours or days is split across them.
/// </summary>
public static class PeriodStats
{
    private static CultureInfo Fr => Format.Fr;

    public static PeriodRange RangeOf(StatsPeriod kind, DateTime anchor, DateTime? firstSession = null)
    {
        var d = anchor.Date;
        return kind switch
        {
            StatsPeriod.Day => new(kind, d, d.AddDays(1)),
            StatsPeriod.Week => new(kind, Monday(d), Monday(d).AddDays(7)),
            StatsPeriod.Month => new(kind, new DateTime(d.Year, d.Month, 1), new DateTime(d.Year, d.Month, 1).AddMonths(1)),
            StatsPeriod.Year => new(kind, new DateTime(d.Year, 1, 1), new DateTime(d.Year + 1, 1, 1)),
            _ => new(kind, new DateTime((firstSession ?? DateTime.Today).Year, 1, 1), new DateTime(DateTime.Today.Year + 1, 1, 1)),
        };
    }

    public static DateTime Monday(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>The same kind of period, <paramref name="step"/> periods before (-1) or after (+1).</summary>
    public static DateTime Shift(StatsPeriod kind, DateTime anchor, int step) => kind switch
    {
        StatsPeriod.Day => anchor.AddDays(step),
        StatsPeriod.Week => anchor.AddDays(7 * step),
        StatsPeriod.Month => anchor.AddMonths(step),
        StatsPeriod.Year => anchor.AddYears(step),
        _ => anchor,
    };

    /// <summary>"Aujourd'hui", "Semaine du 16 au 22 novembre", "Novembre 2026", "2026", "Depuis le début"…</summary>
    public static string Title(PeriodRange r, DateTime today)
    {
        switch (r.Kind)
        {
            case StatsPeriod.Day:
                if (r.Start == today.Date) return "Aujourd'hui";
                if (r.Start == today.Date.AddDays(-1)) return "Hier";
                return Capitalize(r.Start.ToString(r.Start.Year == today.Year ? "dddd d MMMM" : "dddd d MMMM yyyy", Fr));
            case StatsPeriod.Week:
                var last = r.End.AddDays(-1);
                if (r.Start == Monday(today)) return "Cette semaine";
                if (r.Start == Monday(today).AddDays(-7)) return "La semaine dernière";
                var from = r.Start.Month == last.Month ? r.Start.ToString("%d", Fr) : r.Start.ToString("d MMMM", Fr);
                return $"Semaine du {from} au {last.ToString(last.Year == today.Year ? "d MMMM" : "d MMMM yyyy", Fr)}";
            case StatsPeriod.Month:
                return Capitalize(r.Start.ToString("MMMM yyyy", Fr));
            case StatsPeriod.Year:
                return r.Start.Year.ToString(Fr);
            default:
                return "Depuis le début";
        }
    }

    /// <summary>"la veille", "la semaine précédente", "le mois précédent", "l'année précédente".</summary>
    public static string PreviousName(StatsPeriod kind) => kind switch
    {
        StatsPeriod.Day => "la veille",
        StatsPeriod.Week => "la semaine précédente",
        StatsPeriod.Month => "le mois précédent",
        StatsPeriod.Year => "l'année précédente",
        _ => "",
    };

    public static PeriodReport Build(IReadOnlyList<Game> allGames, PeriodRange range, DateTime now)
    {
        var games = allGames.Where(g => !g.IsHidden).ToList();
        var buckets = Buckets(range);
        var perGame = new Dictionary<Game, (long Seconds, int Sessions)>();
        var perDay = new Dictionary<DateTime, long>();
        var byWeekday = new long[7];
        var byHour = new long[24];
        long total = 0, longest = 0;
        int sessions = 0;
        Game? longestGame = null;

        foreach (var g in games)
        {
            foreach (var s in g.Sessions)
            {
                var start = s.Start;
                var end = s.Start.AddSeconds(s.Seconds);
                if (end <= range.Start || start >= range.End || s.Seconds <= 0) continue;
                var from = start < range.Start ? range.Start : start;
                var to = end > range.End ? range.End : end;
                var inRange = (long)(to - from).TotalSeconds;
                if (inRange <= 0) continue;

                total += inRange;
                sessions++; // every session played during the period, even one started the day before
                var (gs, gn) = perGame.GetValueOrDefault(g);
                perGame[g] = (gs + inRange, gn + 1);
                if (s.Seconds > longest) { longest = s.Seconds; longestGame = g; }

                // split per hour (days, weekdays, hours of the day and the chart's bars follow)
                for (var t = from; t < to;)
                {
                    var next = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0).AddHours(1);
                    if (next > to) next = to;
                    var sec = (long)(next - t).TotalSeconds;
                    perDay[t.Date] = perDay.GetValueOrDefault(t.Date) + sec;
                    byWeekday[((int)t.DayOfWeek + 6) % 7] += sec;
                    byHour[t.Hour] += sec;
                    var b = buckets.FirstOrDefault(x => t >= x.Start && t < x.End);
                    if (b != null)
                    {
                        b.Seconds += sec;
                        b.Games.Add(g);
                    }
                    t = next;
                }
            }
        }

        var previous = range.Kind == StatsPeriod.All ? 0
            : PlayedBetween(games, RangeOf(range.Kind, Shift(range.Kind, range.Start, -1)));
        var best = perDay.OrderByDescending(kv => kv.Value).FirstOrDefault();
        var elapsedEnd = range.End > now ? now.Date.AddDays(1) : range.End;
        // the average per day starts with the first session Arcadia measured (not before it existed)
        var firstEver = games.SelectMany(g => g.Sessions).Select(s => (DateTime?)s.Start.Date).Min();
        var elapsedStart = firstEver is DateTime f0 && f0 > range.Start ? f0 : range.Start;

        return new PeriodReport
        {
            Range = range,
            Seconds = total,
            PreviousSeconds = previous,
            Sessions = sessions,
            GamesPlayed = perGame.Count,
            NewGames = games.Count(g => !g.IsNotInstalled && g.AddedAt >= range.Start && g.AddedAt < range.End),
            FirstLaunches = games.Count(g => g.Sessions.Count > 0 && g.Sessions.Min(s => s.Start) is var first && first >= range.Start && first < range.End),
            DaysPlayed = perDay.Count(kv => kv.Value >= 60),
            DaysElapsed = Math.Max(1, (int)Math.Ceiling((elapsedEnd - elapsedStart).TotalDays)),
            LongestSession = longest,
            LongestSessionGame = longestGame,
            BestDay = best.Value > 0 ? best.Key : null,
            BestDaySeconds = best.Value,
            Buckets = buckets,
            Games = perGame.Select(kv => new GamePeriodTime(kv.Key, kv.Value.Seconds, kv.Value.Sessions))
                           .OrderByDescending(x => x.Seconds).ToList(),
            ByWeekday = byWeekday,
            ByHour = byHour,
        };
    }

    private static long PlayedBetween(List<Game> games, PeriodRange r)
    {
        long total = 0;
        foreach (var g in games)
            foreach (var s in g.Sessions)
            {
                var end = s.Start.AddSeconds(s.Seconds);
                var from = s.Start < r.Start ? r.Start : s.Start;
                var to = end > r.End ? r.End : end;
                if (to > from) total += (long)(to - from).TotalSeconds;
            }
        return total;
    }

    /// <summary>The bars: hours of a day, days of a week or month, months of a year, years.</summary>
    internal static List<PeriodBucket> Buckets(PeriodRange r)
    {
        var list = new List<PeriodBucket>();
        switch (r.Kind)
        {
            case StatsPeriod.Day:
                for (var h = 0; h < 24; h++)
                {
                    var s = r.Start.AddHours(h);
                    list.Add(new PeriodBucket(s, s.AddHours(1), h % 3 == 0 ? $"{h} h" : "", $"De {h} h à {h + 1} h"));
                }
                break;
            case StatsPeriod.Week:
                for (var i = 0; i < 7; i++)
                {
                    var s = r.Start.AddDays(i);
                    list.Add(new PeriodBucket(s, s.AddDays(1), Capitalize(s.ToString("ddd d", Fr)).Replace(".", ""), Capitalize(s.ToString("dddd d MMMM", Fr))));
                }
                break;
            case StatsPeriod.Month:
                for (var s = r.Start; s < r.End; s = s.AddDays(1))
                    list.Add(new PeriodBucket(s, s.AddDays(1), s.Day == 1 || s.Day % 5 == 0 ? s.Day.ToString(Fr) : "", Capitalize(s.ToString("dddd d MMMM", Fr))));
                break;
            case StatsPeriod.Year:
                for (var m = 0; m < 12; m++)
                {
                    var s = r.Start.AddMonths(m);
                    list.Add(new PeriodBucket(s, s.AddMonths(1), Capitalize(s.ToString("MMM", Fr)).Replace(".", ""), Capitalize(s.ToString("MMMM yyyy", Fr))));
                }
                break;
            default:
                for (var s = r.Start; s < r.End; s = s.AddYears(1))
                    list.Add(new PeriodBucket(s, s.AddYears(1), s.Year.ToString(Fr), s.Year.ToString(Fr)));
                break;
        }
        return list;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Fr) + s[1..];
}
