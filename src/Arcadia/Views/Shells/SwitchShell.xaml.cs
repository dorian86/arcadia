using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Arcadia.Models;
using Arcadia.Services;
using Arcadia.ViewModels;

namespace Arcadia.Views.Shells;

/// <summary>"Switch" theme: a row of large squares, round shortcut buttons and an "all software" grid.</summary>
public partial class SwitchShell : ConsoleShell
{
    private const int HomeCount = 12, GridColumns = 6;
    private const double TileStep = 314, StripLeft = 96, StripRight = 1500;
    private readonly List<Button> _strip = new();
    private readonly List<Button> _grid = new();
    private Button? _allTile;
    private bool _gridMode, _onAllTile, _discRow;
    private int _disc;

    public SwitchShell(bool light)
    {
        InitializeComponent();
        if (light)
        {
            Resources["SwBg"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
            Resources["SwText"] = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            Resources["SwMuted"] = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x6E));
            Resources["SwLine"] = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
            Resources["SwTile"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            Resources["SwSelect"] = new SolidColorBrush(Color.FromRgb(0x14, 0xB4, 0xE0));
            Resources["SwDisc"] = new SolidColorBrush(Colors.White);
        }
        foreach (var d in Discs.Children.OfType<Button>())
        {
            var button = d;
            d.MouseEnter += (_, _) => DiscLabel.Text = button.ToolTip as string;
            d.MouseLeave += (_, _) => DiscLabel.Text = _discRow ? (Discs.Children[_disc] as Button)?.ToolTip as string : "";
        }
    }

    private int Home => Math.Min(HomeCount, Vm?.Categories.FirstOrDefault()?.Games.Count ?? 0);

    // ------------------------------------------------------------------------------------
    // Building
    // ------------------------------------------------------------------------------------

    protected override void OnRebuilt()
    {
        if (Vm == null) return;
        Strip.Children.Clear();
        _strip.Clear();
        var home = Vm.Categories.FirstOrDefault()?.Games ?? new List<Game>();
        for (var i = 0; i < Math.Min(HomeCount, home.Count); i++)
        {
            var game = home[i];
            var b = new Button { Style = (Style)FindResource("Tile"), DataContext = game };
            b.MouseEnter += (_, _) => { if (!_gridMode && FullyVisible(b) && HoverIsReal()) SelectHome(game); };
            b.Click += (_, _) => { if (Vm?.Current == game && !_onAllTile) Vm.Activate(); else SelectHome(game); };
            Controls.GameMenu.SetEnable(b, true);
            _strip.Add(b);
            Strip.Children.Add(b);
        }
        _allTile = BuildAllTile();
        Strip.Children.Add(_allTile);
        if (_gridMode) BuildGrid();
    }

