using System.Globalization;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>The "Ton année de jeu" window: one page per year, from the sessions recorded by Arcadia.</summary>
public partial class RecapViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly List<int> _years;
    private YearRecap _recap = null!;

    public RecapViewModel(MainViewModel main, int? year = null)
    {
        _main = main;
        _years = YearRecap.Years(main.Library.Games);
        Year = year is int y && _years.Contains(y) ? y : _years.First();
    }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(PreviousYearCommand), nameof(NextYearCommand))]
    private int _year;

    partial void OnYearChanged(int value)
    {
        _recap = YearRecap.Build(_main.Library.Games, value, _main.Trophies.Unlocked);
        OnPropertyChanged(string.Empty); // every text below depends on the year
    }

    public bool HasPlay => _recap.HasPlay;
    public string YearText => Year.ToString(CultureInfo.InvariantCulture);

    public string Period => Year == DateTime.Today.Year
        ? $"Du 1er janvier au {DateTime.Today.ToString("d MMMM", Format.Fr)}"
        : "Toute l'année";

    // ---- key figures ----
    public string TotalTime => _recap.Seconds > 0 ? Format.Duration(_recap.Seconds) : "0 h";
    public string GamesPlayed => _recap.Games.Count.ToString(Format.Fr);
    public string Sessions => _recap.Sessions.ToString("N0", Format.Fr);
    public string Achievements => _recap.AchievementsUnlocked.ToString("N0", Format.Fr);

    // ---- game of the year ----
    public Game? GameOfTheYear => _recap.Games.FirstOrDefault().Game;
    public string GameOfTheYearText => _recap.Games.Count == 0 ? "" :
        $"{Format.Duration(_recap.Games[0].Seconds)}  ·  {Plural(_recap.Games[0].Sessions, "session")}  ·  {Math.Round(100.0 * _recap.Games[0].Seconds / Math.Max(1, _recap.Seconds))} % de ton temps de jeu";

    public List<BarItem> TopGames
    {
        get
        {
            var top = _recap.Games.Take(5).ToList();
            var max = Math.Max(1, top.FirstOrDefault().Seconds);
            return top.Select(g => new BarItem { Label = g.Game.Name, Value = Format.Duration(g.Seconds), Ratio = (double)g.Seconds / max, Game = g.Game }).ToList();
        }
    }

    public List<BarItem> Months
    {
        get
        {
            var max = Math.Max(1, _recap.Months.Max());
            return _recap.Months.Select((s, i) => new BarItem
            {
                Label = new DateTime(Year, i + 1, 1).ToString("MMM", Format.Fr).TrimEnd('.'),
                Value = s > 0 ? Format.DurationShort(s) : "",
                Ratio = Math.Max(0.02, s / (double)max),
                Tooltip = $"{Format.Fr.TextInfo.ToTitleCase(new DateTime(Year, i + 1, 1).ToString("MMMM", Format.Fr))} : {(s > 0 ? Format.Duration(s) : "aucune partie")}",
            }).ToList();
        }
    }

    public string BestMonth
    {
        get
        {
            if (!_recap.HasPlay) return "—";
            var i = Array.IndexOf(_recap.Months, _recap.Months.Max());
            return Format.Fr.TextInfo.ToTitleCase(new DateTime(Year, i + 1, 1).ToString("MMMM", Format.Fr));
        }
    }

    // ---- fun facts ----
    public string LongestSession => _recap.Longest is { } l ? Format.Duration(l.Session.Seconds) : "—";
    public string LongestSessionDetail => _recap.Longest is { } l ? $"sur {l.Game.Name}, le {l.Session.Start.ToString("d MMMM", Format.Fr)}" : "";
    public string FavoriteDay => _recap.FavoriteDay is DayOfWeek d ? Format.Fr.TextInfo.ToTitleCase(Format.Fr.DateTimeFormat.GetDayName(d)) : "—";

    public string FavoriteMoment => _recap.FavoriteMoment switch
    {
        0 => "La nuit",
        1 => "Le matin",
        2 => "L'après-midi",
        3 => "Le soir",
        _ => "—",
    };

    public string GamesAdded => _recap.GamesAdded.ToString(Format.Fr);
    public string TrophiesUnlocked => _recap.TrophiesUnlocked.ToString(Format.Fr);

    private static string Plural(int n, string word) => $"{n.ToString("N0", Format.Fr)} {word}{(n > 1 ? "s" : "")}";

    // ---- navigation ----
    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousYear() => Year = _years[_years.IndexOf(Year) + 1];
    private bool CanGoPrevious() => _years.IndexOf(Year) < _years.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextYear() => Year = _years[_years.IndexOf(Year) - 1];
    private bool CanGoNext() => _years.IndexOf(Year) > 0;

    [RelayCommand]
    private void OpenGame(Game? game)
    {
        if (game == null) return;
        _main.CloseDialog(this);
        _main.OpenDetailCommand.Execute(game);
    }

    [RelayCommand]
    private void Close() => _main.CloseDialog(this);
}
