using System.Text;
using Arcadia.Models;
using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class DuplicateTests
{
    private static Game G(string name, string target, GameStore store = GameStore.Local, string? installDir = null, int? steam = null) =>
        new() { Name = name, LaunchTarget = target, Store = store, InstallDir = installDir, SteamAppId = steam };

    [Fact]
    public void FindsTheSameGameImportedTwice()
    {
        var a = G("Hollow Knight", @"E:\JEUX\Hollow Knight\hollow_knight.exe", installDir: @"E:\JEUX\Hollow Knight");
        var b = G("Hollow Knight", @"F:\Copie\Hollow Knight\hollow_knight.exe");                // same name, another folder
        var c = G("Hollow knight.exe - Raccourci", @"E:\JEUX\Hollow Knight\hollow_knight.exe"); // same exe
        var d = G("Elden Ring", "steam://rungameid/1245620", GameStore.Steam, steam: 1245620);
        var e = G("ELDEN RING", @"D:\Steam\steamapps\common\ELDEN RING\Game\eldenring.exe", GameStore.Steam, steam: 1245620);
        var f = G("Tetris", @"C:\Emu\mgba.exe", GameStore.Emulator);
        var g = G("Tetris", @"C:\Emu\fceux.exe", GameStore.Emulator); // two consoles: not duplicates
        var h = G("Balatro", "steam://rungameid/2379780", GameStore.Steam, steam: 2379780);

        var groups = Duplicates.Find(new[] { a, b, c, d, e, f, g, h });

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, grp => grp.Count == 3 && grp.Contains(a) && grp.Contains(b) && grp.Contains(c));
        Assert.Contains(groups, grp => grp.Count == 2 && grp.Contains(d) && grp.Contains(e));
    }

    [Fact]
    public void AnExeDeepInsideAnotherGamesFolder()
    {
        var store = G("Stray", "steam://rungameid/1332010", GameStore.Steam, installDir: @"D:\Steam\steamapps\common\Stray", steam: 1332010);
        var exe = G("Hk_project", @"D:\Steam\steamapps\common\Stray\Hk_project\Binaries\Win64\Stray-Win64-Shipping.exe");
        var other = G("Other", @"D:\Steam\steamapps\common\Strayed\other.exe");
        var group = Assert.Single(Duplicates.Find(new[] { store, exe, other }));
        Assert.Equal(2, group.Count);
        Assert.DoesNotContain(other, group);
    }

    [Fact]
    public void ALargeLibraryIsCheckedQuickly()
    {
        var games = Enumerable.Range(0, 3000)
            .Select(i => G($"Game {i}", $@"E:\Jeux\Game {i}\game{i}.exe", installDir: $@"E:\Jeux\Game {i}"))
            .ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Empty(Duplicates.Find(games));
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void TheFolderOfShortcutsIsNotAGameFolder()
    {
        var cod = G("Call of Duty", @"C:\Users\x\AppData\Roaming\Arcadia\shortcuts\a.lnk", GameStore.Xbox, installDir: @"C:\Users\x\AppData\Roaming\Arcadia\shortcuts");
        var mc = G("Minecraft", @"C:\Users\x\AppData\Roaming\Arcadia\shortcuts\b.lnk", GameStore.Xbox, installDir: @"C:\Users\x\AppData\Roaming\Arcadia\shortcuts");
        Assert.Empty(Duplicates.Find(new[] { cod, mc }));
    }

    [Fact]
    public void RemembersPairsThatAreNotDuplicates()
    {
        var a = G("Geometry Dash", @"E:\JEUX\GD\GeometryDash.exe");
        var b = G("Geometry Dash", @"F:\JEUX\GD2\GeometryDash.exe");
        Assert.Single(Duplicates.Find(new[] { a, b }));
        Assert.Empty(Duplicates.Find(new[] { a, b }, new List<string> { Duplicates.PairKey(b, a) }));
    }

    [Fact]
    public void KeepsTheWorkingInstalledCopyAndMergesEverything()
    {
        var installed = G("Celeste", @"E:\JEUX\Celeste\Celeste.exe");
        installed.PlayTimeSeconds = 3600;
        installed.Sessions.Add(new PlaySession { Start = new DateTime(2026, 1, 1, 20, 0, 0), Seconds = 3600 });
        var other = G("Celeste", "steam://rungameid/504230", GameStore.Steam, steam: 504230);
        other.IsNotInstalled = true;
        other.PlayTimeSeconds = 1800;
        other.Sessions.Add(new PlaySession { Start = new DateTime(2026, 2, 1, 20, 0, 0), Seconds = 1800 });
        other.Sessions.Add(new PlaySession { Start = new DateTime(2026, 1, 1, 20, 0, 30), Seconds = 3600 }); // the same session
        other.IsFavorite = true;
        other.Notes = "Chapitre 7";
        other.Collections = new List<string> { "Plateformes" };

        var group = Assert.Single(Duplicates.Find(new[] { other, installed }));
        Assert.Same(installed, group[0]);

        Duplicates.Merge(installed, new[] { other });
        Assert.Equal(5400, installed.PlayTimeSeconds);
        Assert.Equal(2, installed.Sessions.Count);
        Assert.True(installed.IsFavorite);
        Assert.Equal("Chapitre 7", installed.Notes);
        Assert.Equal(new[] { "Plateformes" }, installed.Collections);
        Assert.Equal(504230, installed.SteamAppId);
    }
}

