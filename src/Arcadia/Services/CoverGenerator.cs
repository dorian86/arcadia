using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Arcadia.Models;

namespace Arcadia.Services;

/// <summary>
/// Builds a polished 600x900 cover for games without artwork: colour wash from the game's icon,
/// halo, centred icon / logo / monogram, title typography and platform label.
/// Rendered offscreen with WPF (UI thread).
/// </summary>
public static class CoverGenerator
{
    private const int W = 600, H = 900;

    public static byte[] Generate(Game game, byte[]? artBytes = null)
    {
        var icon = LoadIcon(game.IconFullPath, out var iconColor);
        BitmapSource? art = null;
        if (artBytes != null)
        {
            try { art = ImageUtil.Decode(artBytes, 800); } catch { art = null; }
        }

        var baseColor = (art != null ? ColorExtractor.FromBitmap(art) : null) ?? iconColor ?? HashColor(game.Name);
        var dark = Shade(baseColor, 0.28);
        var mid = Shade(baseColor, 0.55);

        var root = new Grid { Width = W, Height = H, Background = new SolidColorBrush(Color.FromRgb(12, 14, 20)) };

        // Colour wash
        root.Children.Add(new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(mid, 0), new(dark, 0.55), new(Color.FromRgb(10, 12, 17), 1),
            }, new Point(0.2, 0), new Point(0.8, 1)),
        });
        root.Children.Add(Blob(Lighten(baseColor, 0.25), 0.55, 760, -260, -300));
        root.Children.Add(Blob(baseColor, 0.35, 620, 280, 520));

        // Diagonal light streaks
        var streaks = new Canvas { Opacity = 0.07 };
        for (var i = 0; i < 6; i++)
        {
            var line = new Border { Width = 14 + i * 6, Height = 1400, Background = Brushes.White, RenderTransform = new RotateTransform(28) };
            Canvas.SetLeft(line, 60 + i * 110);
            Canvas.SetTop(line, -260);
            streaks.Children.Add(line);
        }
        root.Children.Add(streaks);

        // Halo behind the artwork
        root.Children.Add(new Ellipse
        {
            Width = 520, Height = 520, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 70, 0, 0),
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(70, 255, 255, 255), 0), new(Color.FromArgb(18, 255, 255, 255), 0.5), new(Color.FromArgb(0, 255, 255, 255), 1),
            }),
        });

        // Centre artwork: logo > icon > monogram
        FrameworkElement centre;
        if (art != null)
        {
            centre = new Image
            {
                Source = art, Stretch = Stretch.Uniform, Width = 470, Height = 300,
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.55 },
            };
        }
        else if (icon != null)
        {
            centre = new Image
            {
                Source = icon, Width = 230, Height = 230, Stretch = Stretch.Uniform,
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.6 },
            };
            RenderOptions.SetBitmapScalingMode(centre, BitmapScalingMode.HighQuality);
        }
        else
        {
            var mono = new Grid { Width = 250, Height = 250 };
            mono.Children.Add(new Ellipse
            {
                Fill = new LinearGradientBrush(Lighten(baseColor, 0.15), Shade(baseColor, 0.5), 45),
                Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), StrokeThickness = 3,
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.55 },
            });
            mono.Children.Add(new TextBlock
            {
                Text = Initials(game.Name), FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontWeight = FontWeights.Bold, FontSize = 104, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -8, 0, 0),
            });
            centre = mono;
        }
        centre.HorizontalAlignment = HorizontalAlignment.Center;
        centre.VerticalAlignment = VerticalAlignment.Top;
        centre.Margin = new Thickness(0, art != null ? 180 : 215, 0, 0);
        root.Children.Add(centre);

        // Bottom shade for the typography
        root.Children.Add(new Border
        {
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0, 8, 10, 14), 0.45), new(Color.FromArgb(200, 8, 10, 14), 0.85), new(Color.FromArgb(235, 8, 10, 14), 1),
            }, new Point(0, 0), new Point(0, 1)),
        });

        // Title + platform
        var title = game.Name.Trim();
        var size = title.Length switch { <= 12 => 64.0, <= 22 => 54.0, <= 34 => 46.0, _ => 40.0 };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(46, 0, 46, 70) };
        text.Children.Add(new Border
        {
            Width = 54, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 0, 22),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(Lighten(baseColor, 0.35), Colors.White, 0),
        });
        text.Children.Add(new TextBlock
        {
            Text = title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxHeight = size * 3.3,
            TextTrimming = TextTrimming.WordEllipsis, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontWeight = FontWeights.Bold, FontSize = size, LineHeight = size * 1.08, Foreground = Brushes.White,
        });
        text.Children.Add(new TextBlock
        {
            Text = Spaced(Subtitle(game)), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 18, 0, 0),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontWeight = FontWeights.SemiBold, FontSize = 17,
            Foreground = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
        });
        root.Children.Add(text);

        // Grain
        if (Application.Current?.TryFindResource("GrainBrush") is Brush grain)
            root.Children.Add(new Border { Background = grain, Opacity = 1.4 });

        root.Measure(new Size(W, H));
        root.Arrange(new Rect(0, 0, W, H));
        root.UpdateLayout();
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var encoder = new JpegBitmapEncoder { QualityLevel = 93 };
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static string Subtitle(Game g) =>
        (g.Console ?? g.Store switch
        {
            GameStore.Local or GameStore.Other or GameStore.Unknown => "Jeu PC",
            _ => StoreInfo.Label(g.Store),
        }).ToUpper(CultureInfo.CurrentCulture);

    /// <summary>Letter-spacing substitute (WPF TextBlock has no tracking).</summary>
    private static string Spaced(string s) => string.Join(" ", s.ToCharArray());

    private static UIElement Blob(Color c, double opacity, double size, double x, double y) => new Ellipse
    {
        Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(x, y, 0, 0), Opacity = opacity,
        Fill = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(200, c.R, c.G, c.B), 0), new(Color.FromArgb(0, c.R, c.G, c.B), 1),
        }),
    };

    /// <summary>Loads the extracted icon, rejecting tiny icons lost in a large transparent canvas.</summary>
    private static BitmapSource? LoadIcon(string? path, out Color? dominant)
    {
        dominant = null;
        try
        {
            if (path == null || !File.Exists(path)) return null;
            var bmp = ImageUtil.Decode(File.ReadAllBytes(path));
            var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = conv.PixelWidth, h = conv.PixelHeight;
            var px = new byte[w * h * 4];
            conv.CopyPixels(px, w * 4, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (px[(y * w + x) * 4 + 3] > 24)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            if (maxX < 0) return null;
            dominant = ColorExtractor.FromBitmap(bmp);
            var content = Math.Max(maxX - minX, maxY - minY) + 1;
            if (content < w * 0.4 || content < 72) return null; // low resolution icon: a monogram looks better
            var cropped = new CroppedBitmap(bmp, new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
            cropped.Freeze();
            return cropped;
        }
        catch { return null; }
    }

    private static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToList();
        if (words.Count == 0) return "?";
        if (words.Count == 1) return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
        return string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    private static Color HashColor(string name)
    {
        uint hash = 2166136261;
        foreach (var c in name) hash = (hash ^ c) * 16777619;
        return ColorExtractor.FromHsv(hash % 360, 0.6, 0.8);
    }

    private static Color Shade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static Color Lighten(Color c, double f) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
}
