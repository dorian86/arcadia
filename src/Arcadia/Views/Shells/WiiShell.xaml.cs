using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Arcadia.Models;
using Arcadia.Services;

namespace Arcadia.Views.Shells;

/// <summary>"Wii" theme: pages of 12 channels, a clock, round buttons and a channel screen before launching.</summary>
public partial class WiiShell : ConsoleShell
{
    private const int PerPage = 12, Columns = 4;
    private readonly Button[] _slots = new Button[PerPage];
    private int _page = -1;
    private bool _preview;
    private bool _startFocused = true;

    public WiiShell()
    {
        InitializeComponent();
        for (var i = 0; i < PerPage; i++)
        {
            var slot = i;
            var b = new Button { Style = (Style)FindResource("Channel"), Margin = new Thickness(13, 12, 13, 12), DataContext = null, RenderTransformOrigin = new Point(0.5, 0.5) };
            b.MouseEnter += (_, _) => { if (b.DataContext is Game && HoverIsReal()) Vm?.MoveTo(_page * PerPage + slot); };
            b.Click += (_, _) =>
            {
                if (b.DataContext is Game g) { Vm?.Select(g, sound: false); OpenPreview(); }
                else ShowAddMenu(b);
            };
            Controls.GameMenu.SetEnable(b, true);
            _slots[i] = b;
            ChannelGrid.Children.Add(b);
        }
    }

    private int PageCount => Math.Max(1, (int)Math.Ceiling((Vm?.Games.Count ?? 0) / (double)PerPage));

    protected override void OnRebuilt()
    {
        _page = -1; // refill the visible page
        if (_preview && Vm?.Current == null) ClosePreview();
    }

    protected override void OnSelectionChanged(bool animate)
    {
        if (Vm == null) return;
        CategoryText.Text = Vm.Category?.Label ?? "";
        var page = Vm.Index / PerPage;
        if (page != _page) ShowPage(page, animate ? Math.Sign(page - _page) : 0);
        var selected = Vm.Index % PerPage;
        for (var i = 0; i < PerPage; i++)
        {
            var on = i == selected && _slots[i].DataContext is Game;
            if (_slots[i].Template?.FindName("Glow", _slots[i]) is UIElement glow) Animate(glow, OpacityProperty, on ? 1 : 0, 160);
            ScaleTo(_slots[i], on ? 1.06 : 1, 200);
            Panel.SetZIndex(_slots[i], on ? 1 : 0);
        }
        if (_preview) UpdatePreview();
    }

