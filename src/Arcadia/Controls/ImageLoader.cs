using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Arcadia.Services.Online;

namespace Arcadia.Controls;

/// <summary>
/// Asynchronous, cached image loading for Image elements:
/// files and URLs are decoded off the UI thread at the right size, then faded in.
/// Usage: c:ImageLoader.Source="{Binding CoverFullPath}" c:ImageLoader.DecodeWidth="360"
/// </summary>
public static class ImageLoader
{
    private static readonly LruCache Cache = new(400);
    private static readonly SemaphoreSlim DecodeGate = new(4, 4);

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(ImageLoader), new PropertyMetadata(null, OnSourceChanged));

    public static string? GetSource(DependencyObject d) => (string?)d.GetValue(SourceProperty);
    public static void SetSource(DependencyObject d, string? v) => d.SetValue(SourceProperty, v);

    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.RegisterAttached(
        "DecodeWidth", typeof(int), typeof(ImageLoader), new PropertyMetadata(0));

    public static int GetDecodeWidth(DependencyObject d) => (int)d.GetValue(DecodeWidthProperty);
    public static void SetDecodeWidth(DependencyObject d, int v) => d.SetValue(DecodeWidthProperty, v);

    /// <summary>Read-only flag (for triggers): true once an image is displayed.</summary>
    public static readonly DependencyProperty IsLoadedProperty = DependencyProperty.RegisterAttached(
        "IsLoaded", typeof(bool), typeof(ImageLoader), new PropertyMetadata(false));

    public static bool GetIsLoaded(DependencyObject d) => (bool)d.GetValue(IsLoadedProperty);
    public static void SetIsLoaded(DependencyObject d, bool v) => d.SetValue(IsLoadedProperty, v);

    /// <summary>Stretch of the image brush when the target is a Border (default UniformToFill).</summary>
    public static readonly DependencyProperty StretchProperty = DependencyProperty.RegisterAttached(
        "Stretch", typeof(Stretch), typeof(ImageLoader), new PropertyMetadata(Stretch.UniformToFill));

    public static Stretch GetStretch(DependencyObject d) => (Stretch)d.GetValue(StretchProperty);
    public static void SetStretch(DependencyObject d, Stretch v) => d.SetValue(StretchProperty, v);

    /// <summary>Vertical alignment of the image brush (Border targets), e.g. Top for hero artwork.</summary>
    public static readonly DependencyProperty AlignYProperty = DependencyProperty.RegisterAttached(
        "AlignY", typeof(AlignmentY), typeof(ImageLoader), new PropertyMetadata(AlignmentY.Center));

    public static AlignmentY GetAlignY(DependencyObject d) => (AlignmentY)d.GetValue(AlignYProperty);
    public static void SetAlignY(DependencyObject d, AlignmentY v) => d.SetValue(AlignYProperty, v);

    /// <summary>Fade the image in once loaded (default true). Disable when the element's opacity is animated elsewhere.</summary>
    public static readonly DependencyProperty FadeProperty = DependencyProperty.RegisterAttached(
        "Fade", typeof(bool), typeof(ImageLoader), new PropertyMetadata(true));

    public static bool GetFade(DependencyObject d) => (bool)d.GetValue(FadeProperty);
    public static void SetFade(DependencyObject d, bool v) => d.SetValue(FadeProperty, v);

    private static void Assign(FrameworkElement target, BitmapSource? bitmap)
    {
        switch (target)
        {
            case Image img:
                img.Source = bitmap;
                break;
            case Border border:
                if (bitmap == null)
                {
                    border.Background = null;
                    break;
                }
                var brush = new ImageBrush(bitmap)
                {
                    Stretch = GetStretch(border),
                    AlignmentY = GetAlignY(border),
                };
                brush.Freeze();
                border.Background = brush;
                break;
        }
    }

    private static readonly DependencyProperty TokenProperty = DependencyProperty.RegisterAttached(
        "Token", typeof(object), typeof(ImageLoader), new PropertyMetadata(null));

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement image || d is not (Image or Border)) return;
        var source = e.NewValue as string;
        var token = new object();
        image.SetValue(TokenProperty, token);

        var fades = GetFade(image);
        if (string.IsNullOrWhiteSpace(source))
        {
            if (fades) image.BeginAnimation(UIElement.OpacityProperty, null);
            Assign(image, null);
            SetIsLoaded(image, false);
            return;
        }

        var scale = 1.0;
        try { scale = VisualTreeHelper.GetDpi(image).DpiScaleX; } catch { /* not in tree yet */ }
        if (scale < 1) scale = 1;
        var decodeWidth = GetDecodeWidth(image);
        var pixelWidth = decodeWidth > 0 ? (int)Math.Ceiling(decodeWidth * Math.Min(scale, 2)) : 0;
        var key = $"{source}|{pixelWidth}";

        if (Cache.TryGet(key, out var cached))
        {
            if (fades)
            {
                image.BeginAnimation(UIElement.OpacityProperty, null);
                image.Opacity = 1;
            }
            Assign(image, cached);
            SetIsLoaded(image, true);
            return;
        }

        Assign(image, null);
        SetIsLoaded(image, false);
        _ = LoadAsync(image, source, pixelWidth, key, token);
    }

    private static async Task LoadAsync(FrameworkElement image, string source, int pixelWidth, string key, object token)
    {
        BitmapSource? bitmap = null;
        try
        {
            byte[]? bytes;
            if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                bytes = await Http.Client.GetByteArrayAsync(source);
            }
            else
            {
                bytes = await Task.Run(() =>
                {
                    if (!File.Exists(source)) return null;
                    using var fs = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    return ms.ToArray();
                });
            }
            if (bytes == null || bytes.Length == 0) return;

            await DecodeGate.WaitAsync();
            try
            {
                bitmap = await Task.Run(() => Decode(bytes, pixelWidth));
            }
            finally { DecodeGate.Release(); }
        }
        catch
        {
            bitmap = null;
        }

        if (bitmap == null) return;
        Cache.Add(key, bitmap);
        if (!ReferenceEquals(image.GetValue(TokenProperty), token)) return;

        Assign(image, bitmap);
        SetIsLoaded(image, true);
        if (!GetFade(image)) return;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        image.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static BitmapSource? Decode(byte[] bytes, int pixelWidth)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (pixelWidth > 0) bi.DecodePixelWidth = pixelWidth;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    /// <summary>Forgets every cached bitmap for a file (after it was replaced).</summary>
    public static void Invalidate(string? path)
    {
        if (!string.IsNullOrEmpty(path)) Cache.RemoveWhere(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LruCache
    {
        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Value)>> _map = new();
        private readonly LinkedList<(string Key, BitmapSource Value)> _list = new();

        public LruCache(int capacity) => _capacity = capacity;

        public bool TryGet(string key, out BitmapSource value)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = null!;
            return false;
        }

        public void Add(string key, BitmapSource value)
        {
            if (_map.TryGetValue(key, out var existing)) _list.Remove(existing);
            var node = new LinkedListNode<(string, BitmapSource)>((key, value));
            _list.AddFirst(node);
            _map[key] = node;
            while (_map.Count > _capacity && _list.Last is { } last)
            {
                _list.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }

        public void RemoveWhere(Func<string, bool> predicate)
        {
            foreach (var key in _map.Keys.Where(predicate).ToList())
            {
                _list.Remove(_map[key]);
                _map.Remove(key);
            }
        }
    }
}
