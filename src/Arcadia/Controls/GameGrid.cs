using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Arcadia.Models;

namespace Arcadia.Controls;

public sealed class GameRow
{
    public GameRow(IReadOnlyList<Game> items, int columns, double gap, Thickness margin)
    {
        Items = items;
        Columns = columns;
        Gap = gap;
        Margin = margin;
    }

    public Thickness Margin { get; }

    public IReadOnlyList<Game> Items { get; }
    public int Columns { get; }
    public double Gap { get; }
}

/// <summary>
/// Virtualised, justified grid of game cards: games are chunked into rows hosted by a
/// VirtualizingStackPanel, so hundreds of covers stay smooth and memory-friendly.
/// </summary>
public class GameGrid : Control
{
    private ItemsControl? _host;
    private ScrollViewer? _scroll;
    private int _columns;

    static GameGrid()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GameGrid), new FrameworkPropertyMetadata(typeof(GameGrid)));
    }

    public static readonly DependencyProperty GamesProperty = DependencyProperty.Register(
        nameof(Games), typeof(IReadOnlyList<Game>), typeof(GameGrid),
        new PropertyMetadata(null, (d, _) => ((GameGrid)d).Rebuild(force: true)));

    public IReadOnlyList<Game>? Games
    {
        get => (IReadOnlyList<Game>?)GetValue(GamesProperty);
        set => SetValue(GamesProperty, value);
    }

    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(GameGrid),
        new PropertyMetadata(184.0, (d, _) => ((GameGrid)d).Rebuild(force: false)));

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(GameGrid), new PropertyMetadata(22.0));

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(GameGrid), new PropertyMetadata(null));

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _host = GetTemplateChild("PART_Rows") as ItemsControl;
        if (_host != null)
        {
            _host.Loaded += (_, _) => _scroll ??= VisualTreeUtil.FindDescendant<ScrollViewer>(_host);
        }
        Rebuild(force: true);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (sizeInfo.WidthChanged) Rebuild(force: false);
    }

    public void ScrollToTop()
    {
        _scroll ??= _host != null ? VisualTreeUtil.FindDescendant<ScrollViewer>(_host) : null;
        if (_scroll != null) SmoothScroll.ScrollToTop(_scroll);
    }

    private int ComputeColumns()
    {
        var available = ActualWidth - Padding.Left - Padding.Right;
        if (available <= 0) return 0;
        var gap = Gap;
        return Math.Max(1, (int)Math.Floor((available + gap) / (CardWidth + gap)));
    }

    private void Rebuild(bool force)
    {
        if (_host == null) return;
        var columns = ComputeColumns();
        if (columns == 0) return;
        if (!force && columns == _columns) return;
        _columns = columns;

        var games = Games ?? Array.Empty<Game>();
        var rows = new List<GameRow>((games.Count + columns - 1) / columns);
        for (var i = 0; i < games.Count; i += columns)
        {
            var slice = new List<Game>(columns);
            for (var j = i; j < Math.Min(i + columns, games.Count); j++) slice.Add(games[j]);
            var margin = new Thickness(
                Padding.Left - Gap / 2,
                i == 0 ? Padding.Top : 0,
                Padding.Right - Gap / 2,
                i + columns >= games.Count ? Padding.Bottom : 0);
            rows.Add(new GameRow(slice, columns, Gap, margin));
        }
        _host.ItemsSource = rows;
    }
}

/// <summary>Turns the grid gap into the margin of each card cell.</summary>
public sealed class GapToMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var gap = value is double d ? d : 20;
        return new Thickness(gap / 2, 0, gap / 2, gap + 4);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
}
