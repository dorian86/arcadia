using Arcadia.Models;
using Arcadia.Services;
using Arcadia.ViewModels;
using Xunit;

namespace Arcadia.Tests;

public class ThemeTests
{
    [Fact]
    public void UnknownThemeFallsBackToDefault()
    {
        Assert.True(ConsoleTheme.Get(null).IsDefault);
        Assert.True(ConsoleTheme.Get("xbox-360").IsDefault);
        Assert.Equal("Wii U", ConsoleTheme.Get("wiiu").Name);
        Assert.True(ConsoleTheme.Get("switch-light").IsLight);
        Assert.False(ConsoleTheme.Get("ps5").IsLight);
        // every theme key is unique and the default interface is offered first
        Assert.Equal(ConsoleTheme.All.Length, ConsoleTheme.All.Select(t => t.Key).Distinct().Count());
        Assert.Equal(ConsoleTheme.Default, ConsoleTheme.All[0].Key);
    }

    [Fact]
    public void BuildsTilesShapedForTheThemes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arcadia-tiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // a portrait cover only: square and wide tiles keep the whole cover (blurred backdrop)
            var cover = Path.Combine(dir, "cover.jpg");
            File.WriteAllBytes(cover, Sta.Run(() => CoverGenerator.Generate(new Game { Name = "Mecha Test" })).Result);
            var sources = new TileSources(cover, null, null);
            var square = Sta.Run(() => TileArt.Square(sources)).Result!;
            var wide = Sta.Run(() => TileArt.Wide(sources)).Result!;
            Assert.Equal((TileArt.SquareSize, TileArt.SquareSize), Sta.Run(() => ImageUtil.GetSize(square)).Result);
            Assert.Equal((TileArt.WideWidth, TileArt.WideHeight), Sta.Run(() => ImageUtil.GetSize(wide)).Result);

            // the signature follows the source files: a new cover rebuilds the tiles
            var before = TileArt.Signature(sources);
            Assert.Equal(before, TileArt.Signature(sources));
            File.WriteAllBytes(cover, square);
            Assert.NotEqual(before, TileArt.Signature(sources));

            // nothing to build from: no tile (the themes fall back to the cover)
            Assert.Null(Sta.Run(() => TileArt.Square(new TileSources(null, null, null))).Result);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ConsoleHomeShowsLastPlayedFirstThenNewest()
    {
        var now = DateTime.Now;
        var old = new Game { Name = "Old", AddedAt = now.AddDays(-30) };
        var fresh = new Game { Name = "Fresh", AddedAt = now.AddDays(-1) };
        var played = new Game { Name = "Played", AddedAt = now.AddDays(-60), LastPlayedAt = now.AddHours(-2) };
        var playedEarlier = new Game { Name = "Earlier", AddedAt = now.AddDays(-60), LastPlayedAt = now.AddDays(-5) };

        var order = ConsoleShellViewModel.HomeOrder(new[] { old, fresh, playedEarlier, played }).Select(g => g.Name);
        Assert.Equal(new[] { "Played", "Earlier", "Fresh", "Old" }, order);
    }
}
