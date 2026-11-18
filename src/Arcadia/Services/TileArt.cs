using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Arcadia.Services;

/// <summary>The artwork files a tile is built from (read on the UI thread, rendered on a worker thread).</summary>
public sealed record TileSources(string? Cover, string? Hero, string? Logo);

/// <summary>
/// Builds artwork shaped for the console themes, so that no cover is cut in half by a square or wide tile:
/// <list type="bullet">
/// <item>with a wide illustration and a logo: the illustration fills the tile and the official logo sits on it
/// (the look of console tiles);</item>
/// <item>otherwise: the whole cover, centred over a blurred, darkened copy of itself (nothing is cropped).</item>
/// </list>
/// Works for every game, including future ones: tiles are rebuilt whenever the cover, illustration or logo
/// change (see <see cref="Signature"/>). Must run on an STA thread.
/// </summary>
public static class TileArt
{
    /// <summary>Bump to rebuild every tile after a change of layout.</summary>
    public const int Version = 1;

    public const int SquareSize = 512, WideWidth = 1280, WideHeight = 720;

    /// <summary>Changes whenever one of the source images changes (name, size or date).</summary>
    public static string Signature(TileSources s) => $"v{Version}|{Stamp(s.Cover)}|{Stamp(s.Hero)}|{Stamp(s.Logo)}";

    private static string Stamp(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "-";
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? $"{fi.Name}:{fi.Length}:{fi.LastWriteTimeUtc.Ticks}" : "-";
        }
        catch { return "-"; }
    }

    /// <summary>Folder of the tile cache, relative to the data folder.</summary>
    public static readonly string Folder = Path.Combine("images", "tiles");

    /// <summary>
    /// Cache file of a tile: its name is derived from the source images, so a tile built once is found again at
    /// every launch (even when library.json lost track of it, e.g. after running an older Arcadia), and an image
    /// cache can never show an outdated tile (new sources = new name).
    /// </summary>
    public static string FileFor(string gameId, string signature, string kind)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(signature)))[..12].ToLowerInvariant();
        return Path.Combine(Folder, $"{gameId}_{hash}_{kind}.jpg");
    }

    public static byte[]? Square(TileSources s) => Render(s, SquareSize, SquareSize, wide: false);

    public static byte[]? Wide(TileSources s) => Render(s, WideWidth, WideHeight, wide: true);

    private static byte[]? Render(TileSources s, int w, int h, bool wide)
    {
        var hero = Load(s.Hero, 1920);
        var logo = Load(s.Logo, 900);
        var cover = Load(s.Cover, 700);

        FrameworkElement root;
        if (hero != null && (logo != null || wide)) root = HeroTile(hero, logo, w, h, wide);
        else if (cover != null) root = CoverTile(cover, w, h);
        else if (hero != null) root = HeroTile(hero, null, w, h, wide);
        else return null;

        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Illustration filling the tile, the logo laid on a soft shadow.</summary>
    private static FrameworkElement HeroTile(BitmapSource hero, BitmapSource? logo, int w, int h, bool wide)
    {
        var root = new Grid { Width = w, Height = h, Background = Brushes.Black, ClipToBounds = true };
        root.Children.Add(new Image { Source = hero, Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (logo == null) return root;

        // darken where the logo sits so that white and coloured logos both read well
        root.Children.Add(new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, 0, 0, 0), 0),
                new(Color.FromArgb(0, 0, 0, 0), wide ? 0.25 : 0.38),
                new(Color.FromArgb(wide ? (byte)120 : (byte)170, 0, 0, 0), 1),
            }, new Point(0, 0), new Point(0, 1)),
        });
        if (wide)
        {
            root.Children.Add(new Border
            {
                Background = new RadialGradientBrush(Color.FromArgb(110, 0, 0, 0), Color.FromArgb(0, 0, 0, 0))
                { RadiusX = 0.45, RadiusY = 0.55 },
            });
        }
        root.Children.Add(new Image
        {
            Source = logo,
            Stretch = Stretch.Uniform,
            MaxWidth = w * (wide ? 0.56 : 0.82),
            MaxHeight = h * (wide ? 0.46 : 0.36),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = wide ? VerticalAlignment.Center : VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, wide ? 0 : h * 0.09),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 3, Opacity = 0.75, Color = Colors.Black },
        });
        return root;
    }

    /// <summary>The whole cover over a blurred, darkened copy of itself.</summary>
    private static FrameworkElement CoverTile(BitmapSource cover, int w, int h)
    {
        var root = new Grid { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(13, 16, 22)), ClipToBounds = true };
        root.Children.Add(new Image
        {
            Source = cover,
            Stretch = Stretch.UniformToFill,
            Margin = new Thickness(-Math.Max(w, h) * 0.12),
            Effect = new BlurEffect { Radius = Math.Max(w, h) / 14.0, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        });
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(105, 8, 10, 14)) });
        // the cover keeps its own proportions: a tall cover fills the height, a wide one the width
        var inset = Math.Min(w, h) * 0.04;
        root.Children.Add(new Image
        {
            Source = cover,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(inset),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = Math.Min(w, h) / 12.0, ShadowDepth = 6, Opacity = 0.65, Color = Colors.Black },
        });
        return root;
    }

    private static BitmapSource? Load(string? path, int decodeWidth)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try { return ImageUtil.Decode(File.ReadAllBytes(path), decodeWidth); }
        catch (Exception ex)
        {
            Log.Warn($"Tile artwork: cannot read {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }
}
