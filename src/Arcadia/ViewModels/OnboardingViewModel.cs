using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Arcadia.ViewModels;

/// <summary>Installed games found in one launcher (assistant, step "Tes jeux").</summary>
public sealed record LauncherCount(string Label, string Glyph, int Count);

/// <summary>
/// First-launch assistant: welcome, games found in the launchers, interface theme, colour and wallpaper.
/// Shown once (also available from the settings); every step can be skipped.
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    public const int StepCount = 5;
    private readonly MainViewModel _main;
    private List<DetectedGame> _found = new();

    public OnboardingViewModel(MainViewModel main)
    {
        _main = main;
        _keyAtStart = main.Settings.SteamGridDbApiKey;
        Themes = ConsoleTheme.All.Select(t => new ThemeChoice { Theme = t, IsCurrent = t.Key == main.CurrentTheme.Key }).ToList();
        _ = ScanAsync(); // ready by the time the player reaches step 2
    }

    public MainViewModel Main => _main;

    // ---- steps ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsGames), nameof(IsCovers), nameof(IsTheme), nameof(IsLook), nameof(IsLast), nameof(StepText), nameof(CanGoBack))]
    private int _step;

    public bool IsWelcome => Step == 0;
    public bool IsGames => Step == 1;
    public bool IsCovers => Step == 2;
    public bool IsTheme => Step == 3;
    public bool IsLook => Step == 4;
    public bool IsLast => Step == StepCount - 1;
    public bool CanGoBack => Step > 0;
    public string StepText => $"Étape {Step + 1} sur {StepCount}";
    public IReadOnlyList<int> Dots { get; } = Enumerable.Range(0, StepCount).ToList();

    [RelayCommand]
    private void Next()
    {
        if (IsLast) { Finish(); return; }
        Step++;
        _main.Sound.Play(Sfx.Navigate);
    }

    [RelayCommand]
    private void Back()
    {
        if (Step == 0) return;
        Step--;
        _main.Sound.Play(Sfx.Back);
    }

    [RelayCommand]
    private void Skip()
    {
        _main.Settings.OnboardingDone = true;
        _main.SaveSettings();
        _main.CloseDialog(this);
    }

    private void Finish()
    {
        _main.Settings.OnboardingDone = true;
        _main.SaveSettings();
        _main.CloseDialog(this);
        var theme = Themes.FirstOrDefault(t => t.IsCurrent)?.Theme;
        if (theme != null && theme.Key != _main.CurrentTheme.Key) _main.ApplyUiTheme(theme.Key);
        else _main.Sound.Play(Sfx.Open);
        _main.ShowToast(_main.AllCount > 0 ? "C'est parti ! Bon jeu 🎮" : "C'est parti ! Glissez vos jeux dans la fenêtre pour les ajouter.", ToastKind.Success);
        // a key added here: the games already added without an official cover get another chance
        if (HasGridKey && string.IsNullOrWhiteSpace(_keyAtStart) && _main.MissingArtCount > 0) _main.RetryMissingArtCommand.Execute(null);
    }

    // ---- step 2: games found in the launchers ----
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private int _newCount;
    [ObservableProperty] private int _importedCount;
    [ObservableProperty] private List<LauncherCount> _launchers = new();

    public bool HasNewGames => NewCount > 0 && ImportedCount == 0;
    public bool NothingFound => !IsScanning && NewCount == 0 && ImportedCount == 0;
    public bool HasImported => ImportedCount > 0;

    partial void OnNewCountChanged(int value) => OnScanStateChanged();
    partial void OnImportedCountChanged(int value) => OnScanStateChanged();
    partial void OnIsScanningChanged(bool value) => OnScanStateChanged();

    private void OnScanStateChanged()
    {
        OnPropertyChanged(nameof(HasNewGames));
        OnPropertyChanged(nameof(NothingFound));
        OnPropertyChanged(nameof(HasImported));
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            var all = await Task.Run(StoreScanner.ScanAll);
            _found = all.Where(d => !_main.Importer.IsInLibrary(d) && !_main.Settings.IgnoredDetections.Contains(d.Key)).ToList();
            Launchers = _found.GroupBy(d => d.Store).OrderByDescending(g => g.Count())
                .Select(g => new LauncherCount(StoreInfo.Label(g.Key), StoreInfo.Glyph(g.Key), g.Count())).ToList();
            NewCount = _found.Count;
        }
        catch (Exception ex) { Log.Warn($"Assistant scan failed: {ex.Message}"); }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private async Task ImportFound()
    {
        if (_found.Count == 0 || IsImporting) return;
        IsImporting = true;
        try
        {
            var added = await _main.Importer.ImportDetectedAsync(_found);
            ImportedCount = added.Count;
            _main.Sound.Play(Sfx.Notify);
        }
        catch (Exception ex) { _main.ShowToast("L'ajout des jeux a échoué : " + ex.Message, ToastKind.Error); }
        finally { IsImporting = false; }
    }

    // ---- step 3: covers (SteamGridDB key) ----
    private readonly string? _keyAtStart;
    public bool HasGridKey => !string.IsNullOrWhiteSpace(_main.Settings.SteamGridDbApiKey);
    public string GridKey
    {
        get => _main.Settings.SteamGridDbApiKey ?? "";
        set
        {
            _main.Settings.SteamGridDbApiKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _main.SaveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGridKey));
        }
    }

    [RelayCommand]
    private void OpenGridKeyPage() => _main.OpenUrlCommand.Execute("https://www.steamgriddb.com/profile/preferences/api");

    // ---- step 4: interface ----
    public IReadOnlyList<ThemeChoice> Themes { get; }

    [RelayCommand]
    private void PickTheme(ThemeChoice? choice)
    {
        if (choice == null) return;
        foreach (var t in Themes) t.IsCurrent = t == choice;
        _main.Sound.Play(Sfx.Tick);
    }
}
