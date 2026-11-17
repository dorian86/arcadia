using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Arcadia.Services;

/// <summary>
/// Pre-renders soft shadows once at startup. Live BlurEffect/DropShadowEffect on dozens of cards
/// is what made scrolling slow; a stretched bitmap gives the same look for almost no GPU cost.
/// </summary>
public static class ShadowFactory
{
    public static ImageSource Create(double width, double height, double radius, double blur, byte alpha = 255)
    {
        var pad = blur * 2;
        var w = (int)Math.Ceiling(width + pad * 2);
        var h = (int)Math.Ceiling(height + pad * 2);
        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Fill = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)),
            Effect = new BlurEffect { Radius = blur, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        };
        var host = new System.Windows.Controls.Grid { Width = w, Height = h };
        host.Children.Add(rect);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>Margin to apply to a stretched shadow image so it lines up with its element.</summary>
    public static Thickness MarginFor(double blur, double offsetY) =>
        new(-blur * 2, -blur * 2 + offsetY, -blur * 2, -blur * 2 - offsetY);

    public static void Register(ResourceDictionary resources)
    {
        resources["CardShadowImage"] = Create(200, 300, 12, 18);
        resources["PanelShadowImage"] = Create(400, 300, 18, 30);
        resources["GrainBrush"] = CreateGrain();
        resources["CardShadowBrush"] = MakeBrush((ImageSource)resources["CardShadowImage"]);
        resources["PanelShadowBrush"] = MakeBrush((ImageSource)resources["PanelShadowImage"]);
    }

    private static Brush MakeBrush(ImageSource source)
    {
        var b = new ImageBrush(source) { Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }

    private static Brush CreateGrain()
    {
        const int size = 128;
        var pixels = new byte[size * size * 4];
        var rng = new Random(1234);
        for (var i = 0; i < size * size; i++)
        {
            var v = (byte)rng.Next(256);
            var a = (byte)rng.Next(0, 14);
            // premultiplied BGRA
            var p = (byte)(v * a / 255);
            pixels[i * 4] = p;
            pixels[i * 4 + 1] = p;
            pixels[i * 4 + 2] = p;
            pixels[i * 4 + 3] = a;
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bmp.Freeze();
        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }
}

/// <summary>Finds a vivid, pleasant dominant colour in a cover image.</summary>
public static class ColorExtractor
{
    public static Color? FromFile(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var bi = new BitmapImage();
            using (var fs = File.OpenRead(path))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = 32;
                bi.StreamSource = fs;
                bi.EndInit();
            }
            return FromBitmap(bi);
        }
        catch { return null; }
    }

    public static Color? FromBitmap(BitmapSource bitmap)
    {
        try
        {
            BitmapSource bi = bitmap;
            if (bi.PixelWidth > 48) bi = new TransformedBitmap(bi, new ScaleTransform(48.0 / bi.PixelWidth, 48.0 / bi.PixelWidth));
            var src = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new byte[w * h * 4];
            src.CopyPixels(px, w * 4, 0);

            // 12 hue buckets weighted by saturation x brightness
            var weight = new double[12];
            var sumR = new double[12]; var sumG = new double[12]; var sumB = new double[12];
            for (var i = 0; i < w * h; i++)
            {
                double b = px[i * 4] / 255.0, g = px[i * 4 + 1] / 255.0, r = px[i * 4 + 2] / 255.0;
                var (hue, sat, val) = ToHsv(r, g, b);
                if (px[i * 4 + 3] < 64 || val < 0.18 || sat < 0.22) continue;
                var wgt = sat * sat * val;
                var bucket = (int)(hue / 30) % 12;
                weight[bucket] += wgt;
                sumR[bucket] += r * wgt; sumG[bucket] += g * wgt; sumB[bucket] += b * wgt;
            }
            var best = Array.IndexOf(weight, weight.Max());
            if (weight[best] <= 0.5) return null;
            var (h2, s2, _) = ToHsv(sumR[best] / weight[best], sumG[best] / weight[best], sumB[best] / weight[best]);
            return FromHsv(h2, Math.Clamp(s2, 0.55, 0.85), 0.92);
        }
        catch { return null; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 7 || hex[0] != '#') return null;
        return uint.TryParse(hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : null;
    }

    private static (double H, double S, double V) ToHsv(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}

public sealed record AccentPreset(string Name, string Label, Color A, Color B);

/// <summary>Switches the accent colours at runtime (all accent brushes are DynamicResources).</summary>
public static class ThemeService
{
    public static readonly AccentPreset[] Presets =
    {
        new("Violet", "Nébuleuse", Color.FromRgb(0x8B, 0x6C, 0xFF), Color.FromRgb(0x3F, 0xA8, 0xFF)),
        new("Ocean", "Océan", Color.FromRgb(0x2F, 0x8B, 0xFF), Color.FromRgb(0x22, 0xD3, 0xEE)),
        new("Emerald", "Émeraude", Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0x14, 0xB8, 0xA6)),
        new("Sunset", "Crépuscule", Color.FromRgb(0xFF, 0x7A, 0x45), Color.FromRgb(0xFF, 0x3D, 0x7F)),
        new("Sakura", "Sakura", Color.FromRgb(0xF4, 0x72, 0xB6), Color.FromRgb(0xA7, 0x8B, 0xFA)),
        new("Gold", "Or", Color.FromRgb(0xF5, 0xB0, 0x0B), Color.FromRgb(0xEF, 0x55, 0x44)),
    };

    public static AccentPreset Current { get; private set; } = Presets[0];

    public static void Apply(ResourceDictionary resources, string? name)
    {
        var p = Presets.FirstOrDefault(x => x.Name == name) ?? Presets[0];
        Current = p;
        var mid = Lerp(p.A, p.B, 0.5);

        resources["Accent"] = Frozen(new SolidColorBrush(p.A));
        resources["Accent2"] = Frozen(new SolidColorBrush(p.B));
        resources["AccentSoft"] = Frozen(new SolidColorBrush(Color.FromArgb(0x29, p.A.R, p.A.G, p.A.B)));
        resources["AccentGradient"] = Frozen(new LinearGradientBrush(new GradientStopCollection
        {
            new(p.A, 0), new(mid, 0.55), new(p.B, 1),
        }, new Point(0, 0), new Point(1, 1)));
        resources["LogoGradient"] = resources["AccentGradient"];
        resources["AccentGlow"] = Frozen(new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0xB0, mid.R, mid.G, mid.B), 0),
            new(Color.FromArgb(0x40, mid.R, mid.G, mid.B), 0.55),
            new(Color.FromArgb(0x00, mid.R, mid.G, mid.B), 1),
        }));
        resources["AccentColor"] = p.A;
    }

    public static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
