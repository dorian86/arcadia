using Arcadia.Models;
using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

/// <summary>The new Epic Games launcher (2026): installed games journal, titles, play time, ".egstore" folders.</summary>
public class EpicLauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arcadia-epic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    private const string Journal = """
    {"version":1,"writtenAt":"2026-11-19T22:40:30Z","entries":0}
    {"installationId":"A","complete":false,"dir":"C:\\Users\\x\\Epic Games\\Scourgebringer","platform":"Windows","namespace":"ns1","itemId":"item1","artifactId":"app1","pendingVersion":"1.0","launchExecutable":"ScourgeBringer.exe"}
    {"installationId":"A","complete":true,"dir":"C:\\Users\\x\\Epic Games\\Scourgebringer","platform":"Windows","namespace":"ns1","itemId":"item1","artifactId":"app1","version":"1.0","launchExecutable":"ScourgeBringer.exe"}
    {"installationId":"B","complete":true,"dir":"D:\\Epic\\Hades","platform":"Windows","namespace":"ns2","itemId":"item2","artifactId":"app2","version":"2.0","launchExecutable":"x64\\Hades.exe"}
    {"installationId":"B","complete":false,"dir":"D:\\Epic\\Hades","platform":"Windows","namespace":"ns2","itemId":"item2","artifactId":"app2","version":"2.0","pendingVersion":"2.1","launchExecutable":"x64\\Hades.exe"}
    {"installationId":"C","complete":false,"dir":"D:\\Epic\\Downloading","platform":"Windows","namespace":"ns3","itemId":"item3","artifactId":"app3","pendingVersion":"1.0"}
    {"installationId":"A","deleted":true,"writtenAt":"2026-11-19T22:41:38Z"}
    {"installationId":"D","complete":tr
    """;

    [Fact]
    public void ReplaysTheInstallJournal()
    {
        var titles = new Dictionary<string, string> { ["item2"] = "Hades" };
        var installs = EpicLauncher.ParseInstallations(Journal, titles);
        // A was uninstalled, C is still downloading, the last line is being written: only Hades (being updated) is installed
        var hades = Assert.Single(installs);
        Assert.Equal(@"D:\Epic\Hades", hades.Dir);
        Assert.Equal("app2", hades.AppName);
        Assert.Equal(@"x64\Hades.exe", hades.LaunchExecutable);
        Assert.Equal("Hades", hades.Title);
        Assert.Equal("com.epicgames.launcher://apps/ns2%3Aitem2%3Aapp2?action=launch&silent=true", EpicLauncher.LaunchUri(hades.Namespace, hades.ItemId, hades.AppName));
    }

    [Fact]
    public void ReadsTitlesAndPlayTime()
    {
        var titles = EpicLauncher.ParseTitles("""{"items":{"item1":{"item":{"id":"item1","title":"Scourgebringer"}},"x":{"item":{}}},"schemaVersion":1}""");
        Assert.Equal("Scourgebringer", titles["item1"]);
        Assert.Single(titles);

        var time = EpicLauncher.ParsePlaytime("""{"items":[{"artifactId":"Fortnite","totalTime":330345},{"artifactId":"c5109bdc","totalTime":18382},{"artifactId":"zero","totalTime":0}]}""");
        Assert.Equal(330345, time["Fortnite"]);
        Assert.Equal(18382, time["c5109bdc"]);
        Assert.False(time.ContainsKey("zero"));
    }

    private string EpicGame(string rel, string exe, string appName, bool jsonManifest = true)
    {
        var dir = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.Combine(dir, ".egstore"));
        File.WriteAllText(Path.Combine(dir, ".egstore", "ABC.mancpn"),
            $$"""{ "FormatVersion": 0, "CatalogNamespace": "ns", "CatalogItemId": "item", "AppName": "{{appName}}" }""");
        File.WriteAllText(Path.Combine(dir, ".egstore", "ABC.manifest"),
            jsonManifest ? $$"""{"ManifestFileVersion":"013000000000","AppNameString":"{{appName}}","LaunchExeString":"{{exe.Replace("\\", "/")}}","FileManifestList":[]}"""
                         : "\u000c\u00c0binary");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, exe))!);
        File.WriteAllText(Path.Combine(dir, exe), "MZ");
        return dir;
    }

    [Fact]
    public void RecognisesAGameInstalledByEpicFromItsFolder()
    {
        var dir = EpicGame(@"JEUX\DokiDoki", "Doki Doki Literature Club Plus.exe", "c5109bdc");
        var install = EpicLauncher.FromGameFolder(Path.Combine(dir, "Doki Doki Literature Club Plus.exe"));
        Assert.Equal("c5109bdc", install?.AppName);
        Assert.Equal("Doki Doki Literature Club Plus.exe", install?.LaunchExecutable);

        var game = new Game { Name = "DDLC", LaunchTarget = Path.Combine(dir, "Doki Doki Literature Club Plus.exe"), Store = GameStore.Local };
        Assert.Equal(GameStore.Epic, StoreScanner.Detect(game));
        Assert.True(StoreScanner.CorrectStore(game));
        Assert.Equal(GameStore.Epic, game.Store);
    }

    [Fact]
    public void AStrayEgstoreHigherUpDoesNotMakeEveryGameAnEpicGame()
    {
        // "G:\jeux\.egstore" left by an old install, binary manifest: the games inside G:\jeux are not Epic games
        var root = EpicGame("jeux", "Old.exe", "0449f415", jsonManifest: false);
        var other = Path.Combine(root, "ULTRAKILL", "ULTRAKILL.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "MZ");
        Assert.Null(EpicLauncher.FromGameFolder(other));
        Assert.Equal(GameStore.Local, StoreScanner.Detect(new Game { Name = "x", LaunchTarget = other }));

        // an Unreal game whose .egstore names its exe is found from deep inside
        var unreal = EpicGame("Stray", @"Hk_project\Binaries\Win64\Stray-Win64-Shipping.exe", "stray");
        Assert.Equal("stray", EpicLauncher.FromGameFolder(Path.Combine(unreal, @"Hk_project\Binaries\Win64\Stray-Win64-Shipping.exe"))?.AppName);
    }

    [Fact]
    public void UnrealEngineIsNotAGame() =>
        Assert.True(StoreScanner.IsEpicTool(new EpicInstall(@"G:\tout\UE_5.0", "ue", "x", "QuixelBridge_5.0", null, null)));
}
