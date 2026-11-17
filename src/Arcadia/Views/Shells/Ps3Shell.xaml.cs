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

/// <summary>"PS3" theme: a horizontal bar of categories, the games listed vertically under it, a drifting wave.</summary>
public partial class Ps3Shell : ConsoleShell
{
    private const double AnchorX = 330, CatStep = 150, BarTop = 190, SelectedTop = 352, Step = 92;
    private readonly List<FrameworkElement> _items = new();
    private readonly List<StackPanel> _cats = new();
    private int _builtCategory = -1;
    private Game? _bgGame;
    private bool _options;
    private int _option;
    private readonly List<(string Label, string Glyph, Action Run)> _optionEntries = new();

    // background tint of each month (Arcadia's own palette)
    private static readonly (string Top, string Mid)[] Months =
    {
        ("#6E7FA6", "#2C3550"), ("#A9729F", "#45274A"), ("#5E9E63", "#1F4126"), ("#B9973F", "#544014"),
        ("#3F9B6C", "#173F2A"), ("#B07F51", "#4B321D"), ("#2F86CC", "#103A70"), ("#2A82A6", "#0E3651"),
        ("#7D5FB2", "#2F1E4C"), ("#C07A35", "#55320F"), ("#86664B", "#38291C"), ("#B14646", "#4A1818"),
    };

    public Ps3Shell()
    {
        InitializeComponent();
        var month = Months[DateTime.Now.Month - 1];
        BgTop.Color = (Color)ColorConverter.ConvertFromString(month.Top);
        BgMid.Color = (Color)ColorConverter.ConvertFromString(month.Mid);
    }

