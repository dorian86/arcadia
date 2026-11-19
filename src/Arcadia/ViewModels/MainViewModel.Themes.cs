using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>Interface themes: the default interface or a full-screen console-style layout.</summary>
public partial class MainViewModel
{
    private bool _detailFromShell;

    public ConsoleShellViewModel Shell { get; }

    public ConsoleTheme CurrentTheme => ConsoleTheme.Get(Settings.UiTheme);
    public bool IsShellTheme => !CurrentTheme.IsDefault;

    /// <summary>True while the classic interface is shown on top of a console theme (settings, game page…).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowShell), nameof(ShowReturnToShell))]
    private bool _shellSuspended;

    public bool ShowShell => IsShellTheme && !ShellSuspended;
    public bool ShowReturnToShell => IsShellTheme && ShellSuspended;
    public string ReturnToShellText => $"Retour à l'interface « {CurrentTheme.Name} »";

    /// <summary>Raised when the theme changes (the window swaps the console view).</summary>
    public event Action? UiThemeChanged;

    private void InitializeThemes()
    {
        Sound.Flavor = CurrentTheme.Key;
        if (IsShellTheme) Shell.Rebuild();
    }

    [RelayCommand]
    private void OpenThemes()
    {
        if (Dialogs.OfType<ThemePickerViewModel>().Any()) return;
        Dialogs.Add(new ThemePickerViewModel(this));
    }

    public void ApplyUiTheme(string key)
    {
        var theme = ConsoleTheme.Get(key);
        var changed = theme.Key != Settings.UiTheme;
        Settings.UiTheme = theme.Key;
        SaveSettings();
        Sound.Flavor = theme.Key;
        DetailGame = null;
        _detailFromShell = false;
        if (!theme.IsDefault) Shell.Rebuild();
        ShellSuspended = false;
        OnPropertyChanged(nameof(CurrentTheme));
        OnPropertyChanged(nameof(IsShellTheme));
        OnPropertyChanged(nameof(ShowShell));
        OnPropertyChanged(nameof(ShowReturnToShell));
        OnPropertyChanged(nameof(ReturnToShellText));
        if (changed) UiThemeChanged?.Invoke();
        Sound.Play(Sfx.Open);
        if (!theme.IsDefault)
        {
            Trophies.MarkThemeTried();
            ScheduleTrophyCheck();
        }
    }

    /// <summary>Opens a page of the classic interface from a console theme (game page, settings…).</summary>
    public void ShowClassic(Section? section, Game? detail)
    {
        if (detail != null)
        {
            OpenDetailCommand.Execute(detail); // suspends the theme until the page is closed
            return;
        }
        if (section is { } s) Section = s;
        ShellSuspended = true;
        Sound.Play(Sfx.Navigate);
    }

    [RelayCommand]
    private void ReturnToShell()
    {
        if (!IsShellTheme) return;
        _detailFromShell = false;
        DetailGame = null;
        ShellSuspended = false;
        Sound.Play(Sfx.Back);
    }

    /// <summary>Closing a game page opened from a console theme goes straight back to the theme.</summary>
    private void OnDetailClosedForShell(Game? value)
    {
        // a game page opened from a theme (context menu, "Fiche") shows over it, and closing it returns to the theme
        if (value != null && ShowShell)
        {
            _detailFromShell = true;
            ShellSuspended = true;
            return;
        }
        if (value == null && _detailFromShell)
        {
            _detailFromShell = false;
            ShellSuspended = false;
        }
    }
}

public sealed partial class ThemeChoice : ObservableObject
{
    public required ConsoleTheme Theme { get; init; }
    [ObservableProperty] private bool _isCurrent;
}

/// <summary>The "Thèmes" window: one preview card per interface.</summary>
public partial class ThemePickerViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ThemePickerViewModel(MainViewModel main)
    {
        _main = main;
        Choices = ConsoleTheme.All.Select(t => new ThemeChoice { Theme = t, IsCurrent = t.Key == main.CurrentTheme.Key }).ToList();
    }

    public IReadOnlyList<ThemeChoice> Choices { get; }

    [RelayCommand]
    private void Pick(ThemeChoice? choice)
    {
        if (choice == null) return;
        _main.CloseDialog(this);
        _main.ApplyUiTheme(choice.Theme.Key);
    }

    [RelayCommand]
    private void Close() => _main.CloseDialog(this);
}
