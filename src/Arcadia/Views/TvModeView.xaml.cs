using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Arcadia.Controls;
using Arcadia.Models;
using Arcadia.Services;
using Arcadia.ViewModels;

namespace Arcadia.Views;

/// <summary>Full-screen, controller-friendly interface (Big Picture style).</summary>
public partial class TvModeView : UserControl
{
    private const double ItemWidth = 216, ItemGap = 30, Left = 72;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };
    private TvModeViewModel? _vm;
    private int _shownIndex = -1;
    private bool _bgAFront = true;
    private Game? _bgGame;

    public TvModeView()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm", Format.Fr);
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as TvModeViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        };
    }

    public void Show()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm", Format.Fr);
        _clock.Start();
        _shownIndex = -1;
        _bgGame = null;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
        var scale = new ScaleTransform(1.04, 1.04);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => UpdateSelection(animate: false));
    }

    public void Hide(Action onHidden)
    {
        _clock.Stop();
        Trailer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => onHidden();
        BeginAnimation(OpacityProperty, fade);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TvModeViewModel.Index) or nameof(TvModeViewModel.FilterIndex))
        {
            if (e.PropertyName == nameof(TvModeViewModel.FilterIndex)) _shownIndex = -1;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => UpdateSelection(animate: true));
        }
    }

    private void UpdateSelection(bool animate)
    {
        if (_vm == null) return;
        var index = _vm.Index;
        var duration = TimeSpan.FromMilliseconds(animate ? 300 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // slide the strip so the selected cover sits under the fixed selection frame
        var t = (TranslateTransform)Strip.RenderTransform;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Left - index * (ItemWidth + ItemGap), duration) { EasingFunction = ease });

        for (var i = 0; i < Strip.Items.Count; i++)
        {
            if (Strip.ItemContainerGenerator.ContainerFromIndex(i) is not ContentPresenter cp) continue;
            var selected = i == index;
            if (cp.RenderTransform is not ScaleTransform s || s.IsFrozen) cp.RenderTransform = s = new ScaleTransform();
            var target = selected ? 1.16 : 1.0;
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration) { EasingFunction = ease });
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration) { EasingFunction = ease });
            cp.BeginAnimation(OpacityProperty, new DoubleAnimation(selected ? 1 : Math.Abs(i - index) > 5 ? 0.25 : 0.55, duration));
        }

        if (index != _shownIndex)
        {
            _shownIndex = index;
            if (animate)
            {
                var it = (TranslateTransform)Info.RenderTransform;
                Info.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease });
                it.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
            }
            ShowBackdrop(_vm.Current, animate);
            QueueTrailer(_vm.Current);
        }
    }

    /// <summary>After a short pause on a game, its trailer fades in behind the interface.</summary>
    private async void QueueTrailer(Game? game)
    {
        Trailer.Stop();
        if (game == null || !MainViewModel.Instance.Settings.AutoPlayTrailers) return;
        await MainViewModel.Instance.Metadata.EnsureScreenshotsAsync(game);
        if (_vm?.Current != game || !IsVisible) return;
        Trailer.PlayFor(game, TimeSpan.FromMilliseconds(2200));
    }

    /// <summary>Cross-fades the full-screen background to the selected game's artwork.</summary>
    private void ShowBackdrop(Game? game, bool animate)
    {
        if (game == null || game == _bgGame) return;
        _bgGame = game;
        var front = _bgAFront ? BgB : BgA;
        var back = _bgAFront ? BgA : BgB;
        _bgAFront = !_bgAFront;
        ImageLoader.SetDecodeWidth(front, game.HasHero ? 1920 : 28);
        ImageLoader.SetSource(front, null);
        ImageLoader.SetSource(front, game.HasHero ? game.HeroFullPath : game.CoverFullPath);
        Panel.SetZIndex(front, 1);
        Panel.SetZIndex(back, 0);
        var d = TimeSpan.FromMilliseconds(animate ? 600 : 0);
        front.BeginAnimation(OpacityProperty, new DoubleAnimation(0, game.HasHero ? 1 : 0.6, d));
        back.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d) { BeginTime = TimeSpan.FromMilliseconds(animate ? 200 : 0) });
    }

    // ------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------

    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Left: HandlePad(PadButton.Left); return true;
            case Key.Right: HandlePad(PadButton.Right); return true;
            case Key.Enter: case Key.Space: HandlePad(PadButton.A); return true;
            case Key.Escape: case Key.Back: HandlePad(PadButton.B); return true;
            case Key.F: HandlePad(PadButton.Y); return true;
            case Key.R: HandlePad(PadButton.X); return true;
            case Key.PageUp: HandlePad(PadButton.LB); return true;
            case Key.PageDown: case Key.Tab: HandlePad(PadButton.RB); return true;
            case Key.Home: _vm?.Move(-10_000); return true;
            case Key.End: _vm?.Move(10_000); return true;
            default: return false;
        }
    }

    public void HandlePad(PadButton button)
    {
        if (_vm == null) return;
        switch (button)
        {
            case PadButton.Left: _vm.Move(-1); break;
            case PadButton.Right: _vm.Move(1); break;
            case PadButton.Up: _vm.Move(-5); break;
            case PadButton.Down: _vm.Move(5); break;
            case PadButton.A: _vm.PlayCurrent(); break;
            case PadButton.Y: _vm.ToggleFavorite(); break;
            case PadButton.X: _vm.Random(); break;
            case PadButton.LB: _vm.SwitchFilter(-1); break;
            case PadButton.RB: _vm.SwitchFilter(1); break;
            case PadButton.B: case PadButton.Back: MainViewModel.Instance.ExitTvModeCommand.Execute(null); break;
        }
    }
}
