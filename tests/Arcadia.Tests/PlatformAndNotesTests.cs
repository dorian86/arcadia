using Arcadia.Controls;
using Arcadia.Models;
using Arcadia.Services;
using Arcadia.Services.Online;
using Xunit;

namespace Arcadia.Tests;

public class PatchNotesTests
{
    private const string Releases = """
    [
      { "tag_name": "v1.1.7", "name": "Arcadia 1.1.7", "body": "- Septième", "assets": [] },
      { "tag_name": "v1.1.6", "name": "Arcadia 1.1.6", "body": "## Nouveauté\n\n- Sixième", "assets": [] },
      { "tag_name": "v1.1.5", "name": "Arcadia 1.1.5", "body": "- Cinquième", "assets": [] },
      { "tag_name": "v1.2.0", "draft": true, "body": "- Brouillon", "assets": [] }
    ]
    """;

    [Fact]
    public void JoinsTheNotesOfTheSkippedVersionsNewestFirst()
    {
        var notes = UpdateService.CombineNotes(Releases, new Version(1, 1, 5), new Version(1, 1, 7))!;
        Assert.StartsWith("# Arcadia 1.1.7", notes);
        Assert.Contains("# Arcadia 1.1.6", notes);
        Assert.DoesNotContain("Cinquième", notes);
        Assert.DoesNotContain("Brouillon", notes);
        Assert.True(notes.IndexOf("Septième", StringComparison.Ordinal) < notes.IndexOf("Sixième", StringComparison.Ordinal));
    }

    [Fact]
    public void ASingleVersionKeepsItsNotesAsTheyAre() =>
        Assert.Equal("## Nouveauté\n\n- Sixième", UpdateService.CombineNotes(Releases, new Version(1, 1, 5), new Version(1, 1, 6)));

    [Fact]
    public void ItalicsAndEmojisInReleaseNotes()
    {
        var blocks = MarkdownLite.Parse("- 📚 **Toute votre bibliothèque** : dans *Tous les jeux → Non installés*.\n- ⏱️ Temps\n- 🧹 ✨ **Doublons**");
        Assert.Equal("**Toute votre bibliothèque** : dans *Tous les jeux → Non installés*.", blocks[0].Text);
        Assert.Equal("Temps", blocks[1].Text);
        Assert.Equal("**Doublons**", blocks[2].Text);
        var spans = MarkdownLite.Spans(blocks[0].Text);
        Assert.Contains(("Toute votre bibliothèque", "b"), spans);
        Assert.Contains(("Tous les jeux → Non installés", "i"), spans);
        Assert.DoesNotContain(spans, s => s.Text.Contains('*'));
    }

    [Fact]
    public void ReadsLightMarkdown()
    {
        var blocks = MarkdownLite.Parse("# Arcadia 1.2\n\n## Nouveautés\n\n- **Gras** et `code`\n  - sous-point\n  suite du sous-point\nTexte");
        Assert.Equal(new[] { MarkdownLite.Kind.Title, MarkdownLite.Kind.Subtitle, MarkdownLite.Kind.Bullet, MarkdownLite.Kind.Bullet, MarkdownLite.Kind.Paragraph },
                     blocks.Select(b => b.Kind));
        Assert.Equal(1, blocks[3].Level);
        Assert.Equal("sous-point suite du sous-point", blocks[3].Text);
    }
}

public class MorePlatformsTests
{
    [Fact]
    public void ReadsTheEaInstallerData()
    {
        const string xml = """
        <DiPManifest version="4.0">
          <contentIDs><contentID>1026023</contentID><contentID>1026480</contentID></contentIDs>
          <gameTitles><gameTitle locale="en_US">Battlefield 1</gameTitle><gameTitle locale="fr_FR">Battlefield 1 (FR)</gameTitle></gameTitles>
          <runtime>
            <launcher><filePath>[HKEY_LOCAL_MACHINE\SOFTWARE\EA Games\Battlefield 1\Install Dir]bf1Trial.exe</filePath><trial>1</trial></launcher>
            <launcher><filePath>[HKEY_LOCAL_MACHINE\SOFTWARE\EA Games\Battlefield 1\Install Dir]bf1.exe</filePath><trial>0</trial></launcher>
          </runtime>
        </DiPManifest>
        """;
        var data = StoreScanner.ParseEaInstallerData(xml, @"D:\EA Games\Battlefield 1");
        Assert.Equal(new[] { "1026023", "1026480" }, data.ContentIds);
        Assert.Equal("Battlefield 1 (FR)", data.Title);
        Assert.Equal(@"D:\EA Games\Battlefield 1\bf1.exe", data.Executable);
    }