    private Button BuildAllTile()
    {
        var b = new Button { Style = (Style)FindResource("BareButton"), Width = 300, Height = 300 };
        var root = new Grid();
        var frame = new Border { Name = "Frame", Margin = new Thickness(-11), BorderThickness = new Thickness(5), Opacity = 0 };
        frame.SetResourceReference(Border.BorderBrushProperty, "SwSelect");
        var face = new Border();
        face.SetResourceReference(Border.BackgroundProperty, "SwTile");
        var icon = new System.Windows.Controls.Primitives.UniformGrid { Rows = 3, Columns = 3, Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 40) };
        for (var i = 0; i < 9; i++)
        {
            var sq = new Border { Margin = new Thickness(4), CornerRadius = new CornerRadius(3) };
            sq.SetResourceReference(Border.BackgroundProperty, "SwMuted");
            icon.Children.Add(sq);
        }
        var label = new TextBlock { Text = "Tous les logiciels", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 46) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SwText");
        root.Children.Add(frame);
        root.Children.Add(face);
        root.Children.Add(icon);
        root.Children.Add(label);
        b.Content = root;
        b.Tag = frame;
        b.MouseEnter += (_, _) => { if (!_gridMode && FullyVisible(b) && HoverIsReal()) SelectAllTile(); };
        b.Click += (_, _) => OpenGrid(1);
        return b;
    }

    private void BuildGrid()
    {
        if (Vm == null) return;
        GridPanel.Children.Clear();
        _grid.Clear();
        foreach (var game in Vm.Games)
        {
            var g = game;
            var b = new Button { Style = (Style)FindResource("GridTile"), DataContext = g };
            b.MouseEnter += (_, _) => { if (HoverIsReal()) Vm?.Select(g); };
            b.Click += (_, _) => { if (Vm?.Current == g) Vm.Activate(); else Vm?.Select(g); };
            Controls.GameMenu.SetEnable(b, true);
            _grid.Add(b);
            GridPanel.Children.Add(b);
        }
        GridTitle.Text = Vm.Category?.Label == "Tous les jeux" ? "Tous les logiciels" : Vm.Category?.Label;
    }

    // ------------------------------------------------------------------------------------
    // Selection
    // ------------------------------------------------------------------------------------

    private void SelectHome(Game game)
    {
        if (Vm == null) return;
        _discRow = false;
        if (Vm.CategoryIndex != 0) { Vm.CategoryIndex = 0; }
        var wasAll = _onAllTile;
        _onAllTile = false;
        var before = Vm.Index;
        Vm.Select(game);
        if (wasAll && before == Vm.Index) OnSelectionChanged(true);
    }

    private void SelectAllTile()
    {
        if (_onAllTile && !_discRow) return;
        _onAllTile = true;
        _discRow = false;
        Main.Sound.Play(Sfx.Tick);
        OnSelectionChanged(true);
    }

    protected override void OnSelectionChanged(bool animate)
    {
        if (Vm == null) return;
        HintB.Visibility = _gridMode ? Visibility.Visible : Visibility.Collapsed;
        GridPrev.Visibility = GridNext.Visibility = Vm.Categories.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        if (_gridMode)
        {
            for (var i = 0; i < _grid.Count; i++)
                SetFrame(_grid[i], i == Vm.Index);
            if (Vm.Index < _grid.Count) _grid[Vm.Index].BringIntoView();
            return;
        }

        if (Vm.CategoryIndex == 0 && !_onAllTile && Home > 0 && Vm.Index >= Home) { Vm.MoveTo(Home - 1, sound: false); return; }
        var sel = _discRow ? -1 : _onAllTile ? Home : Vm.CategoryIndex == 0 ? Vm.Index : -1;
        for (var i = 0; i < _strip.Count; i++) SetFrame(_strip[i], i == sel);
        if (_allTile?.Tag is Border allFrame) Animate(allFrame, OpacityProperty, sel == Home ? 1 : 0, 120);
        SelectedTitle.Inlines.Clear();
        SelectedTitle.Inlines.Add(new System.Windows.Documents.Run(_discRow ? "" : _onAllTile ? "Tous les logiciels" : Vm.Current?.Name ?? ""));
        // the achievements of the selected game, a little lighter next to its name
        if (!_discRow && !_onAllTile && Vm.Current?.AchievementsShortText is { } ach)
            SelectedTitle.Inlines.Add(new System.Windows.Documents.Run("    " + ach) { FontSize = 20, Foreground = SelectedTitle.Foreground.CloneCurrentValue() is SolidColorBrush b ? new SolidColorBrush(b.Color) { Opacity = 0.6 } : null });

        // keep the selected square in view
        if (sel >= 0)
        {
            var t = (TranslateTransform)Strip.RenderTransform;
            var x = StripLeft + sel * TileStep + t.X;
            var target = t.X;
            if (x + 300 > StripRight) target -= x + 300 - StripRight;
            if (x < StripLeft) target += StripLeft - x;
            if (Math.Abs(target - t.X) > 0.5)
                t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(animate ? 220 : 0)) { EasingFunction = Ease });
        }

        for (var i = 0; i < Discs.Children.Count; i++)
            if (Discs.Children[i] is Button d) d.Tag = _discRow && i == _disc ? "focus" : "";
        DiscLabel.Text = _discRow ? (Discs.Children[_disc] as Button)?.ToolTip as string : "";
    }

    /// <summary>
    /// Hovering selects a square only when it is entirely on screen: selecting a half-hidden one scrolls the row,
    /// which would slide another square under the pointer. Those are selected with a click (or the wheel).
    /// </summary>
    private bool FullyVisible(FrameworkElement tile)
    {
        var x = tile.TranslatePoint(new Point(0, 0), HomeLayer).X;
        return x >= StripLeft - 20 && x + tile.ActualWidth <= StripRight + 20;
    }

    protected override bool HandleWheel(int step)
    {
        if (_gridMode) return false; // the grid scrolls normally
        if (_discRow) { _discRow = false; OnSelectionChanged(true); }
        HandlePad(step > 0 ? PadButton.Right : PadButton.Left);
        return true;
    }

    private static void SetFrame(Button b, bool on)
    {
        if (b.Template?.FindName("Frame", b) is UIElement frame) Animate(frame, OpacityProperty, on ? 1 : 0, 120);
        if (on && b.Template?.FindName("Frame", b) is UIElement f)
        {
            // short "pop" of the selection frame
            var s = ScaleOf(f);
            s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
            s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
        }
    }

    // ------------------------------------------------------------------------------------
    // All software
    // ------------------------------------------------------------------------------------

    private void OpenGrid(int categoryIndex)
    {
        if (Vm == null) return;
        _gridMode = true;
        _discRow = false;
        Vm.SetCategory(Math.Clamp(categoryIndex, 0, Vm.Categories.Count - 1));
        BuildGrid();
        GridLayer.Visibility = Visibility.Visible;
        HomeLayer.Visibility = Visibility.Collapsed;
        GridScroll.ScrollToTop();
        GridLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        OnSelectionChanged(false);
    }

    private void CloseGrid()
    {
        if (Vm == null || !_gridMode) return;
        _gridMode = false;
        GridLayer.Visibility = Visibility.Collapsed;
        HomeLayer.Visibility = Visibility.Visible;
        _onAllTile = true;
        Vm.SetCategory(0);
        Main.Sound.Play(Sfx.Back);
        OnSelectionChanged(false);
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        var fav = Vm?.Categories.FindIndex(c => c.Key == "fav") ?? -1;
        if (fav < 0) { Main.ShowToast("Aucun favori pour l'instant : F ou Y sur un jeu pour l'ajouter", ToastKind.Info); return; }
        OpenGrid(fav);
    }

    private void Add_Click(object sender, RoutedEventArgs e) => ShowAddMenu(AddDisc);

    // ------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------

    public override void HandlePad(PadButton button)
    {
        if (Vm == null) return;
        if (_gridMode)
        {
            switch (button)
            {
                case PadButton.Left: Vm.Move(-1); break;
                case PadButton.Right: Vm.Move(1); break;
                case PadButton.Up: Vm.Move(-GridColumns); break;
                case PadButton.Down: Vm.Move(GridColumns); break;
                case PadButton.A: Vm.Activate(); break;
                case PadButton.B: CloseGrid(); break;
                case PadButton.Y: Vm.ToggleFavoriteCommand.Execute(null); break;
                case PadButton.X: Vm.RandomCommand.Execute(null); break;
                case PadButton.LB: if (Vm.CategoryIndex > 1) { Vm.SwitchCategory(-1); BuildGrid(); OnSelectionChanged(false); } break;
                case PadButton.RB: if (Vm.SwitchCategory(1)) { BuildGrid(); OnSelectionChanged(false); } break;
                case PadButton.Start: Vm.OpenDetailsCommand.Execute(null); break;
            }
            return;
        }

        if (_discRow)
        {
            switch (button)
            {
                case PadButton.Left: if (_disc > 0) { _disc--; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); } break;
                case PadButton.Right: if (_disc < Discs.Children.Count - 1) { _disc++; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); } break;
                case PadButton.Up: _discRow = false; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); break;
                case PadButton.A:
                    if (Discs.Children[_disc] is Button d)
                    {
                        if (d.Command?.CanExecute(null) == true) d.Command.Execute(null);
                        else d.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    break;
            }
            return;
        }

        if (Vm.CategoryIndex != 0) Vm.CategoryIndex = 0;
        switch (button)
        {
            case PadButton.Left:
                if (_onAllTile) { _onAllTile = false; Vm.MoveTo(Home - 1); OnSelectionChanged(true); }
                else Vm.Move(-1);
                break;
            case PadButton.Right:
                if (!_onAllTile && (Vm.Index >= Home - 1)) SelectAllTile();
                else if (!_onAllTile) Vm.Move(1);
                break;
            case PadButton.Down: _discRow = true; Main.Sound.Play(Sfx.Tick); OnSelectionChanged(true); break;
            case PadButton.A: if (_onAllTile) OpenGrid(1); else Vm.Activate(); break;
            case PadButton.Y: if (!_onAllTile) Vm.ToggleFavoriteCommand.Execute(null); break;
            case PadButton.X: OpenGrid(1); Vm.RandomCommand.Execute(null); break;
            case PadButton.RB: OpenGrid(1); break;
            case PadButton.Start: if (!_onAllTile) Vm.OpenDetailsCommand.Execute(null); break;
            case PadButton.Back: Main.OpenThemesCommand.Execute(null); break;
        }
    }
}
