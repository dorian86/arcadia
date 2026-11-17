using Arcadia.Models;
using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class MediaAndEmulationTests
{
    [Fact]
    public void ParsesSteamLocalConfig()
    {
        const string vdf = """
            "UserLocalConfigStore"
            {
                "Software"
                {
                    "Valve"
                    {
                        "Steam"
                        {
                            "apps"
                            {
                                "620" { "LastPlayed" "1700000000" "Playtime" "125" "cloud" { "last_sync_state" "synchronized" } }
                                "400" { "Playtime" "0" }
                            }
                        }
                    }
                }
            }
            """;
        var apps = Vdf.Parse(vdf).Child("UserLocalConfigStore")!.Child("Software")!.Child("Valve")!.Child("Steam")!.Child("apps")!;
        Assert.Equal("125", apps.Child("620")!.Value("Playtime"));
        Assert.Equal("synchronized", apps.Child("620")!.Child("cloud")!.Value("last_sync_state"));
        Assert.Equal("0", apps.Child("400")!.Value("Playtime"));
    }

    [Fact]
    public void PlansDashTrailerDownload()
    {
        const string mpd = """
            <?xml version='1.0' encoding='utf-8'?>
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT1M47.3S">
              <Period id="0" start="PT0.0S">
                <AdaptationSet id="0" contentType="video">
                  <Representation id="0" mimeType="video/mp4" bandwidth="5800000" width="1920" height="1080">
                    <SegmentTemplate timescale="1000000" duration="3000000" initialization="dash_h264/init-stream$RepresentationID$.m4s" media="dash_h264/chunk-stream$RepresentationID$-$Number%05d$.m4s" startNumber="1"/>
                  </Representation>
                  <Representation id="2" mimeType="video/mp4" bandwidth="1400000" width="854" height="480">
                    <SegmentTemplate timescale="1000000" duration="3000000" initialization="dash_h264/init-stream$RepresentationID$.m4s" media="dash_h264/chunk-stream$RepresentationID$-$Number%05d$.m4s" startNumber="1"/>
                  </Representation>
                </AdaptationSet>
                <AdaptationSet id="1" contentType="audio"/>
              </Period>
            </MPD>
            """;
        var urls = TrailerService.Plan(mpd, "https://video.example.com/store_trailers/1/2/dash_h264.mpd?t=1")!;
        Assert.Equal("https://video.example.com/store_trailers/1/2/dash_h264/init-stream2.m4s", urls[0]);
        Assert.Equal("https://video.example.com/store_trailers/1/2/dash_h264/chunk-stream2-00001.m4s", urls[1]);
        Assert.Equal(1 + 20, urls.Count); // 60 s of 3 s segments
    }

    [Fact]
    public void BuildsEmulatedGames()
    {
        var profile = EmulatorService.CreateProfile(@"F:\Emu\yuzu\yuzu.exe", @"F:\Emu\jeux");
        Assert.Equal("Nintendo Switch", profile.Console);
        var rom = @"F:\Emu\jeux\Super Mario Odyssey (World) (En,Fr) [0100000000010000].xci";
        var game = EmulatorService.CreateGame(profile, rom);
        Assert.Equal("Super Mario Odyssey", game.Name);
        Assert.Equal($"-f -g \"{rom}\"", game.Arguments);
        Assert.Equal(GameStore.Emulator, game.Store);

        // A shortcut created by hand for the same ROM (forward slashes) counts as already imported
        var existing = new Game { LaunchTarget = @"F:\Emu\yuzu\yuzu.exe", Arguments = "-f -g \"F:/Emu/jeux\\Super Mario Odyssey (World) (En,Fr) [0100000000010000].xci\"" };
        Assert.True(EmulatorService.IsRomInLibrary(new[] { existing }, rom));
        Assert.False(EmulatorService.IsRomInLibrary(new[] { existing }, @"F:\Emu\jeux\Zelda.xci"));

        var cemu = EmulatorService.CreateProfile(@"C:\Tools\Cemu.exe");
        Assert.Equal("Nintendo Wii U", cemu.Console);
        Assert.Contains("wua", cemu.Extensions);
    }

    [Fact]
    public void HoverZoomGrowsAwayFromListEdges()
    {
        // plenty of room: zoom from the centre
        Assert.Equal(0.5, Arcadia.Controls.HoverZoom.Axis(300, 100, 100), 3);
        // against the top edge of the list: grow downward
        Assert.Equal(0, Arcadia.Controls.HoverZoom.Axis(300, 2, 100), 3);
        // 6 px of room above: origin moves up so the 5 % growth uses at most 2 px (6 - 4 px for the outline)
        Assert.Equal(40.0 / 300, Arcadia.Controls.HoverZoom.Axis(300, 6, 100), 3);
        // against the bottom edge: grow upward
        Assert.Equal(1, Arcadia.Controls.HoverZoom.Axis(300, 100, 0), 3);
    }

    [Fact]
    public void GeneratesCoverWithoutArtwork()
    {
        var bytes = Sta.Run(() => CoverGenerator.Generate(new Game { Name = "The Joy of Creation: Story Mode", Store = GameStore.Local })).Result;
        Assert.True(bytes.Length > 20_000);
        var (w, h) = Sta.Run(() => ImageUtil.GetSize(bytes)).Result;
        Assert.Equal(600, w);
        Assert.Equal(900, h);
    }
}
