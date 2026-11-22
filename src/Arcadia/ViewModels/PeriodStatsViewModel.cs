using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>One bar of the period chart; clicking it opens that day / month / year.</summary>
public sealed class PeriodBar
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public double Ratio { get; init; }
    public string Tooltip { get; init; } = "";
    public DateTime Start { get; init; }
    public bool IsCurrent { get; init; }
    public bool CanOpen { get; init; }
}

/// <summary>
/// "Ton activité" in the statistics page: time played, sessions and games per day, week, month or year, with the
/// previous period for comparison. The choice of period stays while Arcadia is open.
/// </summary>
public partial class PeriodStatsViewModel : ObservableObject
{
    private IReadOnlyList<Game> _games = Array.Empty<Game>();
    private PeriodReport? _report;

    [ObservableProperty] private StatsPeriod _kind = StatsPeriod.Week;
    [ObservableProperty] private DateTime _anchor = DateTime.Today;

    public void Update(IReadOnlyList<Game> games)
    {
        _games = games;
        Rebuild();
    }

    partial void OnKindChanged(StatsPeriod value) => Rebuild();
    partial void OnAnchorChanged(DateTime value) => Rebuild();

    [RelayCommand]
    private void SetPeriod(StatsPeriod kind)
    {
        if (Kind == kind) return;
        Kind = kind;
        MainViewModel.Instance?.Sound.Play(Sfx.Tick);
    }

    [RelayCommand]
    private void Previous()
    {
        Anchor = PeriodStats.Shift(Kind, Anchor, -1);
        MainViewModel.Instance?.Sound.Play(Sfx.Tick);
    }

    [RelayCommand]
    private void Next()
    {
        if (!CanGoNext) return;
        Anchor = PeriodStats.Shift(Kind, Anchor, 1);
        MainViewModel.Instance?.Sound.Play(Sfx.Tick);
    }

    [RelayCommand]
    private void Today()
    {
        Anchor = DateTime.Today;
        MainViewModel.Instance?.Sound.Play(Sfx.Tick);
    }

    /// <summary>A bar opens the period it shows: a day of the week or month, a month of the year, a year.</summary>
    [RelayCommand]
    private void OpenBar(PeriodBar? bar)
    {
        if (bar is not { CanOpen: true }) return;
        var kind = Kind switch { StatsPeriod.Week or StatsPeriod.Month => StatsPeriod.Day, StatsPeriod.Year => StatsPeriod.Month, _ => StatsPeriod.Year };
        _anchor = bar.Start;            // both at once: one rebuild
        Kind = kind;
        OnPropertyChanged(nameof(Anchor));
        MainViewModel.Instance?.Sound.Play(Sfx.Navigate);
    }

    private void Rebuild()
    {
        var first = _games.SelectMany(g => g.Sessions).Select(s => (DateTime?)s.Start).DefaultIfEmpty(null).Min();
        var range = PeriodStats.RangeOf(Kind, Anchor, first);
        _report = PeriodStats.Build(_games, range, DateTime.Now);
        OnPropertyChanged(string.Empty); // every computed property below
    }

    private PeriodReport R => _report!;

    // ---- header ----
    public bool IsDay => Kind == StatsPeriod.Day;
    public bool IsWeek => Kind == StatsPeriod.Week;
    public bool IsMonth => Kind == StatsPeriod.Month;
    public bool IsYear => Kind == StatsPeriod.Year;
    public bool IsAll => Kind == StatsPeriod.All;
    public bool CanNavigate => Kind != StatsPeriod.All;
    public bool CanGoNext => _report != null && Kind != StatsPeriod.All && R.Range.End <= DateTime.Today;
    public bool IsCurrent => _report != null && DateTime.Now >= R.Range.Start && DateTime.Now < R.Range.End;
    public string Title => _report == null ? "" : PeriodStats.Title(R.Range, DateTime.Today);

    // ---- figures ----
    public bool HasData => _report != null && R.Seconds > 0;
    public string TimeText => _report == null || R.Seconds == 0 ? "0 min" : Format.Duration(R.Seconds);

    public string? CompareText
    {
        get
        {
            if (_report == null || Kind == StatsPeriod.All) return null;
            var name = PeriodStats.PreviousName(Kind);
            if (R.PreviousSeconds == 0) return R.Seconds == 0 ? null : $"rien {name}";
            var diff = (R.Seconds - R.PreviousSeconds) * 100.0 / R.PreviousSeconds;
            return Math.Abs(diff) < 1 ? $"autant que {name}"
                : $"{(diff > 0 ? "+" : "−")}{Math.Abs(diff):0} % par rapport à {name}";
        }
    }

    public bool CompareUp => _report != null && R.Seconds >= R.PreviousSeconds;
    public string SessionsText => _report == null ? "0" : R.Sessions.ToString(Format.Fr);
    public string AverageSessionText => _report == null || R.Sessions == 0 ? "" : $"{Format.Duration(R.Seconds / Math.Max(1, R.Sessions))} en moyenne";
    public string GamesText => _report == null ? "0" : R.GamesPlayed.ToString(Format.Fr);

