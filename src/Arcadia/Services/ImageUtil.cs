using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Arcadia.Services;

public static class ImageUtil
{
    public static (int Width, int Height) GetSize(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch { return (0, 0); }
    }

    public static string DetectExtension(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8) return ".jpg";
        if (b.Length > 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return ".gif";
        if (b.Length > 2 && b[0] == 'B' && b[1] == 'M') return ".bmp";
        return ".jpg";
    }

    public static BitmapSource Decode(byte[] bytes, int decodeWidth = 0)
    {
        using var ms = new MemoryStream(bytes);
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>
    /// Builds an elegant 600x900 portrait cover from a landscape image (blurred backdrop + centered artwork).
    /// Must run on the UI thread.
    /// </summary>
    public static byte[] ComposePortrait(byte[] source)
    {
        const int W = 600, H = 900;
        var bmp = Decode(source);

        var root = new Grid { Width = W, Height = H, Background = new SolidColorBrush(Color.FromRgb(13, 16, 22)) };
        root.Children.Add(new Image
        {
            Source = bmp,
            Stretch = Stretch.UniformToFill,
            Margin = new Thickness(-80),
            Opacity = 0.75,
            Effect = new BlurEffect { Radius = 48, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        });
        root.Children.Add(new Border
        {
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(150, 8, 10, 14), 0),
                    new(Color.FromArgb(40, 8, 10, 14), 0.5),
                    new(Color.FromArgb(190, 8, 10, 14), 1),
                }, new Point(0, 0), new Point(0, 1)),
        });
        root.Children.Add(new Image
        {
            Source = bmp,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(28),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 10, Opacity = 0.6, Color = Colors.Black },
        });

        root.Measure(new Size(W, H));
        root.Arrange(new Rect(0, 0, W, H));
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);
        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
