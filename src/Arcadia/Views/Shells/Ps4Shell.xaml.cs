using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Arcadia.Controls;
using Arcadia.Models;
using Arcadia.Services;
using Arcadia.ViewModels;

namespace Arcadia.Views.Shells;

/// <summary>"PS4" theme: function icons on top, a row of squares where the selected one grows, information below.</summary>
public partial class Ps4Shell : ConsoleShell
{
    private const double Small = 170, Big = 300, Gap = 12, RowLeft = 110;
    private const int MaxTiles = 80;
    private readonly List<Border> _tiles = new();
    private bool _fnRow;
    private int _fn;
    private Game? _bgGame;

    public Ps4Shell()
    {
        InitializeComponent();
        foreach (var b in FnRow.Children.OfType<Button>())
        {
            var button = b;
            b.MouseEnter += (_, _) => FnLabel.Text = button.ToolTip as string;
            b.MouseLeave += (_, _) => FnLabel.Text = _fnRow ? (FnRow.Children[_fn] as Button)?.ToolTip as string : "";
        }
    }

    public override void OnShown()
    {
        base.OnShown();
        var t = (TranslateTransform)Lights.RenderTransform;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-60, 60, TimeSpan.FromSeconds(16))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    public override void OnHidden()
    {
        base.OnHidden();
        ((TranslateTransform)Lights.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);
    }

    protected override void OnRebuilt() => BuildRow();

    private int _builtCategory = -1;