public class StatsWithNotInstalledTests
{
    [Fact]
    public void TheHoursOfGamesNotInstalledCountInTheStatistics()
    {
        var installed = new Game { Name = "Balatro", LaunchTarget = "steam://rungameid/2379780", PlayTimeSeconds = 4 * 3600 };
        var notInstalled = new Game { Name = "Sea of Thieves", LaunchTarget = "steam://rungameid/1172620", PlayTimeSeconds = 51 * 3600, IsNotInstalled = true };
        var hidden = new Game { Name = "Caché", LaunchTarget = "x.exe", PlayTimeSeconds = 99 * 3600, IsNotInstalled = true, IsHidden = true };

        var stats = new Arcadia.ViewModels.StatsViewModel(new List<Game> { installed, notInstalled, hidden });

        Assert.Equal(Format.Duration(55 * 3600), stats.TotalTime);
        Assert.Equal($"dont {Format.Duration(51 * 3600)} sur des jeux non installés", stats.TotalTimeHint);
        Assert.Same(notInstalled, stats.MostPlayed);
        Assert.Equal(2, stats.TopGames.Count);
        Assert.Equal("1", stats.GameCount); // the games themselves stay apart
    }
}

public class OwnedGamesTests
{
    [Fact]
    public void ReadsTheEpicLibraryWithoutAddOnsOrTools()
    {
        const string library = """
        {"items":[
          {"appId":{"namespaceId":"fn","itemId":"i1","appName":"Fortnite"},"recordType":"APPLICATION","title":"Fortnite","owned":true},
          {"appId":{"namespaceId":"civ","itemId":"i2","appName":"civ6"},"recordType":"APPLICATION","title":"Sid Meier's Civilization VI","owned":true},
          {"appId":{"namespaceId":"civ","itemId":"i3","appName":"civdlc"},"recordType":"APPLICATION","title":"Civilization VI : Gathering Storm","owned":true},
          {"appId":{"namespaceId":"ue","itemId":"i4","appName":"UE_5.0"},"recordType":"APPLICATION","title":"Unreal Engine","owned":true},
          {"appId":{"namespaceId":"x","itemId":"i5","appName":"lost"},"recordType":"APPLICATION","title":"Refunded","owned":false}
        ]}
        """;
        const string catalog = """
        {"items":{"i2":{"item":{"categories":[{"path":"games"},{"path":"applications"}]}},
                  "i3":{"item":{"categories":[{"path":"addons"}],"mainGameItem":{"id":"i2"}}}}}
        """;
        var games = OwnedGames.ParseEpicLibrary(library, catalog, new Dictionary<string, long> { ["Fortnite"] = 330345 });
        Assert.Equal(new[] { "Fortnite", "Sid Meier's Civilization VI" }, games.Select(g => g.Name));
        Assert.Equal(330345, games[0].Seconds);
        Assert.Equal(GameStore.Epic, games[0].Store);
        Assert.StartsWith("com.epicgames.launcher://apps/fn%3Ai1%3AFortnite", games[0].LaunchTarget);
    }

