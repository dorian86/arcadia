using System.Collections.ObjectModel;
using System.Globalization;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

// ========================================================================================
// Store scan
// ========================================================================================

public partial class ScanEntry : ObservableObject
{
    public ScanEntry(DetectedGame game, bool inLibrary)
    {
        Game = game;
        InLibrary = inLibrary;
        _isSelected = !inLibrary;
    }

    public DetectedGame Game { get; }
    public bool InLibrary { get; }
    public bool CanSelect => !InLibrary;
    public string Name => Game.Name;
    public string Detail => Game.InstallDir ?? Game.LaunchTarget;
    [ObservableProperty] private bool _isSelected;
}

public sealed class ScanGroup
{
    public string Label { get; init; } = "";
    public string Glyph { get; init; } = "";
    public List<ScanEntry> Entries { get; init; } = new();
    public string CountText => $"{Entries.Count} jeu(x)";
}

public partial class ScanViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _closed;

    public ScanViewModel(MainViewModel main, List<DetectedGame>? preloaded)
    {
        _main = main;
        if (preloaded != null) Fill(preloaded);
        else _ = ScanAsync();
    }

    public ObservableCollection<ScanGroup> Groups { get; } = new();
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private int _newCount;

    private IEnumerable<ScanEntry> AllEntries => Groups.SelectMany(g => g.Entries);

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        Status = null;
        Groups.Clear();
        try
        {
            var found = await Task.Run(StoreScanner.ScanAll);
            if (_closed) return;
            Fill(found);
        }
        catch (Exception ex)
        {
            Status = "La détection a échoué : " + ex.Message;
        }
        finally { IsScanning = false; }
    }

    private void Fill(List<DetectedGame> found)
    {
        Groups.Clear();
        foreach (var group in found.GroupBy(g => g.Store).OrderBy(g => g.Key))
        {
            var entries = group.Select(d => new ScanEntry(d, _main.Importer.IsInLibrary(d)))
                               .OrderBy(e => e.InLibrary).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var e in entries) e.PropertyChanged += (_, _) => UpdateCount();
            Groups.Add(new ScanGroup { Label = StoreInfo.Label(group.Key), Glyph = StoreInfo.Glyph(group.Key), Entries = entries });
        }
        NewCount = AllEntries.Count(e => !e.InLibrary);
        UpdateCount();
        Status = found.Count == 0
            ? "Aucun jeu détecté dans Steam, Epic Games, GOG, Ubisoft Connect ou l'application Xbox."
            : NewCount == 0 ? "Tous les jeux installés sont déjà dans votre bibliothèque." : null;
    }

    private void UpdateCount() => SelectedCount = AllEntries.Count(e => e.IsSelected && !e.InLibrary);

    [RelayCommand]
    private void SelectAll() { foreach (var e in AllEntries.Where(e => !e.InLibrary)) e.IsSelected = true; }

    [RelayCommand]
    private void SelectNone() { foreach (var e in AllEntries) e.IsSelected = false; }

    [RelayCommand]
    private async Task Import()
    {
        var chosen = AllEntries.Where(e => e.IsSelected && !e.InLibrary).Select(e => e.Game).ToList();
        if (chosen.Count == 0) return;
        IsImporting = true;
        try
        {
            // remember the ones the user left unchecked so we don't propose them again
            foreach (var e in AllEntries.Where(e => !e.IsSelected && !e.InLibrary))
                if (!_main.Settings.IgnoredDetections.Contains(e.Game.Key)) _main.Settings.IgnoredDetections.Add(e.Game.Key);
            var added = await _main.Importer.ImportDetectedAsync(chosen);
            _main.IsFetchingMetadata = added.Count > 0 && _main.Settings.AutoFetchMetadata;
            _main.Sound.Play(Sfx.Notify);
            _main.ShowToast(added.Count == 1 ? $"« {added[0].Name} » a été ajouté" : $"{added.Count} jeux ajoutés depuis vos launchers", ToastKind.Success);
            Close();
            if (added.Count > 0 && _main.Section is Section.Settings or Section.Stats) _main.Section = Section.All;
        }
        catch (Exception ex) { Status = "L'importation a échoué : " + ex.Message; }
        finally { IsImporting = false; }
    }

    [RelayCommand]
    private void Close()
    {
        _closed = true;
        _main.CloseDialog(this);
    }
}

// ========================================================================================
// Collections of one or several games
// ========================================================================================

public partial class CollectionChoice : ObservableObject
{
    public string Name { get; init; } = "";
    [ObservableProperty] private bool? _isChecked;
    public bool WasMixed { get; init; }
}