    private void BuildRow()
    {
        if (Vm == null) return;
        Row.Children.Clear();
        _tiles.Clear();
        _builtCategory = Vm.CategoryIndex;
        foreach (var game in Vm.Games.Take(MaxTiles))
        {
            var g = game;
            var tile = new Border { Width = Small, Height = Small, Margin = new Thickness(0, 0, Gap, 0), VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Hand, DataContext = g };
            var grid = new Grid();
            var bg = new Border();
            bg.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Game.Name)) { Converter = (System.Windows.Data.IValueConverter)FindResource("NameToBrush") });
            var art = new Border();
            ImageLoader.SetDecodeWidth(art, 340);
            // bound (not set once): the square tile appears as soon as it has been built
            art.SetBinding(ImageLoader.SourceProperty, new System.Windows.Data.Binding(nameof(Game.SquareTilePath)));
            var ring = new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(3), Opacity = 0 };
            grid.Children.Add(bg);
            grid.Children.Add(art);
            grid.Children.Add(ring);
            tile.Child = grid;
            tile.Tag = ring;
            // hovering only highlights: selecting would slide the row away from the pointer (click selects)
            tile.MouseEnter += (_, _) => { if (Vm?.Current != g || _fnRow) Animate(ring, OpacityProperty, 0.5, 120); };
            tile.MouseLeave += (_, _) => Animate(ring, OpacityProperty, Vm?.Current == g && !_fnRow ? 1 : 0, 120);
            tile.MouseLeftButtonUp += (_, _) => { _fnRow = false; if (Vm?.Current == g) Vm.Activate(); else Vm?.Select(g); };
            GameMenu.SetEnable(tile, true);
            _tiles.Add(tile);
            Row.Children.Add(tile);
        }
    }

    protected override void OnSelectionChanged(bool animate)
    {
        if (Vm == null) return;
        if (_builtCategory != Vm.CategoryIndex) { BuildRow(); animate = false; }
        var ms = animate ? 240 : 0;
        var index = Math.Min(Vm.Index, _tiles.Count - 1);
        for (var i = 0; i < _tiles.Count; i++)
        {
            var on = i == index && !_fnRow;
            var size = i == index ? Big : Small;
            _tiles[i].BeginAnimation(WidthProperty, new DoubleAnimation(size, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });
            _tiles[i].BeginAnimation(HeightProperty, new DoubleAnimation(size, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });
            if (_tiles[i].Tag is UIElement ring) Animate(ring, OpacityProperty, on ? 1 : 0, 150);
            var shown = i >= index && i - index <= 9;
            _tiles[i].Opacity = shown ? 1 : 0;
            _tiles[i].IsHitTestVisible = shown; // games already passed are hidden: they must not catch clicks
        }
        var t = (TranslateTransform)Row.RenderTransform;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-Math.Max(0, index) * (Small + Gap), TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });

        var g = Vm.Current;
        SelectedTitle.Text = g?.Name ?? "";
        InfoTime.Text = g == null ? "" : g.PlayTimeSeconds > 0 ? g.PlayTimeText : "—";
        InfoLast.Text = g?.LastPlayedText ?? "";
        InfoPlatform.Text = g?.PlatformLabel ?? "";
        InfoDesc.Text = g?.Description ?? "";
        CategoryText.Text = Vm.Category?.Label ?? "";
        StartButton.Opacity = _fnRow ? 0.5 : 1;
        for (var i = 0; i < FnRow.Children.Count; i++)
            if (FnRow.Children[i] is Button b) b.Tag = _fnRow && i == _fn ? "focus" : "";
        FnLabel.Text = _fnRow ? (FnRow.Children[_fn] as Button)?.ToolTip as string : "";
        if (animate)
        {
            SelectedTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            InfoArea.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(300)));
        }
        ShowBackground(g);
    }

    private void ShowBackground(Game? game)
    {
        if (game == _bgGame) return;
        _bgGame = game;
        GameBg.BeginAnimation(OpacityProperty, null);
        GameBg.Opacity = 0;
        if (game?.HasHero != true) return;
        ImageLoader.SetDecodeWidth(GameBg, 1600);
        ImageLoader.SetSource(GameBg, game.HeroFullPath);
        GameBg.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.55, TimeSpan.FromMilliseconds(600)) { BeginTime = TimeSpan.FromMilliseconds(200) });
    }

    protected override bool HandleWheel(int step)
    {
        _fnRow = false;
        HandlePad(step > 0 ? PadButton.Right : PadButton.Left);
        return true;
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        var fav = Vm?.Categories.FindIndex(c => c.Key == "fav") ?? -1;
        if (fav < 0) { Main.ShowToast("Aucun favori pour l'instant : F ou Y sur un jeu pour l'ajouter", ToastKind.Info); return; }
        _fnRow = false;
        Vm?.SetCategory(fav);
    }

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        _fnRow = false;
        Vm?.SetCategory(Vm.CategoryIndex == 1 ? 0 : 1);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => ShowAddMenu(AddFn);

    public override void HandlePad(PadButton button)
    {
        if (Vm == null) return;
        if (_fnRow)
        {
            switch (button)
            {
                case PadButton.Left: if (_fn > 0) { _fn--; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); } break;
                case PadButton.Right: if (_fn < FnRow.Children.Count - 1) { _fn++; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); } break;
                case PadButton.Down: case PadButton.B: _fnRow = false; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); break;
                case PadButton.A:
                    if (FnRow.Children[_fn] is Button b)
                    {
                        if (b.Command?.CanExecute(null) == true) b.Command.Execute(null);
                        else b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    break;
            }
            return;
        }
        switch (button)
        {
            case PadButton.Left: Vm.Move(-1); break;
            case PadButton.Right: if (Vm.Index < _tiles.Count - 1) Vm.Move(1); break;
            case PadButton.Up: _fnRow = true; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); break;
            case PadButton.A: Vm.Activate(); break;
            case PadButton.Y: Vm.ToggleFavoriteCommand.Execute(null); break;
            case PadButton.X: Vm.RandomCommand.Execute(null); break;
            case PadButton.LB: Vm.SwitchCategory(-1, wrap: true); break;
            case PadButton.RB: Vm.SwitchCategory(1, wrap: true); break;
            case PadButton.Start: Vm.OpenDetailsCommand.Execute(null); break;
            case PadButton.Back: Main.OpenThemesCommand.Execute(null); break;
        }
    }
}