    public override void OnShown()
    {
        base.OnShown();
        // endless drift: the ribbons repeat every 1200 px
        var t = (TranslateTransform)Wave.RenderTransform;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -1200, TimeSpan.FromSeconds(38)) { RepeatBehavior = RepeatBehavior.Forever });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, 14, TimeSpan.FromSeconds(7))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    public override void OnHidden()
    {
        base.OnHidden();
        var t = (TranslateTransform)Wave.RenderTransform;
        t.BeginAnimation(TranslateTransform.XProperty, null);
        t.BeginAnimation(TranslateTransform.YProperty, null);
    }

    // ------------------------------------------------------------------------------------
    // Building
    // ------------------------------------------------------------------------------------

    protected override void OnRebuilt()
    {
        if (Vm == null) return;
        CategoryRow.Children.Clear();
        _cats.Clear();
        for (var i = 0; i < Vm.Categories.Count; i++)
        {
            var index = i;
            var c = Vm.Categories[i];
            var panel = new StackPanel { Width = CatStep, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            panel.Children.Add(new TextBlock { Text = c.Glyph, Style = (Style)FindResource("Glyph"), FontSize = 56, Foreground = Brushes.White, Height = 74 });
            panel.Children.Add(new TextBlock { Text = c.Label, FontSize = 22, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = CatStep + 40 });
            panel.MouseLeftButtonUp += (_, _) => { CloseOptions(); Vm?.SetCategory(index); };
            panel.MouseEnter += (_, _) => { if (Vm?.CategoryIndex != index) Animate(panel, OpacityProperty, 0.85, 120); };
            panel.MouseLeave += (_, _) => { if (Vm?.CategoryIndex != index) Animate(panel, OpacityProperty, 0.55, 120); };
            _cats.Add(panel);
            CategoryRow.Children.Add(panel);
        }
        _builtCategory = -1;
    }

    private void BuildItems()
    {
        if (Vm?.Category is not { } cat) return;
        Items.Children.Clear();
        _items.Clear();
        _builtCategory = Vm.CategoryIndex;
        if (cat.IsActions)
        {
            foreach (var a in cat.Actions) _items.Add(BuildAction(a));
        }
        else
        {
            foreach (var g in cat.Games) _items.Add(BuildGame(g));
        }
        foreach (var item in _items)
        {
            Canvas.SetTop(item, SelectedTop); // animated from here (an unset Canvas.Top cannot be animated)
            Items.Children.Add(item);
        }
    }

    private FrameworkElement BuildGame(Game game)
    {
        var root = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, DataContext = game };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var thumb = new Grid { Width = 128, Height = 72, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        var bg = new Border();
        bg.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Game.Name)) { Converter = (System.Windows.Data.IValueConverter)FindResource("NameToBrush") });
        var art = new Border();
        ImageLoader.SetDecodeWidth(art, 260);
        art.SetBinding(ImageLoader.SourceProperty, new System.Windows.Data.Binding(nameof(Game.WideTilePath)));
        thumb.Children.Add(bg);
        thumb.Children.Add(art);
        // the logo is only drawn here until the wide tile (which already carries it) has been built
        var logo = new Image { Margin = new Thickness(10, 6, 10, 6) };
        ImageLoader.SetDecodeWidth(logo, 200);
        ImageLoader.SetSource(logo, game.LogoFullPath);
        logo.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Game.WideTileNeedsLogo)) { Converter = (System.Windows.Data.IValueConverter)FindResource("BoolToVis") });
        thumb.Children.Add(logo);
        root.Children.Add(thumb);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 14, 0, 0) };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = game.Name, FontSize = 26, Foreground = Brushes.White, MaxWidth = 820, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = game.PlayTimeSeconds > 0 ? $"{game.PlatformLabel}   ·   {game.PlayTimeText}   ·   {game.LastPlayedText}" : game.PlatformLabel,
            FontSize = 18, Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed,
        });
        root.Children.Add(text);
        root.Tag = (thumb, text);
        root.MouseLeftButtonUp += (_, _) => { CloseOptions(); if (Vm?.Current == game) Vm.Activate(); else Vm?.Select(game); };
        GameMenu.SetEnable(root, true);
        return root;
    }

    private FrameworkElement BuildAction(ShellAction action)
    {
        var root = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var thumb = new Grid { Width = 128, Height = 72, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        thumb.Children.Add(new TextBlock { Text = action.Glyph, Style = (Style)FindResource("Glyph"), FontSize = 40, Foreground = Brushes.White });
        root.Children.Add(thumb);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 14, 0, 0) };
        Grid.SetColumn(text, 1);
        text.Children.Add(new TextBlock { Text = action.Label, FontSize = 26, Foreground = Brushes.White });
        text.Children.Add(new TextBlock { Text = action.Description, FontSize = 18, Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed });
        root.Children.Add(text);
        root.Tag = (thumb, text);
        root.MouseLeftButtonUp += (_, _) =>
        {
            if (Vm == null) return;
            var i = _items.IndexOf(root);
            if (i == Vm.Index) Vm.Activate(); else Vm.MoveTo(i);
        };
        return root;
    }

    // ------------------------------------------------------------------------------------
    // Layout of the selection
    // ------------------------------------------------------------------------------------

    protected override void OnSelectionChanged(bool animate)
    {
        if (Vm == null) return;
        ClockDate.Text = DateTime.Now.ToString("d/M", Format.Fr);
        var ms = animate ? 260 : 0;
        if (_builtCategory != Vm.CategoryIndex) { BuildItems(); animate = false; ms = 0; }

        // category bar: the selected icon sits above the list
        var row = (TranslateTransform)CategoryRow.RenderTransform;
        row.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(AnchorX - CatStep / 2 - Vm.CategoryIndex * CatStep, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });
        for (var i = 0; i < _cats.Count; i++)
        {
            var on = i == Vm.CategoryIndex;
            Animate(_cats[i], OpacityProperty, on ? 1 : 0.55, ms);
            ScaleTo(_cats[i].Children[0] as UIElement ?? _cats[i], on ? 1.12 : 0.8, ms);
            ((UIElement)_cats[i].Children[1]).Visibility = on ? Visibility.Visible : Visibility.Hidden;
        }

        // items: selected one under the bar, previous ones above it
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var k = i - Vm.Index;
            if (k < -4 || k > 8) { item.Visibility = Visibility.Collapsed; continue; }
            item.Visibility = Visibility.Visible;
            var top = k == 0 ? SelectedTop : k > 0 ? SelectedTop + 150 + (k - 1) * Step : BarTop - 40 + k * Step;
            Canvas.SetLeft(item, AnchorX - 100);
            item.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Ease });
            Animate(item, OpacityProperty, k == 0 ? 1 : k < 0 ? 0.45 : 0.75, ms);
            if (item.Tag is (Grid thumb, StackPanel text))
            {
                var s = k == 0 ? 1.45 : 1;
                var st = ScaleOf(thumb);
                thumb.RenderTransformOrigin = new Point(0.5, 0);
                Animate(st, ScaleTransform.ScaleXProperty, s, ms);
                Animate(st, ScaleTransform.ScaleYProperty, s, ms);
                text.Margin = new Thickness(k == 0 ? 52 : 18, k == 0 ? 22 : 14, 0, 0);
                ((TextBlock)text.Children[0]).FontSize = k == 0 ? 30 : 24;
                text.Children[1].Visibility = k == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        ShowBackground(Vm.Current);
        if (_options) BuildOptions();
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
        GameBg.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 0.32, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(250) });
    }

    // ------------------------------------------------------------------------------------
    // Options panel
    // ------------------------------------------------------------------------------------

    private void OpenOptions()
    {
        if (Vm?.Current is null) return;
        _options = true;
        _option = 0;
        BuildOptions();
        Options.Visibility = Visibility.Visible;
        OptionsShield.Visibility = Visibility.Visible;
        var t = (TranslateTransform)Options.RenderTransform;
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(430, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = Ease });
        Main.Sound.Play(Sfx.Open);
    }

    private void CloseOptions()
    {
        if (!_options) return;
        _options = false;
        OptionsShield.Visibility = Visibility.Collapsed;
        var t = (TranslateTransform)Options.RenderTransform;
        var a = new DoubleAnimation(430, TimeSpan.FromMilliseconds(200)) { EasingFunction = Ease };
        a.Completed += (_, _) => { if (!_options) Options.Visibility = Visibility.Collapsed; };
        t.BeginAnimation(TranslateTransform.XProperty, a);
        Main.Sound.Play(Sfx.Back);
    }

    private void BuildOptions()
    {
        var g = Vm?.Current;
        if (g == null || Vm == null) return;
        _optionEntries.Clear();
        _optionEntries.Add(("Lancer", "", () => Vm.Activate()));
        _optionEntries.Add(("Fiche du jeu", "", () => Vm.OpenDetailsCommand.Execute(null)));
        _optionEntries.Add((g.IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris", g.IsFavorite ? "" : "", () => Vm.ToggleFavoriteCommand.Execute(null)));
        _optionEntries.Add(("Modifier…", "", () => Vm.EditCommand.Execute(null)));
        _optionEntries.Add(("Au hasard", "", () => Vm.RandomCommand.Execute(null)));
        OptionList.Children.Clear();
        for (var i = 0; i < _optionEntries.Count; i++)
        {
            var index = i;
            var (label, glyph, _) = _optionEntries[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            row.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Glyph"), FontSize = 22, Foreground = Brushes.White, Width = 34 });
            row.Children.Add(new TextBlock { Text = label, FontSize = 24, Foreground = Brushes.White, Margin = new Thickness(14, 0, 0, 0) });
            row.MouseLeftButtonUp += (_, _) => RunOption(index);
            // hovering moves the highlight (styling only: rebuilding under the pointer would loop)
            row.MouseEnter += (_, _) => { if (_option == index) return; _option = index; Main.Sound.Play(Sfx.Tick); HighlightOption(); };
            OptionList.Children.Add(row);
        }
        HighlightOption();
    }

    private void HighlightOption()
    {
        for (var i = 0; i < OptionList.Children.Count; i++)
        {
            if (OptionList.Children[i] is not StackPanel row) continue;
            var on = i == _option;
            row.Opacity = on ? 1 : 0.6;
            ((TextBlock)row.Children[1]).FontSize = on ? 27 : 24;
        }
    }

    private void OptionsShield_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseOptions();
    }

    protected override bool HandleWheel(int step)
    {
        HandlePad(step > 0 ? PadButton.Down : PadButton.Up);
        return true;
    }

    private void RunOption(int index)
    {
        if (index < 0 || index >= _optionEntries.Count) return;
        var run = _optionEntries[index].Run;
        CloseOptions();
        run();
    }

    // ------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------

    public override void HandlePad(PadButton button)
    {
        if (Vm == null) return;
        if (_options)
        {
            switch (button)
            {
                case PadButton.Up: if (_option > 0) { _option--; Main.Sound.Play(Sfx.Tick); HighlightOption(); } break;
                case PadButton.Down: if (_option < _optionEntries.Count - 1) { _option++; Main.Sound.Play(Sfx.Tick); HighlightOption(); } break;
                case PadButton.A: RunOption(_option); break;
                case PadButton.B: case PadButton.Y: case PadButton.Left: CloseOptions(); break;
            }
            return;
        }
        switch (button)
        {
            case PadButton.Left: case PadButton.LB: Vm.SwitchCategory(-1); break;
            case PadButton.Right: case PadButton.RB: Vm.SwitchCategory(1); break;
            case PadButton.Up: Vm.Move(-1); break;
            case PadButton.Down: Vm.Move(1); break;
            case PadButton.A: Vm.Activate(); break;
            case PadButton.Y: case PadButton.Start: OpenOptions(); break;
            case PadButton.X: Vm.RandomCommand.Execute(null); break;
            case PadButton.Back: Main.OpenThemesCommand.Execute(null); break;
        }
    }
}
