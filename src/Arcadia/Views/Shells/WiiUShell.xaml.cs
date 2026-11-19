using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Arcadia.Models;
using Arcadia.Services;

namespace Arcadia.Views.Shells;

/// <summary>"Wii U" theme: round app buttons, a large banner of the selected game and pages of square icons.</summary>
public partial class WiiUShell : ConsoleShell
{
    private const int PerPage = 14, Columns = 7;
    private readonly Button[] _slots = new Button[PerPage];
    private int _page = -1;

    public WiiUShell()
    {
        InitializeComponent();
        for (var i = 0; i < PerPage; i++)
        {
            var slot = i;
            var b = new Button { Style = (Style)FindResource("Icon"), Margin = new Thickness(16), DataContext = null };
            b.MouseEnter += (_, _) => { if (b.DataContext is Game && HoverIsReal()) Vm?.MoveTo(_page * PerPage + slot); };
            b.Click += (_, _) =>
            {
                if (b.DataContext is not Game g) { ShowAddMenu(b); return; }
                if (Vm?.Current == g) Vm.Activate(); // second click on the selected icon launches it
                else Vm?.Select(g);
            };
            Controls.GameMenu.SetEnable(b, true);
            _slots[i] = b;
            IconGrid.Children.Add(b);
        }
    }

    private int PageCount => Math.Max(1, (int)Math.Ceiling((Vm?.Games.Count ?? 0) / (double)PerPage));

    protected override void OnRebuilt() => _page = -1;

    protected override void OnSelectionChanged(bool animate)
    {
        if (Vm == null) return;
        CategoryText.Text = Vm.Category is { } c ? $"{c.Label}  ·  {c.Games.Count} jeu{(c.Games.Count > 1 ? "x" : "")}" : "";
        var page = Vm.Index / PerPage;
        if (page != _page) ShowPage(page, animate ? Math.Sign(page - _page) : 0);
        var selected = Vm.Index % PerPage;
        for (var i = 0; i < PerPage; i++)
        {
            var on = i == selected && _slots[i].DataContext is Game;
            ScaleTo(_slots[i], on ? 1.1 : 1, 220);
            Panel.SetZIndex(_slots[i], on ? 1 : 0);
            if (_slots[i].Template?.FindName("Pulse", _slots[i]) is UIElement pulse)
            {
                pulse.BeginAnimation(OpacityProperty, on
                    ? new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                    : null);
                pulse.Opacity = 0;
            }
        }

        var g = Vm.Current;
        if (g != null)
        {
            BannerInfo.Text = (g.PlayTimeSeconds > 0 ? $"{g.PlatformLabel}   ·   {g.PlayTimeText} de jeu   ·   {g.LastPlayedText}" : g.PlatformLabel)
                              + (g.AchievementsShortText is { } ach ? $"   ·   {ach}" : "");
            FavGlyph.Text = g.IsFavorite ? "" : "";
            if (animate)
            {
                Banner.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(300)));
            }
        }
    }

    private void ShowPage(int page, int direction)
    {
        if (Vm == null) return;
        var old = _page;
        _page = page;
        for (var i = 0; i < PerPage; i++)
        {
            var index = page * PerPage + i;
            _slots[i].DataContext = index < Vm.Games.Count ? Vm.Games[index] : null;
        }
        PrevPage.Visibility = page > 0 ? Visibility.Visible : Visibility.Hidden;
        NextPage.Visibility = page < PageCount - 1 ? Visibility.Visible : Visibility.Hidden;
        PageDots.Items.Clear();
        for (var p = 0; p < PageCount && PageCount > 1; p++)
            PageDots.Items.Add(new Ellipse { Width = 12, Height = 12, Margin = new Thickness(6, 0, 6, 0), Fill = new SolidColorBrush(p == page ? Color.FromRgb(0x12, 0xB5, 0xF0) : Color.FromRgb(0xC9, 0xD0, 0xD6)) });
        if (direction != 0 && old >= 0)
        {
            var t = (TranslateTransform)IconGrid.RenderTransform;
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction * 220, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = Ease });
            IconGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(280)));
        }
    }

    protected override bool HandleWheel(int step)
    {
        if (Vm == null) return false;
        var target = (_page + step) * PerPage;
        if (target >= 0 && target < Vm.Games.Count) Vm.MoveTo(target);
        return true;
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e) => Vm?.MoveTo((_page - 1) * PerPage);
    private void NextPage_Click(object sender, RoutedEventArgs e) => Vm?.MoveTo((_page + 1) * PerPage);
    private void Add_Click(object sender, RoutedEventArgs e) => ShowAddMenu(AddButton);

    public override void HandlePad(PadButton button)
    {
        if (Vm == null) return;
        var col = Vm.Index % PerPage % Columns;
        switch (button)
        {
            case PadButton.Left: Vm.Move(col == 0 ? -(PerPage - Columns + 1) : -1); break;
            case PadButton.Right:
                if (col == Columns - 1) { if (!Vm.Move(PerPage - Columns + 1)) Vm.MoveTo(Vm.Games.Count - 1); }
                else Vm.Move(1);
                break;
            case PadButton.Up: Vm.Move(-Columns); break;
            case PadButton.Down: Vm.Move(Columns); break;
            case PadButton.A: Vm.Activate(); break;
            case PadButton.Y: Vm.ToggleFavoriteCommand.Execute(null); OnSelectionChanged(false); break;
            case PadButton.X: Vm.RandomCommand.Execute(null); break;
            case PadButton.LB: Vm.SwitchCategory(-1, wrap: true); break;
            case PadButton.RB: Vm.SwitchCategory(1, wrap: true); break;
            case PadButton.Start: Vm.OpenDetailsCommand.Execute(null); break;
            case PadButton.Back: Main.OpenThemesCommand.Execute(null); break;
        }
    }
}
