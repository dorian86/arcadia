using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Arcadia.Controls;
using Arcadia.Models;
using Arcadia.Services;

namespace Arcadia.Views.Shells;

/// <summary>"PS5" theme: small tiles along the top, the selected game full screen with its logo, Play and activity cards.</summary>
public partial class Ps5Shell : ConsoleShell
{
    private const double Small = 112, Big = 152, Gap = 16;
    private const int MaxTiles = 80;
    private readonly List<(Border Tile, Border Ring, TextBlock Name)> _items = new();
    private int _builtCategory = -1;
    private bool _buttons;
    private bool _bgAFront = true;
    private Game? _bgGame, _shownGame;

    public Ps5Shell() => InitializeComponent();

    public override void OnHidden()
    {
        base.OnHidden();
        Trailer.Stop();
        _shownGame = null;
    }

    protected override void OnRebuilt()
    {
        if (Vm == null) return;
        Tabs.Items.Clear();
        for (var i = 0; i < Math.Min(5, Vm.Categories.Count); i++)
        {
            var index = i;
            var label = Vm.Categories[i].Key switch { "home" => "Jeux", "all" => "Bibliothèque", _ => Vm.Categories[i].Label };
            var tab = new TextBlock { Text = label, FontSize = 24, Margin = new Thickness(0, 0, 40, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(tab, true); // the tabs sit in the window's drag zone
            tab.MouseLeftButtonUp += (_, _) => Vm?.SetCategory(index);
            tab.MouseEnter += (_, _) => tab.Opacity = 0.75;
            tab.MouseLeave += (_, _) => tab.Opacity = 1;
            Tabs.Items.Add(tab);
        }
        BuildRow();
    }

    private void BuildRow()
    {
        if (Vm == null) return;
        Row.Children.Clear();
        _items.Clear();
        _builtCategory = Vm.CategoryIndex;
        foreach (var game in Vm.Games.Take(MaxTiles))
        {
            var g = game;
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, Gap, 0), VerticalAlignment = VerticalAlignment.Top };
            var tile = new Border { Width = Small, Height = Small, CornerRadius = new CornerRadius(16), Cursor = Cursors.Hand, DataContext = g };
            var grid = new Grid();
            var bg = new Border { CornerRadius = new CornerRadius(16) };
            bg.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Game.Name)) { Converter = (System.Windows.Data.IValueConverter)FindResource("NameToBrush") });
            var art = new Border { CornerRadius = new CornerRadius(16) };
            ImageLoader.SetDecodeWidth(art, 220);
            // bound (not set once): the square tile appears as soon as it has been built
            art.SetBinding(ImageLoader.SourceProperty, new System.Windows.Data.Binding(nameof(Game.SquareTilePath)));
            var ring = new Border { CornerRadius = new CornerRadius(20), Margin = new Thickness(-5), BorderThickness = new Thickness(3), BorderBrush = Brushes.White, Opacity = 0 };
            grid.Children.Add(ring);
            grid.Children.Add(bg);
            grid.Children.Add(art);
            tile.Child = grid;
            // hovering only highlights: selecting would slide the row away from the pointer (click selects)
            tile.MouseEnter += (_, _) => { if (Vm?.Current != g || _buttons) Animate(ring, OpacityProperty, 0.5, 120); };
            tile.MouseLeave += (_, _) => Animate(ring, OpacityProperty, Vm?.Current == g && !_buttons ? 1 : 0, 120);
            tile.MouseLeftButtonUp += (_, _) => { _buttons = false; if (Vm?.Current == g) Vm.Activate(); else Vm?.Select(g); };
            GameMenu.SetEnable(tile, true);
            var name = new TextBlock
            {
                Text = g.Name, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(26, 0, 30, 0), MaxWidth = 440, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed,
            };
            panel.Children.Add(tile);
            panel.Children.Add(name);
            _items.Add((tile, ring, name));
            Row.Children.Add(panel);
        }
    }

    protected override void OnSelectionChanged(bool animate)
    {
        if (Vm == null) return;
        if (_builtCategory != Vm.CategoryIndex) { BuildRow(); animate = false; }
        var ms = animate ? 220 : 0;
        for (var i = 0; i < Tabs.Items.Count; i++)
            if (Tabs.Items[i] is TextBlock tab)
            {
                var on = i == Vm.CategoryIndex;
                tab.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
                tab.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }

        var index = Math.Min(Vm.Index, _items.Count - 1);
        for (var i = 0; i < _items.Count; i++)
        {
            var (tile, ring, name) = _items[i];
            var selected = i == index;
            var size = selected ? Big : Small;
            tile.BeginAnimation(WidthProperty, new DoubleAnimation(size, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });
            tile.BeginAnimation(HeightProperty, new DoubleAnimation(size, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });
            Animate(ring, OpacityProperty, selected && !_buttons ? 1 : 0, 150);
            name.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            tile.Opacity = i < index ? 0 : 1;
            tile.IsHitTestVisible = i >= index; // games already passed are hidden: they must not catch clicks
        }
        var t = (TranslateTransform)Row.RenderTransform;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-Math.Max(0, index) * (Small + Gap), TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });

        PlayRing.Opacity = _buttons ? 1 : 0;
        var g = Vm.Current;
        if (g != _shownGame)
        {
            _shownGame = g;
            ShowGame(g, animate);
        }
        if (g != null) FavGlyph.Text = g.IsFavorite ? "" : "";
    }

    private void ShowGame(Game? g, bool animate)
    {
        if (g == null) return;
        if (g.HasLogo)
        {
            ImageLoader.SetDecodeWidth(HeroLogo, 900);
            ImageLoader.SetSource(HeroLogo, g.LogoFullPath);
            HeroLogo.Visibility = Visibility.Visible;
            HeroTitle.Visibility = Visibility.Collapsed;
        }
        else
        {
            HeroLogo.Visibility = Visibility.Collapsed;
            HeroTitle.Visibility = Visibility.Visible;
            HeroTitle.Text = g.Name;
        }
        var meta = new List<string> { g.PlatformLabel };
        if (!string.IsNullOrWhiteSpace(g.Genres)) meta.Add(g.Genres!);
        if (g.ReleaseYear is int y) meta.Add(y.ToString());
        HeroMeta.Text = string.Join("   ·   ", meta);
        CardTime.Text = g.PlayTimeSeconds > 0 ? g.PlayTimeText : "Jamais lancé";
        CardLast.Text = g.LastPlayedText;
        CardStatus.Text = g.Status != GameStatus.None ? g.StatusLabel : g.Rating > 0 ? new string('★', g.Rating) : "—";
        CardAch.Visibility = g.HasAchievements ? Visibility.Visible : Visibility.Collapsed;
        CardAchText.Text = g.HasAchievements ? $"{g.AchievementsUnlocked} / {g.Achievements!.Count}" : "";
        CardAchBar.ScaleX = g.AchievementsRatio;
        if (animate)
        {
            var tr = (TranslateTransform)Hero.RenderTransform;
            tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = Ease });
            Hero.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            Cards.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)));
        }
        ShowBackdrop(g, animate);
        QueueTrailer(g);
    }

    private void ShowBackdrop(Game game, bool animate)
    {
        if (game == _bgGame) return;
        _bgGame = game;
        var front = _bgAFront ? BgB : BgA;
        var back = _bgAFront ? BgA : BgB;
        _bgAFront = !_bgAFront;
        ImageLoader.SetDecodeWidth(front, game.HasHero ? 1920 : 40);
        ImageLoader.SetSource(front, null);
        ImageLoader.SetSource(front, game.HasHero ? game.HeroFullPath : game.CoverFullPath);
        Panel.SetZIndex(front, 1);
        Panel.SetZIndex(back, 0);
        var d = TimeSpan.FromMilliseconds(animate ? 600 : 0);
        front.BeginAnimation(OpacityProperty, new DoubleAnimation(0, game.HasHero ? 1 : 0.7, d));
        back.BeginAnimation(OpacityProperty, new DoubleAnimation(0, d) { BeginTime = TimeSpan.FromMilliseconds(animate ? 200 : 0) });
    }

    private async void QueueTrailer(Game game)
    {
        Trailer.Stop();
        if (!Main.Settings.AutoPlayTrailers) return;
        await Main.Metadata.EnsureScreenshotsAsync(game);
        if (Vm?.Current != game || !IsVisible) return;
        Trailer.PlayFor(game, TimeSpan.FromMilliseconds(2600));
    }

    protected override bool HandleWheel(int step)
    {
        _buttons = false;
        HandlePad(step > 0 ? PadButton.Right : PadButton.Left);
        return true;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => ShowAddMenu(AddTop);

    public override void HandlePad(PadButton button)
    {
        if (Vm == null) return;
        switch (button)
        {
            case PadButton.Left: _buttons = false; Vm.Move(-1); break;
            case PadButton.Right: _buttons = false; if (Vm.Index < _items.Count - 1) Vm.Move(1); break;
            case PadButton.Down: if (!_buttons) { _buttons = true; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); } break;
            case PadButton.Up: case PadButton.B: if (_buttons) { _buttons = false; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); } break;
            case PadButton.A: Vm.Activate(); break;
            case PadButton.Y: Vm.ToggleFavoriteCommand.Execute(null); break;
            case PadButton.X: Vm.RandomCommand.Execute(null); break;
            case PadButton.LB: Vm.SwitchCategory(-1, wrap: true, count: Math.Min(5, Vm.Categories.Count)); break;
            case PadButton.RB: Vm.SwitchCategory(1, wrap: true, count: Math.Min(5, Vm.Categories.Count)); break;
            case PadButton.Start: Vm.OpenDetailsCommand.Execute(null); break;
            case PadButton.Back: Main.OpenThemesCommand.Execute(null); break;
        }
    }
}