    [Theory]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=frFR --uid=fenris --displayname=\"Diablo IV\"", "fenris")]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --uid=prometheus", "prometheus")]
    [InlineData("MsiExec.exe /X{1234}", null)]
    public void ReadsTheBattleNetProduct(string uninstall, string? uid) =>
        Assert.Equal(uid, StoreScanner.BattleNetUid(uninstall));

    [Fact]
    public void KnowsTheLaunchCodesOfBlizzardGames()
    {
        Assert.Equal("Fen", StoreScanner.BattleNetCodes["fenris"]);
        Assert.Equal("Pro", StoreScanner.BattleNetCodes["prometheus"]);
    }

    [Fact]
    public void ReadsTheRiotProduct()
    {
        Assert.Equal(("valorant", "live"), StoreScanner.RiotProduct("\"G:\\jeux\\Riot Games\\Riot Client\\RiotClientServices.exe\" --uninstall-product=valorant --uninstall-patchline=live"));
        Assert.Null(StoreScanner.RiotProduct("\"G:\\jeux\\Riot Games\\Riot Client\\RiotClientServices.exe\" --uninstall-product=Riot_Client --uninstall-patchline="));
    }

    [Theory]
    [InlineData("origin2://game/launch?offerIds=1026023", null, GameStore.EA)]
    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe", "--exec=\"launch Fen\"", GameStore.BattleNet)]
    [InlineData(@"G:\jeux\Riot Games\Riot Client\RiotClientServices.exe", "--launch-product=valorant --launch-patchline=live", GameStore.Riot)]
    [InlineData(@"G:\jeux\Riot Games\VALORANT\live\VALORANT.exe", null, GameStore.Riot)]
    public void RecognisesTheNewPlatforms(string target, string? args, GameStore expected) =>
        Assert.Equal(expected, StoreScanner.Detect(new Game { Name = "x", LaunchTarget = target, Arguments = args }));
}

public class SteamFiguresTests
{
    [Fact]
    public void ReadsPlayersPriceAndScores()
    {
        const string details = """
        {"367520":{"success":true,"data":{"is_free":false,
          "price_overview":{"currency":"EUR","initial":1479,"final":739,"discount_percent":50,"initial_formatted":"14,79€","final_formatted":"7,39€"},
          "metacritic":{"score":87},"recommendations":{"total":509690},"achievements":{"total":63}}}}
        """;
        var stats = SteamStatsClient.Parse(367520, SteamStatsClient.ParsePlayers("""{"response":{"player_count":5151,"result":1}}"""), details);
        Assert.Equal(5151, stats.PlayersNow);
        Assert.Equal("7,39€", stats.PriceText);
        Assert.True(stats.HasDiscount);
        Assert.Equal("-50 %", stats.DiscountText);
        Assert.Equal("14,79€", stats.PriceBefore);
        Assert.Equal(87, stats.Metacritic);
        Assert.Equal(63, stats.Achievements);
        Assert.Equal("https://steamdb.info/app/367520/", stats.SteamDbUrl);
    }

    [Fact]
    public void AFreeGameAndMissingData()
    {
        var free = SteamStatsClient.Parse(570, null, """{"570":{"success":true,"data":{"is_free":true}}}""");
        Assert.Equal("Gratuit", free.PriceText);
        Assert.Equal("—", free.PlayersText);
        Assert.False(free.HasMetacritic);
        Assert.Null(SteamStatsClient.ParsePlayers("""{"response":{"result":42}}"""));
    }
}

public class OutsidePlaytimeTests
{
    [Fact]
    public void RecognisesGamesStartedOutsideArcadia()
    {
        var steam = new Game { Name = "Hollow Knight", LaunchTarget = "steam://rungameid/367520", InstallDir = @"D:\Steam\steamapps\common\Hollow Knight" };
        var loose1 = new Game { Name = "Sunky", LaunchTarget = @"E:\JEUX\Sunky.MPEG.exe", InstallDir = @"E:\JEUX" };
        var loose2 = new Game { Name = "Sonic", LaunchTarget = @"E:\JEUX\sonic.exe", InstallDir = @"E:\JEUX" };
        var bnet = new Game { Name = "Diablo IV", LaunchTarget = @"C:\Program Files (x86)\Battle.net\Battle.net.exe", Arguments = "--exec=\"launch Fen\"", InstallDir = @"D:\Diablo IV" };

        var running = BackgroundPlaytime.Match(new[] { steam, loose1, loose2, bnet }, new[]
        {
            @"D:\Steam\steamapps\common\Hollow Knight\hollow_knight.exe",
            @"E:\JEUX\Other Game\other.exe",                // in the shared folder, but no game of the library
            @"C:\Program Files (x86)\Battle.net\Battle.net.exe", // the launcher alone is not the game
            @"E:\JEUX\Sunky.MPEG.exe",
        });

        Assert.Equal(new[] { "Hollow Knight", "Sunky" }, running.Select(g => g.Name).Order());
    }

    [Fact]
    public void CrashHandlersDoNotCount()
    {
        var game = new Game { Name = "Game", LaunchTarget = "steam://rungameid/1", InstallDir = @"D:\Games\Game" };
        Assert.Empty(BackgroundPlaytime.Match(new[] { game }, new[] { @"D:\Games\Game\UnityCrashHandler64.exe" }));
    }
}