    public string GamesHint
    {
        get
        {
            if (_report == null) return "";
            var parts = new List<string>();
            if (R.FirstLaunches > 0) parts.Add(R.FirstLaunches == 1 ? "1 découvert" : $"{R.FirstLaunches} découverts");
            if (R.NewGames > 0) parts.Add(R.NewGames == 1 ? "1 ajouté" : $"{R.NewGames} ajoutés");
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Day view: the longest session; other views: the average per day.</summary>
    public string FourthTitle => Kind == StatsPeriod.Day ? "PLUS LONGUE SESSION" : "MOYENNE PAR JOUR";

    public string FourthText => _report == null ? "—"
        : Kind == StatsPeriod.Day ? (R.LongestSession > 0 ? Format.Duration(R.LongestSession) : "—")
        : R.Seconds == 0 ? "0 min" : Format.Duration(R.Seconds / R.DaysElapsed);

    public string FourthHint => _report == null ? ""
        : Kind == StatsPeriod.Day ? R.LongestSessionGame?.Name ?? ""
        : R.DaysPlayed == 0 ? "" : $"{R.DaysPlayed} jour{(R.DaysPlayed > 1 ? "s" : "")} de jeu sur {R.DaysElapsed}";

    public string? BestDayText => _report?.BestDay is DateTime d && Kind is not StatsPeriod.Day
        ? $"Meilleur jour : {d.ToString(d.Year == DateTime.Today.Year ? "dddd d MMMM" : "d MMMM yyyy", Format.Fr)} ({Format.Duration(R.BestDaySeconds)})"
        : null;

    // ---- chart ----
    public string ChartTitle => Kind switch
    {
        StatsPeriod.Day => "Heure par heure",
        StatsPeriod.Week => "Jour par jour",
        StatsPeriod.Month => "Jour par jour",
        StatsPeriod.Year => "Mois par mois",
        _ => "Année par année",
    };

    public string ChartHint => Kind == StatsPeriod.Day ? "" : "Cliquez sur une barre pour voir le détail.";

    public List<PeriodBar> Bars
    {
        get
        {
            if (_report == null) return new();
            var max = Math.Max(1, R.Buckets.Max(b => b.Seconds));
            var now = DateTime.Now;
            return R.Buckets.Select(b => new PeriodBar
            {
                Label = b.Label,
                Value = b.Seconds >= 60 && R.Buckets.Count <= 12 ? Format.DurationShort(b.Seconds) : "",
                Ratio = b.Seconds == 0 ? 0.015 : Math.Max(0.04, b.Seconds / (double)max),
                Tooltip = b.Seconds == 0 ? $"{b.LongLabel} : pas de jeu"
                    : $"{b.LongLabel} : {Format.Duration(b.Seconds)}{(b.Games.Count > 0 ? "\n" + string.Join(", ", b.Games.Take(4).Select(g => g.Name)) + (b.Games.Count > 4 ? "…" : "") : "")}",
                Start = b.Start,
                IsCurrent = now >= b.Start && now < b.End,
                CanOpen = Kind != StatsPeriod.Day && b.Start <= now,
            }).ToList();
        }
    }

    // ---- games of the period ----
    public List<BarItem> Games
    {
        get
        {
            if (_report == null || R.Games.Count == 0) return new();
            var max = Math.Max(1, R.Games[0].Seconds);
            return R.Games.Take(10).Select(x => new BarItem
            {
                Game = x.Game,
                Label = x.Game.Name,
                Value = Format.Duration(x.Seconds),
                Tooltip = $"{x.Sessions} session{(x.Sessions > 1 ? "s" : "")}  ·  {Math.Round(x.Seconds * 100.0 / Math.Max(1, R.Seconds))} % du temps de jeu",
                Ratio = x.Seconds / (double)max,
            }).ToList();
        }
    }

    public bool HasMoreGames => _report != null && R.Games.Count > 10;
    public string MoreGamesText => _report == null ? "" : $"et {R.Games.Count - 10} autre(s) jeu(x)";

    // ---- when the player plays ----
    public bool ShowHabits => Kind != StatsPeriod.Day && HasData;

    public List<PeriodBar> Weekdays
    {
        get
        {
            if (_report == null) return new();
            var names = new[] { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" };
            var full = new[] { "Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi", "Dimanche" };
            var max = Math.Max(1, R.ByWeekday.Max());
            return R.ByWeekday.Select((s, i) => new PeriodBar
            {
                Label = names[i],
                Ratio = s == 0 ? 0.015 : Math.Max(0.04, s / (double)max),
                Tooltip = $"{full[i]} : {(s > 0 ? Format.Duration(s) : "pas de jeu")}",
            }).ToList();
        }
    }

    public List<PeriodBar> Hours
    {
        get
        {
            if (_report == null) return new();
            var max = Math.Max(1, R.ByHour.Max());
            return R.ByHour.Select((s, h) => new PeriodBar
            {
                Label = h % 6 == 0 ? $"{h} h" : "",
                Ratio = s == 0 ? 0.015 : Math.Max(0.04, s / (double)max),
                Tooltip = $"De {h} h à {h + 1} h : {(s > 0 ? Format.Duration(s) : "pas de jeu")}",
            }).ToList();
        }
    }

    public string? FavoriteTimeText
    {
        get
        {
            if (!HasData) return null;
            var h = Array.IndexOf(R.ByHour, R.ByHour.Max());
            var d = Array.IndexOf(R.ByWeekday, R.ByWeekday.Max());
            var days = new[] { "le lundi", "le mardi", "le mercredi", "le jeudi", "le vendredi", "le samedi", "le dimanche" };
            var moment = h < 6 ? "la nuit" : h < 12 ? "le matin" : h < 14 ? "le midi" : h < 18 ? "l'après-midi" : "le soir";
            return $"Tu joues surtout {days[d]}, {moment} (vers {h} h).";
        }
    }
}