    private void ShowPage(int page, int direction)
    {
        if (Vm == null) return;
        var old = _page;
        _page = page;
        var games = Vm.Games;
        for (var i = 0; i < PerPage; i++)
        {
            var index = page * PerPage + i;
            _slots[i].DataContext = index < games.Count ? games[index] : null;
        }
        PrevPage.Visibility = page > 0 ? Visibility.Visible : Visibility.Hidden;
        NextPage.Visibility = page < PageCount - 1 ? Visibility.Visible : Visibility.Hidden;
        PageText.Text = PageCount > 1 ? $"Page {page + 1} / {PageCount}" : "";
        if (direction != 0 && old >= 0)
        {
            var t = (TranslateTransform)ChannelGrid.RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction * 260, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = Ease });
            ChannelGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(300)));
        }
    }

    // ------------------------------------------------------------------------------------
    // Channel screen
    // ------------------------------------------------------------------------------------

    private void OpenPreview()
    {
        if (Vm?.Current == null) return;
        _preview = true;
        _startFocused = true;
        UpdatePreview();
        PreviewLayer.Visibility = Visibility.Visible;
        Main.Sound.Play(Sfx.Open);
        PreviewLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        var s = ScaleOf(PreviewLayer);
        s.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = Ease });
        s.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = Ease });
    }

    private void UpdatePreview()
    {
        var g = Vm?.Current;
        if (g == null) return;
        PreviewInfo.Text = g.PlayTimeSeconds > 0 ? $"{g.PlatformLabel}  ·  {g.PlayTimeText} de jeu  ·  {g.LastPlayedText}" : g.PlatformLabel;
        FavGlyph.Text = g.IsFavorite ? "" : "";
        FavGlyph.Foreground = g.IsFavorite ? new SolidColorBrush(Color.FromRgb(0xE8, 0x4A, 0x6A)) : new SolidColorBrush(Color.FromRgb(0x5E, 0x67, 0x6F));
        StartButton.Tag = _startFocused ? "focus" : null;
        MenuButton.Tag = _startFocused ? null : "focus";
    }

    private void ClosePreview()
    {
        if (!_preview) return;
        _preview = false;
        Main.Sound.Play(Sfx.Back);
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => { if (!_preview) PreviewLayer.Visibility = Visibility.Collapsed; };
        PreviewLayer.BeginAnimation(OpacityProperty, fade);
    }

    private void ClosePreview_Click(object sender, RoutedEventArgs e) => ClosePreview();

    private void Start_Click(object sender, RoutedEventArgs e) => Vm?.Activate();

    // ------------------------------------------------------------------------------------
    // Input
    // ------------------------------------------------------------------------------------

    protected override bool HandleWheel(int step)
    {
        if (Vm == null || _preview) return false;
        var target = (_page + step) * PerPage;
        if (target >= 0 && target < Vm.Games.Count) Vm.MoveTo(target);
        return true;
    }

    private void AddHint_Click(object sender, RoutedEventArgs e) => ShowAddMenu(AddHint);

    private void PrevPage_Click(object sender, RoutedEventArgs e) => Vm?.MoveTo((_page - 1) * PerPage);
    private void NextPage_Click(object sender, RoutedEventArgs e) => Vm?.MoveTo((_page + 1) * PerPage);
    private void PrevCategory_Click(object sender, RoutedEventArgs e) => Vm?.SwitchCategory(-1, wrap: true);
    private void NextCategory_Click(object sender, RoutedEventArgs e) => Vm?.SwitchCategory(1, wrap: true);

    public override void HandlePad(PadButton button)
    {
        if (Vm == null) return;
        if (_preview)
        {
            switch (button)
            {
                case PadButton.Left: case PadButton.Right:
                    _startFocused = !_startFocused;
                    Main.Sound.Play(Sfx.Tick);
                    UpdatePreview();
                    break;
                case PadButton.A:
                    if (_startFocused) Vm.Activate(); else ClosePreview();
                    break;
                case PadButton.B: ClosePreview(); break;
                case PadButton.Y: Vm.ToggleFavoriteCommand.Execute(null); UpdatePreview(); break;
                case PadButton.Start: Vm.OpenDetailsCommand.Execute(null); break;
            }
            return;
        }

        var col = Vm.Index % PerPage % Columns;
        switch (button)
        {
            case PadButton.Left:
                // first column: previous page, last column of it
                Vm.Move(col == 0 ? -(PerPage - Columns + 1) : -1);
                break;
            case PadButton.Right:
                if (col == Columns - 1) { if (!Vm.Move(PerPage - Columns + 1)) Vm.MoveTo(Vm.Games.Count - 1); }
                else Vm.Move(1);
                break;
            case PadButton.Up: Vm.Move(-Columns); break;
            case PadButton.Down: Vm.Move(Columns); break;
            case PadButton.A: OpenPreview(); break;
            case PadButton.Y: Vm.ToggleFavoriteCommand.Execute(null); break;
            case PadButton.X: Vm.RandomCommand.Execute(null); break;
            case PadButton.LB: Vm.SwitchCategory(-1, wrap: true); break;
            case PadButton.RB: Vm.SwitchCategory(1, wrap: true); break;
            case PadButton.Start: Vm.OpenSettingsCommand.Execute(null); break;
            case PadButton.Back: Main.OpenThemesCommand.Execute(null); break;
        }
    }
}
