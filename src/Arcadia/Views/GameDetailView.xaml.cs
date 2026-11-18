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
        ShowAllAchievementsList(false);
        if (game == null) return;
        game.PropertyChanged += OnGamePropertyChanged;
        StartTrailer();
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // trailers are fetched lazily the first time the page opens
        if (e.PropertyName == nameof(Game.Trailers) && !Trailer.IsPlaying) StartTrailer();
        // a new list, or Steam reported new unlocks (they move to the front); a tick by hand changes nothing here
        if (e.PropertyName == nameof(Game.Achievements) || e.PropertyName == nameof(Game.AchievementsUnlocked) && _game?.AchievementsSynced == true)
            UpdateAchievementList();
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

    // ---- Achievements: three rows by default, then 60 more per click ---------------------
    private const double AchievementCardWidth = 392 + 12;
    private const int AchievementsPerClick = 60;
    private int _extraAchievements;
    private int _achievementColumns;

    private int AchievementColumns => Math.Max(1, (int)((AchievementsBox.ActualWidth + 12) / AchievementCardWidth));

    private void ShowAllAchievements_Click(object sender, RoutedEventArgs e)
    {
        var total = _game?.Achievements?.Count ?? 0;
        var shown = AchievementColumns * 3 + _extraAchievements;
        if (shown >= total) ShowAllAchievementsList(false);
        else { _extraAchievements += AchievementsPerClick; UpdateAchievementList(); }
    }

    private void ShowAllAchievementsList(bool all)
    {
        _extraAchievements = all ? int.MaxValue / 2 : 0;
        UpdateAchievementList();
    }

    private void AchievementsBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (AchievementColumns != _achievementColumns) UpdateAchievementList();
    }

    /// <summary>Creates only the cards shown: some games have hundreds of achievements (each with its icon to download).</summary>
    private void UpdateAchievementList()
    {
        _achievementColumns = AchievementColumns;
        var all = _game?.AchievementsOrdered ?? new List<Achievement>();
        var count = Math.Min(all.Count, _achievementColumns * 3 + _extraAchievements);
        AchievementList.ItemsSource = count >= all.Count ? all : all.Take(count).ToList();

        var rest = all.Count - count;
        ShowAllAchievements.Visibility = all.Count > _achievementColumns * 3 ? Visibility.Visible : Visibility.Collapsed;
        ShowAllAchievements.Content = rest > 0
            ? $"Afficher plus ({rest} autre{(rest > 1 ? "s" : "")})"
            : "Réduire la liste";
        Ui.SetGlyph(ShowAllAchievements, rest > 0 ? "" : "");
    }
}
