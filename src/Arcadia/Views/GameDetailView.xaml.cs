using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Arcadia.Controls;
using Arcadia.Models;
using Arcadia.ViewModels;

namespace Arcadia.Views;

public partial class GameDetailView : UserControl
{
    private Game? _game;

    public GameDetailView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnGameChanged(DataContext as Game);
        Trailer.PlayingChanged += playing =>
        {
            TrailerToggle.Visibility = Trailer.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
            Ui.SetGlyph(TrailerToggle, Trailer.IsPaused ? "" : "");
            TrailerToggle.Content = Trailer.IsPaused ? "Reprendre la bande-annonce" : "Bande-annonce";
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Trailer.Stop(); };
    }

    private void OnGameChanged(Game? game)
    {
        if (_game != null) _game.PropertyChanged -= OnGamePropertyChanged;
        _game = game;
        Trailer.Stop();
        if (game == null) return;
        game.PropertyChanged += OnGamePropertyChanged;
        StartTrailer();
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // trailers are fetched lazily the first time the page opens
        if (e.PropertyName == nameof(Game.Trailers) && !Trailer.IsPlaying) StartTrailer();
    }

    private void StartTrailer()
    {
        if (_game?.HasTrailers == true && MainViewModel.Instance.Settings.AutoPlayTrailers)
            Trailer.PlayFor(_game, TimeSpan.FromMilliseconds(1500));
    }

    private void TrailerToggle_Click(object sender, RoutedEventArgs e) => Trailer.TogglePause();

    public void ScrollToTop() => SmoothScroll.ScrollToTop(Scroller);

    /// <summary>Staggered entrance: cover slides in, then title, actions and stats rise.</summary>
    public void PlayEntrance()
    {
        Slide(CoverBlock, x: -34, y: 0, delay: 0);
        Slide(InfoBlock, x: 0, y: 22, delay: 70);
        Slide(ActionsBlock, x: 0, y: 18, delay: 150);
        Slide(StatsBlock, x: 0, y: 24, delay: 210);
    }

    private static void Slide(UIElement el, double x, double y, int delay)
    {
        if (el.RenderTransform is not TranslateTransform t) return;
        el.BeginAnimation(OpacityProperty, Keyframes(0, 1, delay));
        t.BeginAnimation(TranslateTransform.XProperty, Keyframes(x, 0, delay));
        t.BeginAnimation(TranslateTransform.YProperty, Keyframes(y, 0, delay));
    }

    /// <summary>Holds <paramref name="from"/> during the delay, then eases to <paramref name="to"/>.</summary>
    private static DoubleAnimationUsingKeyFrames Keyframes(double from, double to, int delay)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + 480)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        return a;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e) => GameMenu.Open(MoreButton, PlacementMode.Bottom);
}
