using System.ComponentModel;
using Arcadia.Models;
using Arcadia.Services;
using Arcadia.Services.Online;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>A button of the game page of a console theme (focusable with the controller).</summary>
public sealed partial class SheetAction : ObservableObject
{
    public required string Glyph { get; init; }
    public bool IsPrimary { get; init; }
    public required Action Run { get; init; }
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private void Invoke() => Run();
}

/// <summary>
/// The game page shown inside a console theme (instead of going back to the Arcadia interface): artwork,
/// play / favourite buttons, play time, achievements and latest news, drawn in the style of the theme family.
/// </summary>
public sealed partial class GameSheetViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly ConsoleShellViewModel _shell;
    private int _selected;

    public GameSheetViewModel(MainViewModel main, ConsoleShellViewModel shell, Game game)
    {
        _main = main;
        _shell = shell;
        Game = game;
        Theme = main.CurrentTheme;
        Actions = new List<SheetAction>
        {
            new() { Glyph = "", Label = "Jouer", IsPrimary = true, Run = Play },
            new() { Glyph = "", Run = () => _main.ToggleFavorite(Game) },
            new() { Glyph = "", Label = "Fiche complète", Run = OpenFullPage },
        };
        UpdateFavoriteLabel();
        Actions[0].IsSelected = true;
        game.PropertyChanged += OnGameChanged;
        _ = _main.LoadNewsAsync(game);
        _ = _main.SyncSteamAchievementsAsync(game);
    }

    public Game Game { get; }
    public ConsoleTheme Theme { get; }
    public string Family => Theme.Family;
    public bool IsLight => Theme.IsLight;
    /// <summary>Main text colour of the page (light themes write in dark grey).</summary>
    public string TextColor => IsLight ? "#2D2D2D" : "#FFFFFF";
    public IReadOnlyList<SheetAction> Actions { get; }

    // ---- what is shown ----
    public string PlayTime => Game.PlayTimeSeconds > 0 ? Game.PlayTimeText : "Jamais lancé";
    public string LastPlayed => Game.LastPlayedText;
    public string Meta => string.Join("   ·   ", new[] { Game.PlatformLabel, Game.ReleaseYear?.ToString(), Game.Genres }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public bool HasAchievements => Game.HasAchievements;
    public string AchievementsText => Game.AchievementsShortText ?? "";
    public double AchievementsRatio => Game.AchievementsRatio;
    public string AchievementsPercent => $"{Math.Round(Game.AchievementsRatio * 100)} %";

    /// <summary>The latest unlocked achievements (icons shown in the "activities").</summary>
    public List<Achievement> RecentAchievements => Game.Achievements?.Where(a => a.IsUnlocked)
        .OrderByDescending(a => a.UnlockedAt).Take(6).ToList() ?? new();

    public List<GameNews> News => Game.News?.Take(3).ToList() ?? new();
    public bool HasNews => News.Count > 0;

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Game.IsFavorite):
                UpdateFavoriteLabel();
                break;
            case nameof(Game.News):
                OnPropertyChanged(nameof(News));
                OnPropertyChanged(nameof(HasNews));
                break;
            case nameof(Game.Achievements):
            case nameof(Game.AchievementsUnlocked):
                OnPropertyChanged(nameof(HasAchievements));
                OnPropertyChanged(nameof(AchievementsText));
                OnPropertyChanged(nameof(AchievementsRatio));
                OnPropertyChanged(nameof(AchievementsPercent));
                OnPropertyChanged(nameof(RecentAchievements));
                break;
            case nameof(Game.PlayTimeSeconds):
            case nameof(Game.LastPlayedAt):
                OnPropertyChanged(nameof(PlayTime));
                OnPropertyChanged(nameof(LastPlayed));
                break;
        }
    }

    private void UpdateFavoriteLabel() => Actions[1].Label = Game.IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris";

    // ---- actions ----
    private void Play()
    {
        Close();
        _main.Play(Game);
    }

    private void OpenFullPage()
    {
        Close();
        _main.ShowClassic(null, Game);
    }

    [RelayCommand]
    private void Close() => _shell.CloseSheet();

    [RelayCommand]
    private void OpenNews(GameNews? news) => _main.OpenNewsCommand.Execute(news);

    // ---- mouse ----
    /// <summary>The mouse over a button selects it (like the controller would), so A / Enter and the click agree.</summary>
    public void HoverAction(SheetAction action)
    {
        var i = Actions.ToList().IndexOf(action);
        if (i >= 0) Select(i);
    }

    /// <summary>The on-screen "Ⓐ OK" hint: runs the selected button.</summary>
    [RelayCommand]
    private void ActivateSelected() => Actions[_selected].Run();

    // ---- controller / keyboard ----
    private void Select(int index)
    {
        index = Math.Clamp(index, 0, Actions.Count - 1);
        if (index == _selected) return;
        Actions[_selected].IsSelected = false;
        _selected = index;
        Actions[_selected].IsSelected = true;
        _main.Sound.Play(Sfx.Tick);
    }

    public void HandlePad(PadButton button)
    {
        switch (button)
        {
            case PadButton.Left or PadButton.Up: Select(_selected - 1); break;
            case PadButton.Right or PadButton.Down: Select(_selected + 1); break;
            case PadButton.A: Actions[_selected].Run(); break;
            case PadButton.B: Close(); break;
            case PadButton.Y: _main.ToggleFavorite(Game); break;
            case PadButton.Start: OpenFullPage(); break;
        }
    }

    public void Dispose() => Game.PropertyChanged -= OnGameChanged;
}
