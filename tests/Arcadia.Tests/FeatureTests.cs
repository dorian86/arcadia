using Arcadia.Models;
using Arcadia.Services;
using Arcadia.ViewModels;
using Xunit;

namespace Arcadia.Tests;

public class StoreTests
{
    [Fact]
    public void PublisherIdMatchesWindowsAlgorithm() =>
        // Known value: every Microsoft-signed package family name ends with _8wekyb3d8bbwe
        Assert.Equal("8wekyb3d8bbwe", StoreScanner.PublisherId("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));

    [Theory]
    [InlineData("steam://rungameid/620", null, GameStore.Steam)]
    [InlineData("uplay://launch/53/0", null, GameStore.Ubisoft)]
    [InlineData("com.epicgames.launcher://apps/x?action=launch", null, GameStore.Epic)]
    [InlineData(@"F:\SteamLibrary\steamapps\common\Teardown\teardown.exe", null, GameStore.Steam)]
    [InlineData(@"F:\Emu\yuzu\yuzu.exe", "-g \"F:/jeux/Smash.xci\"", GameStore.Emulator)]
    [InlineData(@"E:\JEUX\Mindustry\Mindustry.exe", null, GameStore.Local)]
    public void DetectsPlatform(string target, string? args, GameStore expected)
    {
        var g = new Game { LaunchTarget = target, Arguments = args, TargetPath = target.Contains("://") ? null : target, SourceKind = GameSourceKind.Executable };
        Assert.Equal(expected, StoreScanner.Detect(g));
    }

    [Fact]
    public void AGameIdentifiedOnSteamIsNotASteamGameForThat()
    {
        // Xbox / Game Pass shortcut (no file target), identified as "Forza Horizon 6" on Steam
        var xbox = new Game
        {
            SourceKind = GameSourceKind.Shortcut, LaunchTarget = @"C:\Users\x\AppData\Roaming\Arcadia\shortcuts\abc.lnk",
            SteamAppId = 2483190,
        };
        Assert.False(StoreScanner.LaunchesThroughSteam(xbox));
        Assert.Equal(GameStore.Xbox, StoreScanner.Detect(xbox));
        Assert.False(new Game { Store = StoreScanner.Detect(xbox) }.CanHaveAchievements);

        // an Epic link identified on Steam stays an Epic game
        Assert.Equal(GameStore.Epic, StoreScanner.Detect(new Game { LaunchTarget = "com.epicgames.launcher://apps/x?action=launch", SteamAppId = 620 }));

        // "steam.exe -applaunch 620" is a Steam game
        var applaunch = new Game { SourceKind = GameSourceKind.Shortcut, LaunchTarget = @"C:\Program Files (x86)\Steam\steam.exe", TargetPath = @"C:\Program Files (x86)\Steam\steam.exe", Arguments = "-applaunch 620", SteamAppId = 620 };
        Assert.Equal(GameStore.Steam, StoreScanner.Detect(applaunch));
    }

    [Fact]
    public void LoadingCorrectsGamesWronglyMarkedAsSteam()
    {
        var forza = new Game { Name = "Forza Horizon 6", SourceKind = GameSourceKind.Shortcut, LaunchTarget = @"C:\a\shortcuts\f.lnk", SteamAppId = 2483190, Store = GameStore.Steam };
        Assert.True(StoreScanner.CorrectStore(forza));
        Assert.Equal(GameStore.Xbox, forza.Store);

        var portal = new Game { Name = "Portal 2", SourceKind = GameSourceKind.InternetShortcut, LaunchTarget = "steam://rungameid/620", SteamAppId = 620, Store = GameStore.Steam };
        Assert.False(StoreScanner.CorrectStore(portal));
        Assert.Equal(GameStore.Steam, portal.Store);

        // a platform found by the store scan is never second-guessed
        var epic = new Game { LaunchTarget = @"E:\Jeux\x.exe", TargetPath = @"E:\Jeux\x.exe", SourceKind = GameSourceKind.Executable, Store = GameStore.Epic };
        Assert.False(StoreScanner.CorrectStore(epic));
    }

    [Fact]
    public void NeverWatchesSystemFolders()
    {
        Assert.False(ProcessWatcher.IsWatchable(@"C:\"));
        Assert.False(ProcessWatcher.IsWatchable(Environment.GetFolderPath(Environment.SpecialFolder.System)));
        Assert.False(ProcessWatcher.IsWatchable(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.True(ProcessWatcher.IsWatchable(@"F:\SteamLibrary\steamapps\common\Teardown"));
        Assert.Equal(@"E:\JEUX\TJoC_SM", ProcessWatcher.GameFolderOf(@"E:\JEUX\TJoC_SM\Binaries\Win64\TJoC_SM-Win64-Shipping.exe"));
    }

    [Fact]
    public void StatsAggregateSessions()
    {
        var a = new Game { Name = "A", PlayTimeSeconds = 7200, LastPlayedAt = DateTime.Now };
        a.Sessions.Add(new PlaySession { Start = DateTime.Now.AddHours(-3), Seconds = 3600 });
        a.Sessions.Add(new PlaySession { Start = DateTime.Now.AddDays(-1), Seconds = 3600 });
        var b = new Game { Name = "B", PlayTimeSeconds = 1800, LastPlayedAt = DateTime.Now };
        b.Sessions.Add(new PlaySession { Start = DateTime.Now.AddDays(-20), Seconds = 1800 });
        var stats = new StatsViewModel(new List<Game> { a, b, new Game { Name = "C" } });
        Assert.Equal("2 h 30", stats.TotalTime);
        Assert.Equal("2 / 3", stats.PlayedCount);
        Assert.Equal("A", stats.TopGames[0].Label);
        Assert.Equal(1.0, stats.TopGames[0].Ratio);
        Assert.True(stats.HasActivity);
        Assert.Equal(12, stats.Weeks.Count);
    }
}
