using Arcadia.Models;
using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class PeriodStatsTests
{
    private static Game G(string name, params (DateTime Start, long Seconds)[] sessions)
    {
        var g = new Game { Name = name, LaunchTarget = name + ".exe", AddedAt = new DateTime(2026, 1, 1) };
        foreach (var (start, seconds) in sessions) g.Sessions.Add(new PlaySession { Start = start, Seconds = seconds });
        return g;
    }

    [Fact]
    public void RangesOfEachPeriod()
    {
        var d = new DateTime(2026, 11, 19, 15, 0, 0); // a Thursday
        Assert.Equal((new DateTime(2026, 11, 19), new DateTime(2026, 11, 20)), (PeriodStats.RangeOf(StatsPeriod.Day, d).Start, PeriodStats.RangeOf(StatsPeriod.Day, d).End));
        Assert.Equal(new DateTime(2026, 11, 16), PeriodStats.RangeOf(StatsPeriod.Week, d).Start); // Monday
        Assert.Equal(new DateTime(2026, 12, 1), PeriodStats.RangeOf(StatsPeriod.Month, d).End);
        Assert.Equal(new DateTime(2027, 1, 1), PeriodStats.RangeOf(StatsPeriod.Year, d).End);
        Assert.Equal(new DateTime(2026, 10, 19), PeriodStats.Shift(StatsPeriod.Month, d, -1).Date);
    }

    [Fact]
    public void ASessionOverMidnightIsSplitBetweenTheTwoDays()
    {
        var game = G("Elden Ring", (new DateTime(2026, 11, 18, 23, 0, 0), 2 * 3600)); // 23:00 → 01:00
        var day18 = PeriodStats.Build(new[] { game }, PeriodStats.RangeOf(StatsPeriod.Day, new DateTime(2026, 11, 18)), new DateTime(2026, 11, 20));
        var day19 = PeriodStats.Build(new[] { game }, PeriodStats.RangeOf(StatsPeriod.Day, new DateTime(2026, 11, 19)), new DateTime(2026, 11, 20));
        Assert.Equal(3600, day18.Seconds);
        Assert.Equal(3600, day19.Seconds);
        Assert.Equal(1, day18.Sessions);  // played on both days: a session on each
        Assert.Equal(1, day19.Sessions);
        Assert.Equal(3600, day18.Buckets[23].Seconds);
        Assert.Equal(3600, day19.Buckets[0].Seconds);
    }

    [Fact]
    public void AWeekWithItsGamesComparisonAndHabits()
    {
        var games = new[]
        {
            G("Balatro", (new DateTime(2026, 11, 16, 20, 0, 0), 3600), (new DateTime(2026, 11, 17, 21, 0, 0), 1800)),
            G("MiSide", (new DateTime(2026, 11, 21, 14, 0, 0), 2 * 3600)),
            G("Old", (new DateTime(2026, 11, 10, 20, 0, 0), 1800)), // the week before
        };
        var week = PeriodStats.Build(games, PeriodStats.RangeOf(StatsPeriod.Week, new DateTime(2026, 11, 19)), new DateTime(2026, 11, 23, 12, 0, 0));

        Assert.Equal(3600 + 1800 + 7200, week.Seconds);
        Assert.Equal(1800, week.PreviousSeconds);
        Assert.Equal(3, week.Sessions);
        Assert.Equal(2, week.GamesPlayed);
        Assert.Equal("MiSide", week.Games[0].Game.Name);
        Assert.Equal(3, week.DaysPlayed);
        Assert.Equal(7, week.Buckets.Count);
        Assert.Equal(7200, week.Buckets[5].Seconds);   // Saturday
        Assert.Equal(7200, week.ByWeekday[5]);
        Assert.Equal(3600, week.ByHour[20]);
        Assert.Equal(new DateTime(2026, 11, 21), week.BestDay);
        Assert.Equal(7, week.DaysElapsed);
    }

    [Fact]
    public void AYearHasTwelveMonthsAndHiddenGamesDoNotCount()
    {
        var hidden = G("Caché", (new DateTime(2026, 3, 1, 20, 0, 0), 9999));
        hidden.IsHidden = true;
        var games = new[] { G("A", (new DateTime(2026, 3, 1, 20, 0, 0), 600), (new DateTime(2026, 11, 1, 20, 0, 0), 600)), hidden };
        var year = PeriodStats.Build(games, PeriodStats.RangeOf(StatsPeriod.Year, new DateTime(2026, 6, 1)), new DateTime(2026, 11, 23));
        Assert.Equal(12, year.Buckets.Count);
        Assert.Equal(600, year.Buckets[2].Seconds);
        Assert.Equal(600, year.Buckets[10].Seconds);
        Assert.Equal(1200, year.Seconds);
    }

    [Fact]
    public void Titles()
    {
        var today = new DateTime(2026, 11, 23);
        Assert.Equal("Aujourd'hui", PeriodStats.Title(PeriodStats.RangeOf(StatsPeriod.Day, today), today));
        Assert.Equal("Cette semaine", PeriodStats.Title(PeriodStats.RangeOf(StatsPeriod.Week, today), today));
        Assert.Equal("La semaine dernière", PeriodStats.Title(PeriodStats.RangeOf(StatsPeriod.Week, today.AddDays(-7)), today));
        Assert.Equal("Semaine du 2 au 8 novembre", PeriodStats.Title(PeriodStats.RangeOf(StatsPeriod.Week, new DateTime(2026, 11, 4)), today));
        Assert.Equal("Novembre 2026", PeriodStats.Title(PeriodStats.RangeOf(StatsPeriod.Month, today), today));
    }
}
