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
