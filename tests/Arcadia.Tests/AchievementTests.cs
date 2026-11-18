using Arcadia.Models;
using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class AchievementTests
{
    // trimmed answer of IPlayerService/GetGameAchievements (language=french)
    private const string Json = """
    {"response":{"achievements":[
      {"internal_name":"DiveForTheVeryFirstTime","localized_name":"Les pieds mouillés ! ","localized_desc":"Plonger pour la toute première fois","icon":"a.jpg","icon_gray":"g.jpg","hidden":true,"player_percent_unlocked":"89.8","internal_key":1281,"archived":false},
      {"internal_name":"Repair","localized_name":"Extinction évitée","localized_desc":"","icon":"b.jpg","icon_gray":"g.jpg","hidden":false,"player_percent_unlocked":"3.5"},
      {"internal_name":"Old","localized_name":"Retiré","icon":"c.jpg","archived":true},
      {"internal_name":"","localized_name":"Sans clé"}
    ]}}
    """;

    [Fact]
    public void ReadsTheOfficialListInFrench()
    {
        var list = AchievementService.Parse(Json, 264710);
        Assert.Equal(2, list.Count); // archived and broken entries are skipped
        var first = list[0];
        Assert.Equal("DiveForTheVeryFirstTime", first.Key);
        Assert.Equal("Les pieds mouillés !", first.Name);
        Assert.True(first.Hidden);
        Assert.Equal(89.8, first.Percent);
        Assert.Equal("https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/264710/a.jpg", first.Icon);
        Assert.Null(list[1].Description);

        // a secret achievement keeps its description for later, and shows the coloured icon once obtained
        Assert.True(first.IsSecret);
        Assert.DoesNotContain("Plonger", first.ShownDescription);
        Assert.EndsWith("g.jpg", first.IconUrl);
        first.UnlockedAt = DateTime.Now;
        Assert.Equal("Plonger pour la toute première fois", first.ShownDescription);
        Assert.EndsWith("a.jpg", first.IconUrl);
    }

    [Fact]
    public void EmptyAnswerMeansNoAchievements()
    {
        Assert.Empty(AchievementService.Parse("""{"response":{}}""", 1));
    }

    [Fact]
    public void RefreshingKeepsWhatWasTicked()
    {
        var when = new DateTime(2026, 11, 18, 10, 0, 0);
        var old = AchievementService.Parse(Json, 264710);
        old[1].UnlockedAt = when;
        var merged = AchievementService.Merge(AchievementService.Parse(Json, 264710), old);
        Assert.Null(merged[0].UnlockedAt);
        Assert.Equal(when, merged[1].UnlockedAt);
    }

    [Fact]
    public void ProgressFollowsTheTickedAchievements()
    {
        var game = new Game { Name = "Subnautica", Store = GameStore.Local, Achievements = AchievementService.Parse(Json, 264710) };
        Assert.True(game.CanHaveAchievements);
        Assert.Equal("0 / 2  ·  0 %", game.AchievementsProgressText);
        game.Achievements![1].UnlockedAt = DateTime.Now;
        game.RefreshAchievementProgress();
        Assert.Equal(1, game.AchievementsUnlocked);
        Assert.Equal("1 / 2  ·  50 %", game.AchievementsProgressText);

        // Steam games: read from Steam (not ticked); other stores and emulators: not looked up
        Assert.False(game.AchievementsSynced);
        Assert.True(new Game { Store = GameStore.Steam }.AchievementsSynced);
        Assert.True(new Game { Store = GameStore.Steam }.CanHaveAchievements);
        Assert.False(new Game { Store = GameStore.Epic }.CanHaveAchievements);
        Assert.False(new Game { Store = GameStore.Emulator }.CanHaveAchievements);
    }

    // ---- Steam's local stats files (binary KeyValues) ----------------------------------

    private sealed class KvWriter
    {
        private readonly MemoryStream _ms = new();
        public KvWriter Open(string key) { _ms.WriteByte(0); Str(key); return this; }
        public KvWriter Close() { _ms.WriteByte(8); return this; }
        public KvWriter S(string key, string value) { _ms.WriteByte(1); Str(key); Str(value); return this; }
        public KvWriter I(string key, int value) { _ms.WriteByte(2); Str(key); _ms.Write(BitConverter.GetBytes(value)); return this; }
        private void Str(string s) { var b = System.Text.Encoding.UTF8.GetBytes(s); _ms.Write(b); _ms.WriteByte(0); }
        public byte[] Bytes() { _ms.WriteByte(8); return _ms.ToArray(); }
    }

    [Fact]
    public void ReadsUnlockedAchievementsFromSteamFiles()
    {
        // schema: stat 1 holds ACH00 (bit 0), ACH01 (bit 1); stat 2 holds ACH40 (bit 31)
        var schema = new KvWriter().Open("1245620").Open("stats")
            .Open("1").Open("bits").Open("0").S("name", "ACH00").Close().Open("1").S("name", "ACH01").Close().Close().Close()
            .Open("2").Open("bits").Open("31").S("name", "ACH40").Close().Close().Close()
            .Close().Close().Bytes();
        // user: bit 1 of stat 1 and bit 31 of stat 2 unlocked
        var user = new KvWriter().Open("cache").I("crc", 1)
            .Open("1").I("data", 0b10).Open("AchievementTimes").I("1", 1720812537).Close().Close()
            .Open("2").I("data", unchecked((int)0x80000000)).Open("AchievementTimes").I("31", 1777722205).Close().Close()
            .Close().Bytes();

        var unlocked = SteamLocalAchievements.Combine(Kv.Read(schema), Kv.Read(user));
        Assert.Equal(new[] { "ACH01", "ACH40" }, unlocked.Keys.OrderBy(k => k));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1720812537).LocalDateTime, unlocked["ACH01"]);

        var list = new List<Achievement> { new() { Key = "ACH00" }, new() { Key = "ACH01" }, new() { Key = "ACH40", UnlockedAt = unlocked["ACH40"] } };
        var (changed, newly) = AchievementService.ApplyUnlocks(list, unlocked);
        Assert.Equal((1, 1), (changed, newly));
        Assert.Null(list[0].UnlockedAt);
        Assert.Equal((0, 0), AchievementService.ApplyUnlocks(list, unlocked)); // nothing new the second time
    }
}
