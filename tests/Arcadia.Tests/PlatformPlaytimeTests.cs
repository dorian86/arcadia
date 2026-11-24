using Arcadia.Models;
using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class PlatformPlaytimeTests
{
    private static Game G(string target, GameStore store, int? steam = 1245620, string? installDir = null) =>
        new() { Name = "Jeu", LaunchTarget = target, Store = store, SteamAppId = steam, InstallDir = installDir };

    [Fact]
    public void SteamCopiesAreTheGamesInstalledOrListedBySteam()
    {
        Assert.True(PlatformPlaytime.IsSteamCopy(G("steam://rungameid/1245620", GameStore.Steam)));
        Assert.True(PlatformPlaytime.IsSteamCopy(G(@"D:\SteamLibrary\steamapps\common\ELDEN RING\Game\eldenring.exe", GameStore.Local)));
        Assert.True(PlatformPlaytime.IsSteamCopy(G("steam://rungameid/1245620", GameStore.Local)));
        // another copy of a game sold on Steam (identified by its Steam id for the artwork): measured by Arcadia
        Assert.False(PlatformPlaytime.IsSteamCopy(G(@"E:\JEUX\MiSide\MiSide.exe", GameStore.Local, installDir: @"E:\JEUX\MiSide")));
        Assert.True(PlatformPlaytime.IsSteamCopy(G("steam://rungameid/235600", GameStore.Steam, steam: null))); // id read from the link
        Assert.Equal(235600, PlatformPlaytime.SteamIdOf(G("steam://rungameid/235600", GameStore.Steam, steam: null)));
        Assert.False(PlatformPlaytime.IsSteamCopy(G(@"E:\JEUX\Jeu\jeu.exe", GameStore.Steam, steam: null)));
    }

    [Fact]
    public void ThePlatformFigureReplacesArcadiasEvenWhenLower()
    {
        // e.g. Wallpaper Engine: Arcadia had counted 54 h in the background, Steam says 29 h
        var g = G("steam://rungameid/431960", GameStore.Steam, 431960);
        g.PlayTimeSeconds = 194_720;
        Assert.Equal(104_880 - 194_720, PlatformPlaytime.Apply(g, GameStore.Steam, 104_880));
        Assert.Equal(104_880, g.PlayTimeSeconds);
        Assert.Equal(GameStore.Steam, g.PlaytimeSource);
        Assert.Equal(0, PlatformPlaytime.Apply(g, GameStore.Steam, 104_880));
    }

    [Fact]
    public void MergingDuplicatesKeepsThePlatformHours()
    {
        var steam = G("steam://rungameid/1245620", GameStore.Steam);
        PlatformPlaytime.Apply(steam, GameStore.Steam, 36_000);
        var copy = G(@"E:\JEUX\Elden Ring\eldenring.exe", GameStore.Local);
        copy.PlayTimeSeconds = 7_200;
        copy.Sessions.Add(new PlaySession { Start = new DateTime(2026, 11, 20, 20, 0, 0), Seconds = 7_200 });

        Duplicates.Merge(steam, new[] { copy });

        Assert.Equal(36_000, steam.PlayTimeSeconds);
        Assert.Single(steam.Sessions); // the session stays in the history (stats per period)
    }

    [Fact]
    public void ThePlatformIsSavedWithTheGame()
    {
        var g = G("steam://rungameid/1245620", GameStore.Steam);
        g.PlaytimeSource = GameStore.Epic;
        var json = System.Text.Json.JsonSerializer.Serialize(g, JsonFile.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<Game>(json, JsonFile.Options)!;
        Assert.Equal(GameStore.Epic, back.PlaytimeSource);
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<Game>("{\"Name\":\"x\"}", JsonFile.Options)!.PlaytimeSource);
    }
}
