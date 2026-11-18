using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Arcadia.ViewModels;

/// <summary>A picture of the user's choice behind the default interface (darkened, optionally blurred).</summary>
public partial class MainViewModel
{
    private bool _wallpaperBusy;

    /// <summary>Image shown (the blurred copy when the blur is on), null when there is none.</summary>
    public string? WallpaperPath
    {
        get
        {
            var rel = Settings.WallpaperBlur && Settings.WallpaperBlurred != null ? Settings.WallpaperBlurred : Settings.Wallpaper;
            var full = AppPaths.Resolve(rel);
            return full != null && File.Exists(full) ? full : null;
        }
    }

    public bool HasWallpaper => WallpaperPath != null;

    /// <summary>Opacity of the dark layer laid over the wallpaper.</summary>
    public double WallpaperDimOpacity => Math.Clamp(Settings.WallpaperDim, 0, 90) / 100.0;

    public bool IsWallpaperBusy
    {
        get => _wallpaperBusy;
        private set => SetProperty(ref _wallpaperBusy, value);
    }

    /// <summary>Called by the settings listener of the constructor.</summary>
    private void OnWallpaperSettingChanged(string? property)
    {
        switch (property)
        {
            case nameof(AppSettings.Wallpaper):
            case nameof(AppSettings.WallpaperBlurred):
                OnPropertyChanged(nameof(WallpaperPath));
                OnPropertyChanged(nameof(HasWallpaper));
                break;
            case nameof(AppSettings.WallpaperBlur):
                OnPropertyChanged(nameof(WallpaperPath));
                if (Settings.WallpaperBlur && Settings.WallpaperBlurred == null && Settings.Wallpaper != null) _ = BuildBlurredWallpaperAsync();
                break;
            case nameof(AppSettings.WallpaperDim):
                OnPropertyChanged(nameof(WallpaperDimOpacity));
                break;
        }
    }

    [RelayCommand]
    private async Task ChooseWallpaper()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir un fond d'écran",
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|Tous les fichiers (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        await SetWallpaperAsync(dlg.FileName);
    }

    /// <summary>Copies the image into the data folder (the original can be moved or deleted afterwards).</summary>
    public async Task SetWallpaperAsync(string file)
    {
        IsWallpaperBusy = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(file);
            var (w, h) = ImageUtil.GetSize(bytes);
            if (w < 64 || h < 64) throw new InvalidOperationException("Ce fichier n'est pas une image valide.");
            var rel = Path.Combine("images", $"wallpaper_{DateTime.UtcNow.Ticks:x}{ImageUtil.DetectExtension(bytes)}");
            await File.WriteAllBytesAsync(AppPaths.Resolve(rel)!, bytes);

            var oldImage = Settings.Wallpaper;
            var oldBlur = Settings.WallpaperBlurred;
            Settings.WallpaperBlurred = null;
            Settings.Wallpaper = rel;
            AppPaths.TryDeleteManaged(oldImage);
            AppPaths.TryDeleteManaged(oldBlur);
            if (Settings.WallpaperBlur) await BuildBlurredWallpaperAsync();
            SaveSettings();
            ShowToast("Fond d'écran appliqué", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Warn($"Wallpaper failed: {ex.Message}");
            ShowToast("Impossible d'utiliser cette image : " + ex.Message, ToastKind.Error);
        }
        finally { IsWallpaperBusy = false; }
    }

    [RelayCommand]
    private void RemoveWallpaper()
    {
        var oldImage = Settings.Wallpaper;
        var oldBlur = Settings.WallpaperBlurred;
        Settings.Wallpaper = null;
        Settings.WallpaperBlurred = null;
        AppPaths.TryDeleteManaged(oldImage);
        AppPaths.TryDeleteManaged(oldBlur);
        SaveSettings();
    }

    /// <summary>The blur is computed once into a picture (a live blur effect would slow every frame down).</summary>
    private async Task BuildBlurredWallpaperAsync()
    {
        var source = AppPaths.Resolve(Settings.Wallpaper);
        if (source == null || !File.Exists(source)) return;
        IsWallpaperBusy = true;
        try
        {
            var bytes = await File.ReadAllBytesAsync(source);
            var blurred = await Sta.Run(() => Blur(bytes));
            var rel = Path.Combine("images", $"wallpaper_blur_{DateTime.UtcNow.Ticks:x}.jpg");
            await File.WriteAllBytesAsync(AppPaths.Resolve(rel)!, blurred);
            var old = Settings.WallpaperBlurred;
            Settings.WallpaperBlurred = rel;
            AppPaths.TryDeleteManaged(old);
            SaveSettings();
        }
        catch (Exception ex) { Log.Warn($"Wallpaper blur failed: {ex.Message}"); }
        finally { IsWallpaperBusy = false; }
    }

    /// <summary>Soft blur of an image, rendered at 1920 px wide (STA thread).</summary>
    public static byte[] Blur(byte[] bytes)
    {
        var image = ImageUtil.Decode(bytes, 1920);
        var w = 1920;
        var h = Math.Max(1, (int)Math.Round(1920.0 * image.PixelHeight / image.PixelWidth));
        var root = new Grid { Width = w, Height = h, ClipToBounds = true, Background = Brushes.Black };
        root.Children.Add(new Image
        {
            Source = image,
            Stretch = Stretch.UniformToFill,
            Margin = new Thickness(-60), // the blur fades the edges: draw a little beyond them
            Effect = new BlurEffect { Radius = 48, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
        });
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
}
