using Arcadia.Models;
using Arcadia.Services;
using Arcadia.Services.Online;
using Xunit;

namespace Arcadia.Tests;

public class NewsTests
{
    [Fact]
    public void ReadsOfficialAnnouncements()
    {
        const string json = """
        {"appnews":{"appid":2379780,"newsitems":[
          {"gid":"1831432155580317","title":"Steam Deckbuilders Fest &amp; Balatro","date":1778000000,
           "contents":"[h1]Deckbuilders Fest![/h1][p]Enjoy 20% off during Steam Deckbuilder Fest.[/p][p][img src=\"{STEAM_CLAN_IMAGE}/44150325/d899.jpg\"][/img][/p][url=\"https://youtooz.com\"]https://youtooz.com[/url]"},
          {"gid":"2","title":"No picture","date":1700000000,"contents":"Just <b>text</b> here."},
          {"title":"no gid"}
        ]}}
        """;
        var news = SteamNews.Parse(json, 2379780);
        Assert.Equal(2, news.Count);
        Assert.Equal("Steam Deckbuilders Fest & Balatro", news[0].Title);
        Assert.Equal("https://store.steampowered.com/news/app/2379780/view/1831432155580317", news[0].Url);
        Assert.Equal("https://clan.akamai.steamstatic.com/images/44150325/d899.jpg", news[0].ImageUrl);
        Assert.Equal("Deckbuilders Fest! Enjoy 20% off during Steam Deckbuilder Fest.", news[0].Excerpt);
        Assert.Null(news[1].ImageUrl);
        Assert.Equal("Just text here.", news[1].Excerpt);
    }

    [Fact]
    public void ExcerptsAreShortened()
    {
        var text = string.Join(" ", Enumerable.Repeat("mot", 200));
        var excerpt = SteamNews.Excerpt(text, 50);
        Assert.True(excerpt.Length <= 51);
        Assert.EndsWith("…", excerpt);
        Assert.Equal("https://x/a.jpg", SteamNews.FirstImage("[img]https://x/a.jpg[/img]"));
    }
}

public class TrophyTests
{
    private static Game Played(string name, double hours, params (DateTime Start, double Hours)[] sessions) => new()
    {
        Name = name,
        PlayTimeSeconds = (long)(hours * 3600),
        Sessions = sessions.Select(s => new PlaySession { Start = s.Start, Seconds = (long)(s.Hours * 3600) }).ToList(),
    };

    [Fact]
    public void StreaksAndVariety()
    {
        var d = new DateTime(2026, 3, 1, 20, 0, 0);
        Assert.Equal(3, TrophyStats.Streak(new[] { d, d.AddDays(1), d.AddDays(1), d.AddDays(2), d.AddDays(5) }));
        Assert.Equal(0, TrophyStats.Streak(Array.Empty<DateTime>()));
        Assert.Equal(2, TrophyStats.GamesInAWeek(new[] { (d, "a"), (d.AddDays(1), "b"), (d.AddDays(8), "c"), (d.AddDays(9), "a") }));
    }

    [Fact]
    public void GrantsTrophiesOnceAndThePlatinumLast()
    {
        var file = Path.Combine(Path.GetTempPath(), $"arcadia-trophies-{Guid.NewGuid():N}.json");
        try
        {
            var night = new DateTime(2026, 3, 1, 1, 30, 0);
            var games = new List<Game>
            {
                Played("A", 120, (night, 3.5)),
                Played("B", 5),
            };
            games[0].IsFavorite = true;
            games[0].Status = GameStatus.Finished;

            var service = new TrophyService(file);
            Assert.False(service.Initialized);
            var first = service.Check(games, collections: 0);
            var ids = first.Select(t => t.Id).ToHashSet();
            Assert.Contains("first-launch", ids);
            Assert.Contains("hours-100", ids);
            Assert.Contains("centurion", ids);
            Assert.Contains("marathon", ids);
            Assert.Contains("night", ids);
            Assert.Contains("favorite", ids);
            Assert.Contains("finished-1", ids);
            Assert.DoesNotContain("library-10", ids);
            Assert.DoesNotContain("platinum", ids);

            // nothing granted twice; saved and reloaded
            Assert.Empty(service.Check(games, 0));
            var reloaded = new TrophyService(file);
            Assert.True(reloaded.Initialized);
            Assert.Equal(first.Count, reloaded.Unlocked.Count);

            // trying a console theme
            reloaded.MarkThemeTried();
            Assert.Contains(reloaded.Check(games, 0), t => t.Id == "theme");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ProgressIsMeasured()
    {
        var stats = TrophyStats.From(new List<Game> { Played("A", 37), Played("B", 2) }, collections: 1, themeTried: false);
        var hours = TrophyService.All.Single(t => t.Id == "hours-100").Progress(stats);
        Assert.Equal((39, 100), (Math.Round(hours.Value), hours.Target));
        Assert.Equal(1, TrophyService.All.Single(t => t.Id == "collection").Progress(stats).Value);
    }
}

public class RecapTests
{
    [Fact]
    public void SummarisesTheYear()
    {
        var a = new Game { Name = "Elden Ring", AddedAt = new DateTime(2026, 1, 3) };
        a.Sessions.Add(new PlaySession { Start = new DateTime(2026, 3, 7, 21, 0, 0), Seconds = 3 * 3600 }); // Saturday evening
        a.Sessions.Add(new PlaySession { Start = new DateTime(2026, 3, 14, 22, 0, 0), Seconds = 2 * 3600 });
        a.Sessions.Add(new PlaySession { Start = new DateTime(2025, 12, 31, 22, 0, 0), Seconds = 9 * 3600 }); // other year
        var b = new Game { Name = "Balatro", AddedAt = new DateTime(2025, 5, 1) };
        b.Sessions.Add(new PlaySession { Start = new DateTime(2026, 6, 2, 9, 0, 0), Seconds = 3600 });

        var recap = YearRecap.Build(new[] { a, b }, 2026, today: new DateTime(2026, 11, 19));
        Assert.Equal(6 * 3600, recap.Seconds);
        Assert.Equal(3, recap.Sessions);
        Assert.Equal(a, recap.Games[0].Game);
        Assert.Equal(5 * 3600, recap.Games[0].Seconds);
        Assert.Equal(5 * 3600, recap.Months[2]);
        Assert.Equal(3 * 3600, recap.Longest!.Value.Session.Seconds);
        Assert.Equal(DayOfWeek.Saturday, recap.FavoriteDay);
        Assert.Equal(3, recap.FavoriteMoment); // evening
        Assert.Equal(1, recap.GamesAdded);
        Assert.Equal(new[] { 2026, 2025 }, YearRecap.Years(new[] { a, b }, new DateTime(2026, 11, 19)));
        Assert.False(YearRecap.Build(new[] { b }, 2024, today: new DateTime(2026, 11, 19)).HasPlay);
    }
}