public partial class CollectionsDialogViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly List<Game> _games;

    public CollectionsDialogViewModel(MainViewModel main, List<Game> games)
    {
        _main = main;
        _games = games;
        Title = games.Count == 1 ? $"Collections de « {games[0].Name} »" : $"Collections de {games.Count} jeux";
        foreach (var name in main.Library.Collections)
        {
            var with = games.Count(g => g.Collections.Contains(name, StringComparer.CurrentCultureIgnoreCase));
            Choices.Add(new CollectionChoice
            {
                Name = name,
                IsChecked = with == 0 ? false : with == games.Count ? true : null,
                WasMixed = with > 0 && with < games.Count,
            });
        }
    }

    public string Title { get; }
    public ObservableCollection<CollectionChoice> Choices { get; } = new();
    public bool HasChoices => Choices.Count > 0;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string? _error;

    [RelayCommand]
    private void Add()
    {
        var name = NewName.Trim();
        var err = _main.ValidateCollectionName(name);
        if (err != null) { Error = err; return; }
        _main.Library.Collections.Add(name);
        Choices.Add(new CollectionChoice { Name = name, IsChecked = true });
        NewName = "";
        Error = null;
        OnPropertyChanged(nameof(HasChoices));
    }

    [RelayCommand]
    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(NewName)) Add();
        foreach (var g in _games)
        {
            var set = g.Collections.ToList();
            foreach (var c in Choices)
            {
                if (c.IsChecked == true && !set.Contains(c.Name)) set.Add(c.Name);
                if (c.IsChecked == false) set.Remove(c.Name);
            }
            if (!set.SequenceEqual(g.Collections)) g.Collections = set;
        }
        _main.CloseDialog(this);
        _main.ClearSelection();
        _main.ShowToast("Collections mises à jour", ToastKind.Success);
    }

    [RelayCommand]
    private void Cancel() => _main.CloseDialog(this);
}

// ========================================================================================
// Simple text prompt
// ========================================================================================

public partial class PromptDialogViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public PromptDialogViewModel(MainViewModel main) => _main = main;

    public string Title { get; init; } = "";
    public string? Message { get; init; }
    public string? Placeholder { get; init; }
    public string ConfirmText { get; init; } = "Valider";
    public Func<string, string?>? Validate { get; init; }
    public Action<string>? OnConfirm { get; init; }
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string? _error;

    [RelayCommand]
    private void Confirm()
    {
        var value = Text.Trim();
        var err = Validate?.Invoke(value);
        if (err != null) { Error = err; return; }
        _main.CloseDialog(this);
        OnConfirm?.Invoke(value);
    }

    [RelayCommand]
    private void Cancel() => _main.CloseDialog(this);
}

// ========================================================================================
// Screenshot viewer
// ========================================================================================

public partial class LightboxViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly List<Screenshot> _shots;

    public LightboxViewModel(MainViewModel main, List<Screenshot> shots, int index)
    {
        _main = main;
        _shots = shots;
        _index = index;
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Current), nameof(Position))] private int _index;
    public Screenshot Current => _shots[Index];
    public string Position => $"{Index + 1} / {_shots.Count}";

    [RelayCommand]
    public void Next() { Index = (Index + 1) % _shots.Count; _main.Sound.Play(Sfx.Tick); }

    [RelayCommand]
    public void Previous() { Index = (Index - 1 + _shots.Count) % _shots.Count; _main.Sound.Play(Sfx.Tick); }

    [RelayCommand]
    private void Close() => _main.CloseDialog(this);
}

// ========================================================================================
// Statistics
// ========================================================================================

public sealed class BarItem
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public double Ratio { get; init; }
    public Game? Game { get; init; }
    public string? Tooltip { get; init; }
}

public sealed class StatsViewModel
{
    public StatsViewModel(List<Game> games)
    {
        var visible = games.Where(g => !g.IsHidden).ToList();
        var total = visible.Sum(g => g.PlayTimeSeconds);
        var sessions = visible.SelectMany(g => g.Sessions).ToList();
        TotalTime = total > 0 ? Format.Duration(total) : "0 h";
        GameCount = visible.Count.ToString(CultureInfo.CurrentCulture);
        PlayedCount = $"{visible.Count(g => g.LastPlayedAt != null)} / {visible.Count}";
        SessionCount = Math.Max(sessions.Count, visible.Sum(g => g.PlayCount)).ToString(CultureInfo.CurrentCulture);
        AverageSession = sessions.Count > 0 ? Format.Duration((long)sessions.Average(s => s.Seconds)) : "—";
        var top = visible.Where(g => g.PlayTimeSeconds > 0).OrderByDescending(g => g.PlayTimeSeconds).Take(8).ToList();
        MostPlayed = top.FirstOrDefault();
        var max = Math.Max(1, top.FirstOrDefault()?.PlayTimeSeconds ?? 1);
        TopGames = top.Select(g => new BarItem
        {
            Label = g.Name, Value = Format.Duration(g.PlayTimeSeconds), Ratio = (double)g.PlayTimeSeconds / max, Game = g,
        }).ToList();

        // last 12 weeks (Monday-based)
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weeks = Enumerable.Range(0, 12).Select(i => monday.AddDays(-7 * (11 - i))).ToList();
        var perWeek = weeks.Select(w => sessions.Where(s => s.Start >= w && s.Start < w.AddDays(7)).Sum(s => s.Seconds)).ToList();
        var maxWeek = Math.Max(1, perWeek.Max());
        Weeks = weeks.Select((w, i) => new BarItem
        {
            Label = w.ToString("d MMM", Format.Fr),
            Value = perWeek[i] > 0 ? Format.DurationShort(perWeek[i]) : "",
            Ratio = Math.Max(0.02, perWeek[i] / (double)maxWeek),
            Tooltip = $"Semaine du {w.ToString("d MMMM", Format.Fr)} : {(perWeek[i] > 0 ? Format.Duration(perWeek[i]) : "aucune session")}",
        }).ToList();
        HasActivity = perWeek.Any(s => s > 0);

        var byStore = visible.GroupBy(g => g.Store).OrderByDescending(g => g.Count()).ToList();
        var maxStore = Math.Max(1, byStore.FirstOrDefault()?.Count() ?? 1);
        Platforms = byStore.Select(g => new BarItem
        {
            Label = StoreInfo.Label(g.Key), Value = g.Count().ToString(CultureInfo.CurrentCulture), Ratio = g.Count() / (double)maxStore,
        }).ToList();
    }

