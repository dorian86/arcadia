using System.Windows.Input;
using System.Windows.Threading;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>A list of games shown by a console-style theme (a tab, a column of the XMB, a page…).</summary>
public sealed class ShellCategory
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Glyph { get; init; } = "";
    public List<Game> Games { get; set; } = new();
    /// <summary>Non-game entries (settings, themes…): used by the PS3 "Réglages" column.</summary>
    public List<ShellAction> Actions { get; init; } = new();
    public bool IsActions => Actions.Count > 0;
    public int Count => IsActions ? Actions.Count : Games.Count;
}

/// <summary>A menu entry that runs an Arcadia command (settings, themes, add games…).</summary>
public sealed class ShellAction
{
    public required string Label { get; init; }
    public required string Glyph { get; init; }
    public string Description { get; init; } = "";
    public required ICommand Command { get; init; }
}

/// <summary>
/// State shared by every console-style theme: categories of games, the selection, the clock and the actions.
/// Each theme only draws this state its own way.
/// </summary>
public partial class ConsoleShellViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(10) };

    public ConsoleShellViewModel(MainViewModel main)
    {
        _main = main;
        _clock.Tick += (_, _) => UpdateClock();
        // ARCADIA_DISPLAY_NAME: name shown by the themes instead of the Windows account (demo screenshots)
        UserName = Environment.GetEnvironmentVariable("ARCADIA_DISPLAY_NAME") is { Length: > 0 } shown ? shown : Environment.UserName;
        UserInitial = string.IsNullOrEmpty(UserName) ? "A" : char.ToUpper(UserName[0]).ToString();
        Actions = new List<ShellAction>
        {
            new() { Label = "Réglages d'Arcadia", Glyph = "", Description = "Apparence, sons, Steam, émulateurs…", Command = OpenSettingsCommand },
            new() { Label = "Thèmes", Glyph = "", Description = "Changer l'interface d'Arcadia", Command = main.OpenThemesCommand },
            new() { Label = "Détecter les jeux installés", Glyph = "", Description = "Steam, Epic, GOG, Ubisoft, Xbox", Command = main.ScanPcCommand },
            new() { Label = "Ajouter des jeux", Glyph = "", Description = "Raccourcis ou exécutables", Command = main.AddGamesCommand },
            new() { Label = "Importer un dossier", Glyph = "", Description = "Tous les jeux d'un dossier", Command = main.ImportFolderCommand },
            new() { Label = "Au hasard", Glyph = "", Description = "Je ne sais pas à quoi jouer", Command = main.OpenRouletteCommand },
            new() { Label = "Mode TV", Glyph = "", Description = "Plein écran pour la manette", Command = main.EnterTvModeCommand },
            new() { Label = "Interface classique", Glyph = "", Description = "Bibliothèque, statistiques, collections", Command = OpenClassicCommand },
        };
    }

    public MainViewModel Main => _main;
    public IReadOnlyList<ShellAction> Actions { get; }

    [ObservableProperty] private List<ShellCategory> _categories = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Category), nameof(Games))] private int _categoryIndex;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Current), nameof(CurrentAction))] private int _index;
    [ObservableProperty] private string _timeText = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _shortDateText = "";
    [ObservableProperty] private string? _batteryText;

    public string UserName { get; }
    public string UserInitial { get; }

    public ShellCategory? Category => Categories.Count > 0 ? Categories[Math.Clamp(CategoryIndex, 0, Categories.Count - 1)] : null;
    public List<Game> Games => Category?.Games ?? new();
    public Game? Current => Category is { IsActions: false } c && c.Games.Count > 0 ? c.Games[Math.Clamp(Index, 0, c.Games.Count - 1)] : null;
    public ShellAction? CurrentAction => Category is { IsActions: true } c ? c.Actions[Math.Clamp(Index, 0, c.Actions.Count - 1)] : null;
    public int AllCount => _main.AllCount;

    /// <summary>Raised whenever the lists changed (views rebuild their tiles).</summary>
    public event Action? Rebuilt;

    // ------------------------------------------------------------------------------------
    // Lists
    // ------------------------------------------------------------------------------------

    /// <summary>Home order of every console: most recently played first, then the newest additions.</summary>
    public static List<Game> HomeOrder(IEnumerable<Game> games) => games
        .OrderByDescending(g => g.LastPlayedAt ?? DateTime.MinValue)
        .ThenByDescending(g => g.AddedAt)
        .ToList();

    /// <summary>Adds a last "Réglages" column of actions (PS3-style bar).</summary>
    public bool WithSettings { get; set; }

    public void Rebuild(bool force = false)
    {
        var all = _main.Library.Games.Where(g => !g.IsHidden).ToList();
        var byName = all.OrderBy(g => g.Name, MainViewModel.NameComparer).ToList();
        var list = new List<ShellCategory>
        {
            new() { Key = "home", Label = "Accueil", Glyph = "", Games = HomeOrder(all) },
            new() { Key = "all", Label = "Tous les jeux", Glyph = "", Games = byName },
        };
        var favorites = byName.Where(g => g.IsFavorite).ToList();
        if (favorites.Count > 0) list.Add(new() { Key = "fav", Label = "Favoris", Glyph = "", Games = favorites });
        var recent = all.Where(g => g.LastPlayedAt != null).OrderByDescending(g => g.LastPlayedAt).ToList();
        if (recent.Count > 0) list.Add(new() { Key = "recent", Label = "Récents", Glyph = "", Games = recent });
        foreach (var c in _main.Library.Collections)
        {
            var games = byName.Where(g => g.Collections.Contains(c, StringComparer.CurrentCultureIgnoreCase)).ToList();
            if (games.Count > 0) list.Add(new() { Key = "col:" + c, Label = c, Glyph = "", Games = games });
        }

        if (WithSettings) list.Add(new() { Key = "settings", Label = "Réglages", Glyph = "", Actions = Actions.ToList() });

        // keep what is on screen when nothing changed (every refresh of the library calls this)
        if (!force && list.Count == Categories.Count && list.Zip(Categories).All(p => p.First.Key == p.Second.Key && p.First.Games.SequenceEqual(p.Second.Games)))
            return;

        var keepKey = Category?.Key;
        var keepGame = Current;
        Categories = list;
        var ci = list.FindIndex(c => c.Key == keepKey);
        CategoryIndex = ci < 0 ? 0 : ci;
        var gi = keepGame == null ? 0 : Games.IndexOf(keepGame);
        Index = Math.Max(0, gi);
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(Games));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(AllCount));
        Rebuilt?.Invoke();
    }

    /// <summary>Categories plus a final "Réglages" column of actions (PS3).</summary>
    public List<ShellCategory> CategoriesWithSettings() =>
        Categories.Append(new ShellCategory { Key = "settings", Label = "Réglages", Glyph = "", Actions = Actions.ToList() }).ToList();

    // ------------------------------------------------------------------------------------
    // Clock
    // ------------------------------------------------------------------------------------

    public void Start()
    {
        UpdateClock();
        _clock.Start();
    }

    public void Stop() => _clock.Stop();

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("HH:mm", Format.Fr);
        DateText = Format.Fr.TextInfo.ToTitleCase(now.ToString("dddd d MMMM", Format.Fr));
        ShortDateText = now.ToString("ddd d/M", Format.Fr);
        try
        {
            var power = System.Windows.Forms.SystemInformation.PowerStatus;
            BatteryText = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
                ? null : $"{Math.Round(power.BatteryLifePercent * 100)} %";
        }
        catch { BatteryText = null; }
    }

    // ------------------------------------------------------------------------------------
    // Selection
    // ------------------------------------------------------------------------------------

    public bool Move(int delta, bool sound = true)
    {
        var count = Category?.Count ?? 0;
        if (count == 0) return false;
        var next = Math.Clamp(Index + delta, 0, count - 1);
        if (next == Index) return false;
        Index = next;
        if (sound) _main.Sound.Play(Sfx.Tick);
        return true;
    }

    public void MoveTo(int index, bool sound = true)
    {
        var count = Category?.Count ?? 0;
        if (count == 0) return;
        index = Math.Clamp(index, 0, count - 1);
        if (index == Index) return;
        Index = index;
        if (sound) _main.Sound.Play(Sfx.Tick);
    }

    public void Select(Game? game, bool sound = true)
    {
        if (game == null) return;
        var i = Games.IndexOf(game);
        if (i >= 0) MoveTo(i, sound);
    }

    public bool SwitchCategory(int delta, bool wrap = false, int? count = null)
    {
        var n = count ?? Categories.Count;
        if (n < 2) return false;
        var next = wrap ? (CategoryIndex + delta + n) % n : Math.Clamp(CategoryIndex + delta, 0, n - 1);
        if (next == CategoryIndex) return false;
        CategoryIndex = next;
        Index = 0;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentAction));
        _main.Sound.Play(Sfx.Navigate);
        return true;
    }

    /// <summary>A newly loaded theme starts on its home list, first game.</summary>
    public void ResetSelection()
    {
        CategoryIndex = 0;
        Index = 0;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentAction));
    }

    public void SetCategory(int index)
    {
        if (index == CategoryIndex) return;
        CategoryIndex = index;
        Index = 0;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CurrentAction));
        _main.Sound.Play(Sfx.Navigate);
    }

    // ------------------------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private void Play(object? arg)
    {
        var game = arg as Game ?? Current;
        if (game != null) _main.Play(game);
    }

    /// <summary>A on a game launches it; on an action entry runs the action.</summary>
    public void Activate()
    {
        if (CurrentAction is { } a) { if (a.Command.CanExecute(null)) a.Command.Execute(null); }
        else Play(null);
    }

    [RelayCommand]
    private void ToggleFavorite(object? arg)
    {
        var game = arg as Game ?? Current;
        if (game != null) _main.ToggleFavorite(game);
    }

    [RelayCommand]
    private void OpenDetails(object? arg)
    {
        var game = arg as Game ?? Current;
        if (game != null) _main.ShowClassic(null, game);
    }

    [RelayCommand]
    private void Edit(object? arg)
    {
        var game = arg as Game ?? Current;
        if (game != null) _main.EditCommand.Execute(game);
    }

    [RelayCommand]
    private void OpenSettings() => _main.ShowClassic(Section.Settings, null);

    [RelayCommand]
    private void OpenClassic() => _main.ShowClassic(Section.Home, null);

    [RelayCommand]
    private void Random()
    {
        if (Games.Count < 2) { _main.OpenRouletteCommand.Execute(null); return; }
        int next;
        do next = System.Random.Shared.Next(Games.Count); while (next == Index);
        Index = next;
        _main.Sound.Play(Sfx.Navigate);
    }
}
