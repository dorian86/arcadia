using System.Windows;
using System.Windows.Controls;
using Arcadia.Controls;
using Arcadia.Models;
using Arcadia.ViewModels;

namespace Arcadia.Views;

/// <summary>A horizontal row of games on the home page ("Reprendre la partie", "Favoris"...).</summary>
public partial class GameShelf : UserControl
{
    public GameShelf()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(GameShelf), new PropertyMetadata(""));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty GamesProperty = DependencyProperty.Register(
        nameof(Games), typeof(IReadOnlyList<Game>), typeof(GameShelf),
        new PropertyMetadata(null, (d, e) => ((GameShelf)d).OnGamesChanged(e.NewValue as IReadOnlyList<Game>)));

    public IReadOnlyList<Game>? Games
    {
        get => (IReadOnlyList<Game>?)GetValue(GamesProperty);
        set => SetValue(GamesProperty, value);
    }

    public static readonly DependencyProperty SeeAllSectionProperty =
        DependencyProperty.Register(nameof(SeeAllSection), typeof(Section), typeof(GameShelf), new PropertyMetadata(Section.All));

    public Section SeeAllSection
    {
        get => (Section)GetValue(SeeAllSectionProperty);
        set => SetValue(SeeAllSectionProperty, value);
    }

    private void OnGamesChanged(IReadOnlyList<Game>? games)
    {
        Items.ItemsSource = games;
        var count = games?.Count ?? 0;
        CountText.Text = count.ToString();
        Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Left_Click(object sender, RoutedEventArgs e) => SmoothScroll.ScrollHorizontalBy(Strip, -Math.Max(400, Strip.ViewportWidth * 0.8));
    private void Right_Click(object sender, RoutedEventArgs e) => SmoothScroll.ScrollHorizontalBy(Strip, Math.Max(400, Strip.ViewportWidth * 0.8));

    private void SeeAll_Click(object sender, RoutedEventArgs e)
    {
        var vm = MainViewModel.Instance;
        if (SeeAllSection == Section.All) vm.SortMode = SortMode.AddedDesc;
        vm.NavigateCommand.Execute(SeeAllSection);
    }
}
