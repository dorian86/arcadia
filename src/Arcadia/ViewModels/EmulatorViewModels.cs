using System.Collections.ObjectModel;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Arcadia.ViewModels;

// ========================================================================================
// Emulators & ROM folders
// ========================================================================================

public partial class EmulatorRow : ObservableObject
{
    public EmulatorRow(EmulatorProfile p)
    {
        Id = p.Id;
        _name = p.Name;
        _console = p.Console;
        _exePath = p.ExePath;
        _arguments = p.Arguments;
        _romFolder = p.RomFolder ?? "";
        _extensions = p.Extensions;
        UpdateCount();
    }

    public string Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _console;
    [ObservableProperty] private string _exePath;
    [ObservableProperty] private string _arguments;
    [ObservableProperty] private string _romFolder;
    [ObservableProperty] private string _extensions;
    [ObservableProperty] private string _romCountText = "";

    partial void OnRomFolderChanged(string value) => UpdateCount();
    partial void OnExtensionsChanged(string value) => UpdateCount();

    public bool ExeExists => File.Exists(ExePath);
    partial void OnExePathChanged(string value) => OnPropertyChanged(nameof(ExeExists));

    private async void UpdateCount()
    {
        var profile = ToProfile();
        if (string.IsNullOrWhiteSpace(profile.RomFolder)) { RomCountText = "Aucun dossier de jeux"; return; }
        var n = await Task.Run(() => EmulatorService.FindRoms(profile).Count());
        RomCountText = n == 0 ? "Aucun jeu trouvé dans ce dossier" : $"{n} jeu(x) trouvé(s)";
    }

    public EmulatorProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Console = Console.Trim(),
        ExePath = ExePath.Trim(),
        Arguments = string.IsNullOrWhiteSpace(Arguments) ? "\"{rom}\"" : Arguments.Trim(),
        RomFolder = string.IsNullOrWhiteSpace(RomFolder) ? null : RomFolder.Trim(),
        Extensions = Extensions.Trim(),
    };
}

public partial class EmulatorsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public EmulatorsViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var p in main.Settings.Emulators) Rows.Add(new EmulatorRow(p));
        foreach (var p in EmulatorService.DetectFromLibrary(main.Library.Games, main.Settings.Emulators)) Rows.Add(new EmulatorRow(p));
    }

    public ObservableCollection<EmulatorRow> Rows { get; } = new();
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _status;

    [RelayCommand]
    private void AddEmulator()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir l'émulateur (Ryujinx.exe, Cemu.exe, Dolphin.exe…)",
            Filter = "Programmes (*.exe)|*.exe",
        };
        if (dlg.ShowDialog() != true) return;
        if (Rows.Any(r => string.Equals(r.ExePath, dlg.FileName, StringComparison.OrdinalIgnoreCase))) return;
        var row = new EmulatorRow(EmulatorService.CreateProfile(dlg.FileName));
        Rows.Add(row);
        if (EmulatorService.Match(dlg.FileName) == null)
            Status = "Émulateur non reconnu : vérifiez les arguments ({rom} = le fichier du jeu) et les extensions.";
        BrowseRomFolder(row);
    }

    [RelayCommand]
    private void BrowseRomFolder(EmulatorRow? row)
    {
        if (row == null) return;
        var dlg = new OpenFolderDialog { Title = $"Dossier des jeux pour {row.Name}" };
        if (Directory.Exists(row.RomFolder)) dlg.InitialDirectory = row.RomFolder;
        if (dlg.ShowDialog() == true) row.RomFolder = dlg.FolderName;
    }

    [RelayCommand]
    private void BrowseExe(EmulatorRow? row)
    {
        if (row == null) return;
        var dlg = new OpenFileDialog { Title = "Emplacement de l'émulateur", Filter = "Programmes (*.exe)|*.exe" };
        if (dlg.ShowDialog() == true) row.ExePath = dlg.FileName;
    }

    [RelayCommand]
    private void Remove(EmulatorRow? row)
    {
        if (row != null) Rows.Remove(row);
    }

    private void Save()
    {
        _main.Settings.Emulators = Rows.Select(r => r.ToProfile()).Where(p => p.ExePath.Length > 0).ToList();
        _main.SaveSettings();
    }

    [RelayCommand]
    private async Task Scan()
    {
        Save();
        IsScanning = true;
        Status = null;
        try
        {
            var n = await _main.ScanEmulatorProfilesAsync(_main.Settings.Emulators);
            Status = n == 0 ? "Aucun nouveau jeu : tous vos jeux émulés sont déjà dans la bibliothèque." : $"{n} jeu(x) ajouté(s) à la bibliothèque.";
            if (n > 0)
            {
                _main.Sound.Play(Sfx.Notify);
                _main.ShowToast($"{n} jeu(x) émulé(s) ajouté(s)", ToastKind.Success);
            }
        }
        catch (Exception ex) { Status = "Analyse impossible : " + ex.Message; }
        finally { IsScanning = false; }
    }

    [RelayCommand]
    private void Close()
    {
        Save();
        _main.CloseDialog(this);
    }
}