    public string TotalTime { get; }
    public string GameCount { get; }
    public string PlayedCount { get; }
    public string SessionCount { get; }
    public string AverageSession { get; }
    public Game? MostPlayed { get; }
    public List<BarItem> TopGames { get; }
    public bool HasTopGames => TopGames.Count > 0;
    public List<BarItem> Weeks { get; }
    public bool HasActivity { get; }
    public List<BarItem> Platforms { get; }
}

// ========================================================================================
// TV mode (controller-friendly full screen)
// ========================================================================================

public sealed class TvFilter
{
    public string Label { get; init; } = "";
    public List<Game> Games { get; init; } = new();
}

public partial class TvModeViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public TvModeViewModel(MainViewModel main) => _main = main;

    [ObservableProperty] private List<TvFilter> _filters = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Games), nameof(FilterLabel))] private int _filterIndex;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Current))] private int _index;

    public List<Game> Games => Filters.Count > 0 ? Filters[Math.Clamp(FilterIndex, 0, Filters.Count - 1)].Games : new();
    public Game? Current => Games.Count > 0 ? Games[Math.Clamp(Index, 0, Games.Count - 1)] : null;
    public string FilterLabel => Filters.Count > 0 ? Filters[FilterIndex].Label : "";
    public string Clock => DateTime.Now.ToString("HH:mm", Format.Fr);

    public void Open()
    {
        var all = _main.Library.Games.Where(g => !g.IsHidden).ToList();
        var cmp = StringComparer.Create(Format.Fr, true);
        var filters = new List<TvFilter>();
        var recent = all.Where(g => g.LastPlayedAt != null).OrderByDescending(g => g.LastPlayedAt).ToList();
        if (recent.Count > 0) filters.Add(new TvFilter { Label = "Récents", Games = recent });
        filters.Add(new TvFilter { Label = "Tous les jeux", Games = all.OrderBy(g => g.Name, cmp).ToList() });
        var favs = all.Where(g => g.IsFavorite).OrderBy(g => g.Name, cmp).ToList();
        if (favs.Count > 0) filters.Add(new TvFilter { Label = "Favoris", Games = favs });
        foreach (var c in _main.Library.Collections)
        {
            var games = all.Where(g => g.Collections.Contains(c)).OrderBy(g => g.Name, cmp).ToList();
            if (games.Count > 0) filters.Add(new TvFilter { Label = c, Games = games });
        }
        Filters = filters;
        FilterIndex = 0;
        Index = 0;
        OnPropertyChanged(nameof(Games));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Clock));
    }

    public void Move(int delta)
    {
        if (Games.Count == 0) return;
        var next = Math.Clamp(Index + delta, 0, Games.Count - 1);
        if (next == Index) return;
        Index = next;
        _main.Sound.Play(Sfx.Tick);
    }

    public void SwitchFilter(int delta)
    {
        if (Filters.Count < 2) return;
        FilterIndex = (FilterIndex + delta + Filters.Count) % Filters.Count;
        Index = 0;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Clock));
        _main.Sound.Play(Sfx.Navigate);
    }

    public void PlayCurrent()
    {
        if (Current != null) _main.Play(Current);
    }

    public void ToggleFavorite()
    {
        if (Current != null) _main.ToggleFavorite(Current);
    }

    public void Random()
    {
        if (Games.Count < 2) return;
        int next;
        do next = System.Random.Shared.Next(Games.Count); while (next == Index);
        Index = next;
        _main.Sound.Play(Sfx.Navigate);
    }
}
