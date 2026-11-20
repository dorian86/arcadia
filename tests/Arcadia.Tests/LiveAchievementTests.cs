using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

/// <summary>Achievement files written by games started without their store's launcher.</summary>
public class LiveAchievementTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arcadia-ach-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    [Fact]
    public void ReadsGoldbergJson()
    {
        var u = EmulatorAchievements.Parse("""
        { "BuildBase": { "earned": true, "earned_time": 1751130524 },
          "BuildCyclops": { "earned": false, "earned_time": 0 },
          "Secret": { "earned": true, "earned_time": 0 } }
        """);
        Assert.Equal(2, u.Count);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1751130524).LocalDateTime, u["buildbase"]);
        Assert.Equal(DateTime.MinValue, u["Secret"]);
    }

    [Fact]
    public void ReadsCodexRuneAndOnlineFixIni()
    {
        var codex = EmulatorAchievements.Parse("[BuildBase]\nAchieved=1\nCurProgress=0\nMaxProgress=0\nUnlockTime=1751130524\n\n[Other]\nAchieved=0\nUnlockTime=0\n");
        Assert.Equal(new[] { "BuildBase" }, codex.Keys);
        var onlineFix = EmulatorAchievements.Parse("[CRAFT_POD]\nachieved=true\ntimestamp=1753367119\n\n\n[NOT_YET]\nachieved=false\n");
        Assert.Equal(new[] { "CRAFT_POD" }, onlineFix.Keys);
        var cream = EmulatorAchievements.Parse("[ACH_GROUP_SLEEP]\nachieved = true\nunlocktime = 1676416\n");
        Assert.Equal(2023, cream["ACH_GROUP_SLEEP"].Year); // thousands of seconds
    }

    [Fact]
    public void ReadsTenokeUserStats()
    {
        var game = Directory.CreateDirectory(Path.Combine(_dir, "MiSide"));
        Directory.CreateDirectory(Path.Combine(game.FullName, "MiSideFull_Data", "Plugins", "x86_64"));
        File.WriteAllText(Path.Combine(game.FullName, "MiSideFull_Data", "Plugins", "x86_64", "tenoke.ini"),
            "[TENOKE]\n# appid\nid = 2527500 # MiSide\n\n# username\nuser = \"TENOKE\"\n");
        Directory.CreateDirectory(Path.Combine(game.FullName, "SteamData"));
        File.WriteAllText(Path.Combine(game.FullName, "SteamData", "user_stats.ini"),
            "[STATS]\n\"SOAP_COLLECTED\" = 2.000000\n\n[ACHIEVEMENTS]\n\"achi_mmnice\" = {unlocked = true, time = 1780909064}\n\"achi_final\" = {unlocked = true, time = 1780924441}\n\"achi_locked\" = {unlocked = false, time = 0}\n");

        Assert.Equal(2527500, EmulatorAchievements.AppIdFromFolder(game.FullName));
        var unlocked = EmulatorAchievements.ReadUnlocked(2527500, game.FullName);
        Assert.Equal(new[] { "achi_final", "achi_mmnice" }, unlocked.Keys.Order());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1780924441).LocalDateTime, unlocked["achi_final"]);
        Assert.True(EmulatorAchievements.HasFiles(2527500, game.FullName));
        Assert.True(EmulatorAchievements.IsAchievementFile(Path.Combine(game.FullName, "SteamData", "user_stats.ini")));
    }

    [Fact]
    public void GoldbergDefinitionListsAreNotUnlocks() =>
        // steam_settings\achievements.json lists the achievements (an array): nothing is unlocked by it
        Assert.Empty(EmulatorAchievements.Parse("""[ { "name": "BABA_LAKE", "displayName": "Water Is Sink", "hidden": 0 } ]"""));

    [Fact]
    public void ReadsRldLists()
    {
        var u = EmulatorAchievements.Parse("[Steam]\nACHCount = 3\nACH001 = LEVEL_1\nACHG001 = 1\nACH002 = LEVEL_10\nACHG002 = 0\nACH003 = BOSS\nACHG003 = 1\n");
        Assert.Equal(new[] { "BOSS", "LEVEL_1" }, u.Keys.Order());
    }

    [Fact]
    public void FindsTheSteamAppIdOfAGameFolder()
    {
        var game = Directory.CreateDirectory(Path.Combine(_dir, "Game"));
        Assert.Null(EmulatorAchievements.AppIdFromFolder(game.FullName));
        Directory.CreateDirectory(Path.Combine(game.FullName, "Binaries", "Win64"));
        File.WriteAllText(Path.Combine(game.FullName, "Binaries", "Win64", "OnlineFix.ini"), "[Main]\nRealAppId=2780980\nFakeAppId=480\n");
        Assert.Equal(2780980, EmulatorAchievements.AppIdFromFolder(game.FullName));

        var other = Directory.CreateDirectory(Path.Combine(_dir, "Other"));
        File.WriteAllText(Path.Combine(other.FullName, "steam_appid.txt"), "480\n"); // Valve's test app: ignored
        Assert.Null(EmulatorAchievements.AppIdFromFolder(other.FullName));
        File.WriteAllText(Path.Combine(other.FullName, "steam_emu.ini"), "; AppId\n;    Your game appid\nAppId = 322170\n");
        Assert.Equal(322170, EmulatorAchievements.AppIdFromFolder(other.FullName));
    }

    [Fact]
    public void ReadsAchievementsSavedNextToTheGame()
    {
        var game = Directory.CreateDirectory(Path.Combine(_dir, "Celeste"));
        var save = Directory.CreateDirectory(Path.Combine(game.FullName, "Data", "steam_settings", "504230"));
        File.WriteAllText(Path.Combine(save.FullName, "achievements.json"), """{ "CHAPTER_1": { "earned": true, "earned_time": 1700000000 } }""");
        Assert.True(EmulatorAchievements.HasFiles(504230, game.FullName));
        Assert.Contains("CHAPTER_1", EmulatorAchievements.ReadUnlocked(504230, game.FullName).Keys);
        Assert.Equal(504230, EmulatorAchievements.AppIdFromPath(Path.Combine(save.FullName, "achievements.json")));
        Assert.True(EmulatorAchievements.IsAchievementFile(@"C:\x\Stats\Achievements.ini"));
        Assert.False(EmulatorAchievements.IsAchievementFile(@"C:\x\Stats\Stats.ini"));
    }

    [Fact]
    public void TheWatcherReportsANewUnlock()
    {
        var root = Directory.CreateDirectory(Path.Combine(_dir, "GSE Saves"));
        var reported = new System.Collections.Concurrent.ConcurrentBag<int?>();
        using var watcher = new AchievementFileWatcher((id, _) => reported.Add(id));
        watcher.Watch(root.FullName);
        var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "264710"));
        File.WriteAllText(Path.Combine(folder.FullName, "achievements.json"), """{ "BuildBase": { "earned": true, "earned_time": 1751130524 } }""");
        File.WriteAllText(Path.Combine(folder.FullName, "playtime.txt"), "1"); // not an achievement file
        var deadline = DateTime.Now.AddSeconds(5);
        while (reported.IsEmpty && DateTime.Now < deadline) Thread.Sleep(100);
        Assert.Contains(264710, reported);
    }
}
