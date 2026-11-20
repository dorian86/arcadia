using CommunityToolkit.Mvvm.ComponentModel;

namespace Arcadia.Models;

public partial class AppSettings : ObservableObject
{
    /// <summary>Optional SteamGridDB API key (free) for better artwork coverage.</summary>
    [ObservableProperty] private string? _steamGridDbApiKey;

    [ObservableProperty] private bool _minimizeOnLaunch = true;

    [ObservableProperty] private bool _autoFetchMetadata = true;

    [ObservableProperty] private double _cardSize = 184;

    [ObservableProperty] private SortMode _sortMode = SortMode.NameAsc;

    // ---- Appearance ----
    [ObservableProperty] private string _accentPreset = "Violet";

    /// <summary>Interface theme: "default" or a console-style layout (see ConsoleTheme).</summary>
    [ObservableProperty] private string _uiTheme = "default";

    // ---- Updates ----
    [ObservableProperty] private bool _autoCheckUpdates = true;
    /// <summary>Measure the play time of library games started outside Arcadia (Steam, Epic, EA app, shortcuts…).</summary>
    [ObservableProperty] private bool _trackOutsidePlaytime = true;
    /// <summary>Also add the games of the Steam / Epic libraries that are not installed (kept apart, with their hours).</summary>
    [ObservableProperty] private bool _importOwnedGames = true;
    /// <summary>Pairs of games the user said are not duplicates ("idA|idB", sorted).</summary>
    public List<string> NotDuplicates { get; set; } = new();

    /// <summary>A version the user chose to skip ("Ignorer cette version").</summary>
    [ObservableProperty] private string? _skippedUpdate;
    /// <summary>Version of Arcadia at the last start: a newer one shows its patch notes once ("Quoi de neuf").</summary>
    [ObservableProperty] private string? _lastRunVersion;

    /// <summary>Tint the background with the colours of the selected game's cover.</summary>
    [ObservableProperty] private bool _dynamicColors = true;

    // ---- Wallpaper of the default interface ----
    /// <summary>Copy of the chosen image in the data folder (relative path), null = no wallpaper.</summary>
    [ObservableProperty] private string? _wallpaper;
    /// <summary>Blurred copy, built once when the blur is turned on.</summary>
    [ObservableProperty] private string? _wallpaperBlurred;
    [ObservableProperty] private bool _wallpaperBlur;
    /// <summary>Darkening laid over the image so that text stays readable (0-90 %).</summary>
    [ObservableProperty] private double _wallpaperDim = 70;

    /// <summary>Last year whose "Ton année de jeu" was announced.</summary>
    [ObservableProperty] private int _recapAnnounced;

    /// <summary>The first-launch assistant was shown (or skipped).</summary>
    [ObservableProperty] private bool _onboardingDone;

    // ---- Sound ----
    [ObservableProperty] private bool _soundEnabled = true;
    [ObservableProperty] private double _soundVolume = 0.5;

    // ---- System ----
    [ObservableProperty] private bool _showTrayIcon = true;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _autoDetectGames = true;

    [ObservableProperty] private bool _autoPlayTrailers = true;
    [ObservableProperty] private bool _importSteamPlaytime = true;

    /// <summary>Configured emulators (ROM library).</summary>
    public List<EmulatorProfile> Emulators { get; set; } = new();

    /// <summary>Installed games the user chose not to import (never proposed again automatically).</summary>
    public List<string> IgnoredDetections { get; set; } = new();

    public WindowPlacement? Window { get; set; }
}

/// <summary>An emulator and the folder containing its games (ROMs).</summary>
public sealed class EmulatorProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Console { get; set; } = "";
    public string ExePath { get; set; } = "";
    /// <summary>Command line, {rom} is replaced by the game file.</summary>
    public string Arguments { get; set; } = "\"{rom}\"";
    public string? RomFolder { get; set; }
    /// <summary>Comma separated extensions (xci,nsp...).</summary>
    public string Extensions { get; set; } = "";
}

public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
}
