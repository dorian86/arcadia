using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

/// <summary>The folder search (GameFolderScanner) on a messy "jeux" folder built for the test.</summary>
public class FolderSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "arcadia-jeux-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp folder */ }
    }

    /// <summary>A fake executable (starts like one, never run).</summary>
    private void Exe(string rel, int size = 120_000)
    {
        var bytes = new byte[size];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        Write(rel, bytes);
    }

    private void Touch(string rel) => Write(rel, new byte[16]);

    private void Folder(string rel) => Directory.CreateDirectory(Path.Combine(_root, rel));

    private void Write(string rel, byte[] bytes)
    {
        var path = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private Dictionary<string, DetectedGame> Search() =>
        GameFolderScanner.Scan(_root).Games.ToDictionary(g => Path.GetFileName(g.Executable!), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void FindsTheGamesOfAMessyFolder()
    {
        // Unity, with its crash handler
        Exe(@"Hollow Knight\hollow_knight.exe");
        Folder(@"Hollow Knight\hollow_knight_Data");
        Touch(@"Hollow Knight\UnityPlayer.dll");
        Exe(@"Hollow Knight\UnityCrashHandler64.exe");
        // Unreal in a category folder: the small exe at the top, the real one and the crash reporter
        Exe(@"Action\Stray\Stray.exe");
        Exe(@"Action\Stray\Hk_project\Binaries\Win64\Stray-Win64-Shipping.exe");
        Exe(@"Action\Stray\Engine\Binaries\Win64\CrashReportClient.exe");
        // two games side by side in another category
        Exe(@"Indés\Undertale\UNDERTALE.exe");
        Touch(@"Indés\Undertale\data.win");
        Exe(@"Indés\Brotato\Brotato.exe");
        Touch(@"Indés\Brotato\Brotato.pck");
        // 64 and 32-bit copies, a redistributable and the uninstaller
        Exe(@"The Witcher 3\bin\x64\witcher3.exe");
        Touch(@"The Witcher 3\bin\x64\steam_api64.dll");
        Exe(@"The Witcher 3\bin\x86\witcher3.exe");
        Touch(@"The Witcher 3\bin\x86\steam_api.dll");
        Exe(@"The Witcher 3\_CommonRedist\vcredist_x64.exe");
        Exe(@"The Witcher 3\unins000.exe");
        // a portable game left at the root
        Exe(@"Celeste.exe");
        Touch(@"FNA.dll");

        var found = Search();

        Assert.Equal(new[] { "Brotato.exe", "Celeste.exe", "hollow_knight.exe", "Stray.exe", "UNDERTALE.exe", "witcher3.exe" },
                     found.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(found.Values, g => Assert.False(g.Uncertain));
        Assert.Equal("Unity", found["hollow_knight.exe"].Note);
        Assert.Equal("Unreal Engine", found["Stray.exe"].Note);
        Assert.Equal("GameMaker", found["UNDERTALE.exe"].Note);
        Assert.Equal("Godot", found["Brotato.exe"].Note);
        Assert.Contains(@"\x64\", found["witcher3.exe"].Executable);
        Assert.Equal("The Witcher 3", found["witcher3.exe"].Name);
        Assert.Equal(Path.Combine(_root, "The Witcher 3"), found["witcher3.exe"].InstallDir);
        Assert.Equal(Path.Combine(_root, "Action", "Stray"), found["Stray.exe"].InstallDir);
        Assert.Null(found["Celeste.exe"].InstallDir); // the root folder is not the game's folder
        Assert.Equal(Path.Combine(_root, @"Hollow Knight"), found["hollow_knight.exe"].WorkingDirectory);
    }

    [Fact]
    public void LeavesSoftwareInstallersAndLaunchersOut()
    {
        Exe(@"setup_cyberpunk_2077.exe");
        Exe(@"Logiciels\NoteApp\NoteApp.exe");
        Touch(@"Logiciels\NoteApp\resources\app.asar");
        Touch(@"Logiciels\NoteApp\d3dcompiler_47.dll");
        Exe(@"Logiciels\Studio\StudioApp.exe");
        Touch(@"Logiciels\Studio\Qt6Core.dll");
        Exe(@"Logiciels\MonOutil\MonOutil.exe");
        Exe(@"Emulateurs\RetroArch\retroarch.exe");
        Exe(@"Launchers\Steam\steam.exe");
        Exe(@"Jeu\Jeu Launcher Config.exe");
        Exe(@"Jeu\DedicatedServer.exe");

        Assert.Empty(Search());
    }

    [Fact]
    public void AnUnrealGameUnpackedAtTheRootIsOneGame()
    {
        Exe(@"Stray.exe");
        Exe(@"Stray\Binaries\Win64\Stray-Win64-Shipping.exe");
        Folder(@"Engine\Binaries\ThirdParty");

        var game = Assert.Single(Search()).Value;
        Assert.Equal(Path.Combine(_root, "Stray.exe"), game.Executable);
        Assert.Equal("Unreal Engine", game.Note);
    }

    [Fact]
    public void AGameWithFewCluesIsProposedUnchecked()
    {
        Exe(@"Divers\Puzzle\Puzzle.exe");
        Touch(@"Divers\Puzzle\SDL2.dll");

        var game = Assert.Single(Search()).Value;
        Assert.True(game.Uncertain);
        Assert.Equal("Puzzle", game.Name);
    }

    [Fact]
    public void ReadsTheHeaderOfARealExecutable()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var info = PeInfo.Read(cmd);
        Assert.NotNull(info);
        Assert.True(info.IsConsole);
        Assert.False(info.IsDll);
        Assert.NotEmpty(info.Imports);

        var dll = PeInfo.Read(Path.Combine(Environment.SystemDirectory, "kernel32.dll"));
        Assert.True(dll?.IsDll);
        Assert.Null(PeInfo.Read(typeof(FolderSearchTests).Assembly.Location + ".missing"));
    }
}
