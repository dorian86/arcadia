using System.Text.Json;
using Arcadia.Models;
using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arcadia-tests-" + Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void GameRoundTripsWithoutTransientState()
    {
        var game = new Game
        {
            Name = "Subnautica",
            LaunchTarget = @"C:\Games\Subnautica\Subnautica.exe",
            SourceKind = GameSourceKind.Shortcut,
            IsFavorite = true,
            PlayTimeSeconds = 3600,
            SteamAppId = 264710,
            NameHints = new List<string> { "Subnautica" },
            IsMissing = true,
            IsRunning = true,
        };
        var path = Path.Combine(_dir, "library.json");
        JsonFile.Write(path, new LibraryData { Games = { game } });
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("IsMissing", json);
        Assert.DoesNotContain("CoverFullPath", json);

        var loaded = JsonFile.Read<LibraryData>(path)!.Games.Single();
        Assert.Equal(game.Id, loaded.Id);
        Assert.Equal("Subnautica", loaded.Name);
        Assert.True(loaded.IsFavorite);
        Assert.Equal(3600, loaded.PlayTimeSeconds);
        Assert.Equal(264710, loaded.SteamAppId);
        Assert.Equal(GameSourceKind.Shortcut, loaded.SourceKind);
        Assert.False(loaded.IsMissing);
    }

    [Fact]
    public void CorruptFileFallsBackToBackup()
    {
        var path = Path.Combine(_dir, "library.json");
        JsonFile.Write(path, new LibraryData { Games = { new Game { Name = "A" } } });
        JsonFile.Write(path, new LibraryData { Games = { new Game { Name = "B" } } }); // A is now in .bak
        File.WriteAllText(path, "{ broken json");
        var loaded = JsonFile.Read<LibraryData>(path);
        Assert.NotNull(loaded);
        Assert.Equal("A", loaded!.Games.Single().Name);
    }

    [Fact]
    public void ReadsShortcutsAndUrlFiles()
    {
        var target = Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\notepad.exe");
        var lnk = Path.Combine(_dir, "Notepad.exe - Raccourci.lnk");
        Sta.Run(() => ShortcutResolver.CreateLnk(lnk, target, "-test", @"C:\Windows")).Wait();
        var info = Sta.Run(() => ShortcutResolver.ReadLnk(lnk)).Result;
        Assert.NotNull(info);
        Assert.Equal(target, info!.TargetPath, ignoreCase: true);
        Assert.Equal("-test", info.Arguments);

        var url = Path.Combine(_dir, "Portal 2.url");
        File.WriteAllText(url, "[InternetShortcut]\r\nIDList=\r\nIconIndex=0\r\nURL=steam://rungameid/620\r\nIconFile=C:\\nope.ico\r\n");
        var u = ShortcutResolver.ReadUrl(url);
        Assert.Equal("steam://rungameid/620", u!.Url);
        Assert.Equal(620, ShortcutResolver.ExtractSteamAppId(u.Url));
    }
}
