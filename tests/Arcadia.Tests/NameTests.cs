using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class NameCleanerTests
{
    [Theory]
    [InlineData("Subnautica.exe - Raccourci.lnk", "Subnautica")]
    [InlineData("Cyberpunk2077.exe", "Cyberpunk 2077")]
    [InlineData("TheWitcher3.exe", "The Witcher 3")]
    [InlineData("GTA5.exe - Raccourci.lnk", "Grand Theft Auto V")]
    [InlineData("Baba Is You.exe - Raccourci.lnk", "Baba Is You")]
    [InlineData("Hard Bullet.exe - Raccourci (2).lnk", "Hard Bullet")]
    [InlineData("GettingOverIt.exe - Raccourci.lnk", "Getting Over It")]
    [InlineData("JumpKing.exe - Raccourci.lnk", "Jump King")]
    [InlineData("ROBOBEAT V1.3.10.lnk", "ROBOBEAT")]
    [InlineData("Call of Duty®.lnk", "Call of Duty")]
    [InlineData("Street Fighter™ 6.url", "Street Fighter 6")]
    [InlineData("Future Soldier DX11.exe - Raccourci.lnk", "Future Soldier")]
    [InlineData("THUMPER_win10.exe - Raccourci.lnk", "THUMPER")]
    [InlineData("TJoC_SM-Win64-Shipping.exe - Raccourci.lnk", "The Joy of Creation: Story Mode")]
    [InlineData("ktane.exe - Raccourci.lnk", "Keep Talking and Nobody Explodes")]
    [InlineData("NSUNS4_Launcher.exe - Raccourci.lnk", "NARUTO SHIPPUDEN: Ultimate Ninja STORM 4")]
    [InlineData("Minecraft Launcher.lnk", "Minecraft")]
    [InlineData("teardown.exe", "Teardown")]
    [InlineData("my_cool_game.exe", "My Cool Game")]
    [InlineData("Legend of Zelda - Ocarina of Time.lnk", "Legend of Zelda - Ocarina of Time")]
    public void CleansFileNames(string input, string expected) =>
        Assert.Equal(expected, NameCleaner.CleanFileName(input));

    [Theory]
    [InlineData("Baba.Is.You.v481d", "Baba Is You")]
    [InlineData("[game3rb] Geometry.Dash.v2.203a", "Geometry Dash")]
    [InlineData("Getting.Over.It.with.Bennett.Foddy.v1.7", "Getting Over It with Bennett Foddy")]
    [InlineData("Hard.Bullet.Build.17027836", "Hard Bullet")]
    [InlineData("Jump.King.Build.20210122", "Jump King")]
    [InlineData("MiSide v0.93L-P2P", "MiSide")]
    [InlineData("Saiko.no.sutoka.v2.2.8", "Saiko no sutoka")]
    [InlineData("ULTRAKILL Build 04282023", "ULTRAKILL")]
    [InlineData("Thumper.26.05.2020", "Thumper")]
    [InlineData("M.O.L.E", "MOLE")]
    [InlineData("Time.To.Wake.Up", "Time To Wake Up")]
    [InlineData("Super Smash Bros. Ultimate (World) (En,Ja,Fr)", "Super Smash Bros Ultimate")]
    public void CleansFolderNames(string input, string expected) =>
        Assert.Equal(expected, NameCleaner.CleanCore(input));

    [Fact]
    public void PrefersFolderForCrypticExeNames()
    {
        var (display, _) = NameCleaner.Suggest(new NameContext
        {
            ShortcutFile = @"C:\Users\x\Desktop\JEUX\ttwu.exe - Raccourci.lnk",
            ExePath = @"F:\JEUX\Time.To.Wake.Up\game\ttwu.exe",
        });
        Assert.Equal("Time To Wake Up", display);
    }

    [Fact]
    public void RenamedShortcutWins()
    {
        var (display, hints) = NameCleaner.Suggest(new NameContext
        {
            ShortcutFile = @"C:\Desktop\JEUX\Super Meat Boy 3D.lnk",
            ExePath = @"F:\JEUX\Super Meat Boy 3D\SMB.exe",
        });
        Assert.Equal("Super Meat Boy 3D", display);
        Assert.True(hints.Count >= 1);
    }

    [Fact]
    public void RenamedShortcutNeverFallsBackToTargetFolder()
    {
        var (display, hints) = NameCleaner.Suggest(new NameContext
        {
            ShortcutFile = @"C:\Desktop\Fake Racer.lnk",
            ExePath = @"C:\Windows\System32\cmd.exe",
        });
        Assert.Equal("Fake Racer", display);
        Assert.Equal(new[] { "Fake Racer" }, hints);
    }

    [Fact]
    public void EmulatorShortcutUsesRomAndShortcutName()
    {
        var args = "-f -g \"F:/Emu/jeux\\Super Smash Bros. Ultimate (World) (En,Ja,Fr,De).xci\"";
        var rom = NameCleaner.FindRomInArguments(args);
        Assert.NotNull(rom);
        var (display, hints) = NameCleaner.Suggest(new NameContext
        {
            ShortcutFile = @"C:\Desktop\JEUX\Super Smash Bros Ultimate.lnk",
            ExePath = @"F:\Emu\yuzu-windows-msvc\yuzu.exe",
            RomPath = rom,
        });
        Assert.Equal("Super Smash Bros Ultimate", display);
        Assert.DoesNotContain(hints, h => h.Contains("msvc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LongerFolderNameWinsWhenItContainsExeName()
    {
        var (display, _) = NameCleaner.Suggest(new NameContext
        {
            ExePath = @"G:\jeux\Tom Clancys Ghost Recon Future Soldier\Future Soldier DX11.exe",
        });
        Assert.Equal("Tom Clancys Ghost Recon Future Soldier", display);
    }

    [Fact]
    public void OfficialNamesLoseTrademarks() =>
        Assert.Equal("Call of Duty: Black Ops III", NameCleaner.CleanOfficialName("Call of Duty®: Black Ops III"));
}

public class NameMatcherTests
{
    [Theory]
    [InlineData("GTA V", "Grand Theft Auto V", false)]
    [InlineData("Portal", "Portal", true)]
    [InlineData("Geometry Dash", "GeometryDash", true)]
    [InlineData("The Legend of Zelda: Ocarina of Time", "Legend of Zelda - Ocarina of Time", true)]
    [InlineData("Pokémon Pokopia", "Pokemon Pokopia", true)]
    [InlineData("Dark Souls III", "Dark Souls 3", true)]
    public void ExactishMatches(string a, string b, bool expectHigh)
    {
        var s = NameMatcher.Similarity(a, b);
        if (expectHigh) Assert.True(s >= 0.95, $"{a} ~ {b} = {s}");
    }

    [Fact]
    public void SequelIsNotTheSameGame() =>
        Assert.True(NameMatcher.Similarity("Portal", "Portal 2") < 0.75);

    [Fact]
    public void SubtitleStillMatches() =>
        Assert.True(NameMatcher.Similarity("Getting Over It", "Getting Over It with Bennett Foddy") >= 0.75);

    [Fact]
    public void SoundtrackIsPenalised() =>
        Assert.True(NameMatcher.ResultPenalty("Celeste", "Celeste Original Soundtrack") < 1);

    [Fact]
    public void UnrelatedNamesScoreLow() =>
        Assert.True(NameMatcher.Similarity("Teardown", "Mindustry") < 0.5);
}
