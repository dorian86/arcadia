using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Arcadia.Models;
using Arcadia.Services;

namespace Arcadia.Controls;

/// <summary>
/// Muted, looping background trailer. The video is downloaded (once, cached) after a short delay so that
/// quickly browsing through games does not trigger downloads, then fades in over the artwork.
/// </summary>
public class TrailerPlayer : Grid
{
    private readonly MediaElement _media;
    private CancellationTokenSource? _cts;
    private bool _paused;

    public static readonly DependencyProperty TargetOpacityProperty = DependencyProperty.Register(
        nameof(TargetOpacity), typeof(double), typeof(TrailerPlayer), new PropertyMetadata(1.0));

    public double TargetOpacity
    {
        get => (double)GetValue(TargetOpacityProperty);
        set => SetValue(TargetOpacityProperty, value);
    }

    public event Action<bool>? PlayingChanged;
    public bool IsPlaying { get; private set; }

    public TrailerPlayer()
    {
        IsHitTestVisible = false;
        _media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            IsMuted = true,
            Volume = 0,
            Stretch = Stretch.UniformToFill,
            Opacity = 0,
        };
        Children.Add(_media);
        _media.MediaOpened += (_, _) =>
        {
            Log.Info($"Trailer playing: {_media.Source} ({_media.NaturalVideoWidth}x{_media.NaturalVideoHeight})");
            SetPlaying(true);
            _media.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TargetOpacity, TimeSpan.FromMilliseconds(1400))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });
        };
        _media.MediaEnded += (_, _) =>
        {
            _media.Position = TimeSpan.Zero;
            _media.Play();
        };
        _media.MediaFailed += (_, e) =>
        {
            Log.Warn($"Trailer playback failed: {e.ErrorException?.Message}");
            Stop();
        };
        Unloaded += (_, _) => Stop();
    }

    public async void PlayFor(Game? game, TimeSpan delay)
    {
        Stop();
        if (game?.Trailers is not { Count: > 0 } trailers) return;
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(delay, cts.Token);
            var file = await TrailerService.GetVideoFileAsync(trailers[0], cts.Token);
            if (file == null || cts.IsCancellationRequested) return;
            _paused = false;
            _media.Source = new Uri(file);
            _media.Play();
        }
        catch (OperationCanceledException) { /* moved to another game */ }
    }

    public void TogglePause()
    {
        if (!IsPlaying) return;
        _paused = !_paused;
        if (_paused)
        {
            _media.Pause();
            _media.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        }
        else
        {
            _media.Play();
            _media.BeginAnimation(OpacityProperty, new DoubleAnimation(TargetOpacity, TimeSpan.FromMilliseconds(600)));
        }
        PlayingChanged?.Invoke(!_paused);
    }

    public bool IsPaused => _paused;

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _media.BeginAnimation(OpacityProperty, null);
        _media.Opacity = 0;
        _media.Stop();
        _media.Source = null;
        SetPlaying(false);
    }

    private void SetPlaying(bool playing)
    {
        if (IsPlaying == playing) return;
        IsPlaying = playing;
        PlayingChanged?.Invoke(playing);
    }
}