// ========================================================================================
// "Je ne sais pas à quoi jouer"
// ========================================================================================

public partial class RouletteViewModel : ObservableObject
{
    public const int WinnerIndex = 30;
    private readonly MainViewModel _main;

    public RouletteViewModel(MainViewModel main)
    {
        _main = main;
        BuildStrip();
    }

    public string[] Filters { get; } = { "Tous les jeux", "Jamais lancés", "À faire", "Favoris" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(F0), nameof(F1), nameof(F2), nameof(F3))] private int _filterIndex;
    public bool F0 => FilterIndex == 0;
    public bool F1 => FilterIndex == 1;
    public bool F2 => FilterIndex == 2;
    public bool F3 => FilterIndex == 3;
    [ObservableProperty] private List<Game> _strip = new();
    [ObservableProperty] private Game? _winner;
    [ObservableProperty] private bool _isSpinning;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string? _status;

    public event Action? SpinRequested;

    partial void OnFilterIndexChanged(int value)
    {
        if (IsSpinning) return;
        HasResult = false;
        Winner = null;
        BuildStrip();
        _main.Sound.Play(Sfx.Tick);
    }

    [RelayCommand]
    private void SelectFilter(object? arg)
    {
        if (int.TryParse(arg?.ToString(), out var i)) FilterIndex = i;
    }

    private List<Game> Pool()
    {
        var games = _main.Library.Games.Where(g => !g.IsHidden && !g.IsMissing).ToList();
        return FilterIndex switch
        {
            1 => games.Where(g => g.LastPlayedAt == null && g.PlayTimeSeconds < 60).ToList(),
            2 => games.Where(g => g.Status == GameStatus.Backlog).ToList(),
            3 => games.Where(g => g.IsFavorite).ToList(),
            _ => games,
        };
    }

    private static double Weight(Game g)
    {
        var w = 1.0;
        if (g.Status == GameStatus.Backlog) w += 2;
        if (g.Status == GameStatus.Playing) w += 1;
        if (g.IsFavorite) w += 1;
        if (g.LastPlayedAt == null) w += 0.5;
        if (g.Status is GameStatus.Finished or GameStatus.Abandoned) w *= 0.3;
        return w;
    }

    private void BuildStrip()
    {
        var pool = Pool();
        if (pool.Count == 0)
        {
            Strip = new List<Game>();
            Status = FilterIndex switch
            {
                1 => "Vous avez déjà lancé tous vos jeux !",
                2 => "Aucun jeu marqué « À faire » : définissez une progression depuis la fiche d'un jeu.",
                3 => "Aucun favori pour l'instant.",
                _ => "Aucun jeu disponible.",
            };
            return;
        }
        Status = null;
        var rng = Random.Shared;
        // weighted draw of the winner
        var total = pool.Sum(Weight);
        var pick = rng.NextDouble() * total;
        var winner = pool[^1];
        foreach (var g in pool)
        {
            pick -= Weight(g);
            if (pick <= 0) { winner = g; break; }
        }
        var strip = new List<Game>();
        for (var i = 0; i < WinnerIndex + 5; i++)
            strip.Add(i == WinnerIndex ? winner : pool[rng.Next(pool.Count)]);
        // avoid the winner showing up right next to itself
        for (var i = WinnerIndex - 2; i <= WinnerIndex + 2; i++)
            if (i != WinnerIndex && strip[i] == winner && pool.Count > 1)
                strip[i] = pool.First(g => g != winner);
        Strip = strip;
    }

    [RelayCommand]
    private void Spin()
    {
        if (IsSpinning || Strip.Count == 0 && Pool().Count == 0) return;
        if (HasResult || Strip.Count == 0) BuildStrip();
        HasResult = false;
        Winner = null;
        IsSpinning = true;
        SpinRequested?.Invoke();
    }

    public void OnSpinCompleted()
    {
        IsSpinning = false;
        Winner = Strip.Count > WinnerIndex ? Strip[WinnerIndex] : null;
        HasResult = Winner != null;
        _main.Sound.Play(Sfx.Notify);
    }

    public void OnTick() => _main.Sound.Play(Sfx.Tick);

    [RelayCommand]
    private void PlayWinner()
    {
        if (Winner == null) return;
        var g = Winner;
        _main.CloseDialog(this);
        _main.Play(g);
    }

    [RelayCommand]
    private void ShowWinner()
    {
        if (Winner == null) return;
        var g = Winner;
        _main.CloseDialog(this);
        _main.OpenDetailCommand.Execute(g);
    }

    [RelayCommand]
    private void Close() => _main.CloseDialog(this);
}
