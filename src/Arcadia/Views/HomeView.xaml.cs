using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Arcadia.Controls;
using Arcadia.Models;
using Arcadia.ViewModels;

namespace Arcadia.Views;

public partial class HomeView : UserControl
{
    private bool _aIsFront = true;
    private Game? _shown;

    public HomeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var vm = MainViewModel.Instance;
            vm.PropertyChanged -= OnVmChanged;
            vm.PropertyChanged += OnVmChanged;
            ShowFeatured(vm.FeaturedGame, animate: false);
        };
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.FeaturedGame)) ShowFeatured(MainViewModel.Instance.FeaturedGame, animate: IsVisible);
    }

    /// <summary>Cross-fades the backdrop and slides the text of the newly featured game.</summary>
    private void ShowFeatured(Game? game, bool animate)
    {
        if (game == null || game == _shown) return;
        _shown = game;
        var front = _aIsFront ? LayerB : LayerA; // the layer that becomes visible
        var back = _aIsFront ? LayerA : LayerB;
        _aIsFront = !_aIsFront;

        // Hero artwork, or the cover decoded tiny and stretched: a soft blur for free (no BlurEffect)
        ImageLoader.SetAlignY(front, AlignmentY.Center);
        RenderOptions.SetBitmapScalingMode(front, BitmapScalingMode.Linear);
        ImageLoader.SetDecodeWidth(front, game.HasHero ? 1600 : 28);
        ImageLoader.SetSource(front, null);
        ImageLoader.SetSource(front, game.HasHero ? game.HeroFullPath : game.CoverFullPath);
        Panel.SetZIndex(front, 1);
        Panel.SetZIndex(back, 0);

        var duration = TimeSpan.FromMilliseconds(animate ? 750 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        front.BeginAnimation(OpacityProperty, new DoubleAnimation(0, game.HasHero ? 1 : 0.7, duration) { EasingFunction = ease });
        back.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(animate ? 250 : 0) });

        if (!animate) return;
        var t = (TranslateTransform)HeroContent.RenderTransform;
        var ease2 = new CubicEase { EasingMode = EasingMode.EaseOut };
        HeroContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease2 });
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-24, 0, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease2 });
    }

    private void Hero_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        MainViewModel.Instance.PauseFeatured = true;
        FadeTo(PrevButton, 1);
        FadeTo(NextButton, 1);
    }

    private void Hero_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        MainViewModel.Instance.PauseFeatured = false;
        FadeTo(PrevButton, 0);
        FadeTo(NextButton, 0);
    }

    private static void FadeTo(UIElement el, double to) =>
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(200)));
}