    [Fact]
    public void KeepsOnlyTheSteamGames()
    {
        var info = new Dictionary<int, SteamAppSummary>
        {
            [367520] = new(367520, "Hollow Knight", "game"),
            [228980] = new(228980, "Steamworks Common Redistributables", "tool"),
            [1000] = new(1000, "Some Demo", "demo"),
            [2000] = new(2000, "Game Soundtrack", "game"),
        };
        var usage = new Dictionary<int, SteamAppUsage> { [367520] = new(7200, new DateTime(2026, 3, 1)) };
        var games = OwnedGames.Select(new[] { 367520, 228980, 1000, 2000, 3000 }, info, usage);
        var hk = Assert.Single(games);
        Assert.Equal("Hollow Knight", hk.Name);
        Assert.Equal(7200, hk.Seconds);
        Assert.Equal("steam://rungameid/367520", hk.LaunchTarget);
        Assert.Equal("steam:367520", hk.Key);
    }

    [Fact]
    public void ReadsTheAchievementProgressCache() =>
        Assert.Equal(new[] { 582500, 2825210 },
            OwnedGames.ParseAchievementProgress("""{"nVersion":3,"mapCache":[[582500,{"appid":582500}],[2825210,{"appid":2825210}]]}"""));

    /// <summary>A tiny appinfo.vdf in the 2024+ format (keys in a string table).</summary>
    [Fact]
    public void ReadsNamesAndTypesFromAppInfo()
    {
        var table = new[] { "appinfo", "common", "name", "type", "appid" };
        byte[] Kv(string name, string type)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            void Key(string k) => w.Write((uint)Array.IndexOf(table, k));
            void Str(string s) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
            w.Write((byte)0); Key("appinfo");
            w.Write((byte)2); Key("appid"); w.Write(1);
            w.Write((byte)0); Key("common");
            w.Write((byte)1); Key("name"); Str(name);
            w.Write((byte)1); Key("type"); Str(type);
            w.Write((byte)8); w.Write((byte)8); w.Write((byte)8);
            return ms.ToArray();
        }
        using var file = new MemoryStream();
        using var bw = new BinaryWriter(file);
        bw.Write(0x07564429u); bw.Write(1u); bw.Write(0L); // string table offset, written below
        foreach (var (id, name, type) in new[] { (10, "Counter-Strike", "Game"), (20, "Some Tool", "Tool") })
        {
            var kv = Kv(name, type);
            bw.Write((uint)id); bw.Write((uint)(60 + kv.Length)); bw.Write(new byte[60]); bw.Write(kv);
        }
        bw.Write(0u);
        var tableAt = file.Position;
        bw.Write((uint)table.Length);
        foreach (var s in table) { bw.Write(Encoding.UTF8.GetBytes(s)); bw.Write((byte)0); }
        file.Position = 8;
        bw.Write(tableAt);

        var apps = SteamAppInfo.Parse(file.ToArray(), new HashSet<int> { 10, 20 });
        Assert.Equal("Counter-Strike", apps[10].Name);
        Assert.Equal("game", apps[10].Type);
        Assert.Equal("tool", apps[20].Type);
    }
}
