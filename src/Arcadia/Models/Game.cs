using System.Text.Json.Serialization;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Arcadia.Models;

public enum GameSourceKind { Executable, Shortcut, InternetShortcut }

public enum MetadataState { Pending, Matched, NotFound, Manual }

public enum SortMode { NameAsc, NameDesc, AddedDesc, AddedAsc, LastPlayed, PlayTime, Rating }

/// <summary>Where a game comes from. Unknown = not detected yet (older libraries).</summary>
public enum GameStore { Unknown, Steam, Epic, GOG, Ubisoft, Xbox, EA, Emulator, Local, Other, BattleNet, Riot }

public sealed class PlaySession
{
    public DateTime Start { get; set; }
    public long Seconds { get; set; }
}

public sealed class Screenshot
{
    public string Thumb { get; set; } = "";
    public string Full { get; set; } = "";
}

public sealed class Trailer
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Thumb { get; set; } = "";
    public string Dash { get; set; } = "";
}

/// <summary>Personal progress on a game.</summary>
public enum GameStatus { None, Backlog, Playing, Finished, Abandoned }

/// <summary>What happens to the Arcadia window when this game starts.</summary>
public enum WindowOnLaunch { Default, Minimize, KeepOpen, Close }

public static class StatusInfo
{
    public static string Label(GameStatus s) => s switch
    {
        GameStatus.Backlog => "À faire",
        GameStatus.Playing => "En cours",
        GameStatus.Finished => "Terminé",
        GameStatus.Abandoned => "Abandonné",
        _ => "Aucun",
    };

    public static string Plural(GameStatus s) => s switch
    {
        GameStatus.Backlog => "À faire",
        GameStatus.Playing => "En cours",
        GameStatus.Finished => "Terminés",
        GameStatus.Abandoned => "Abandonnés",
        _ => "",
    };

    public static string Glyph(GameStatus s) => s switch
    {
        GameStatus.Backlog => "",
        GameStatus.Playing => "",
        GameStatus.Finished => "",
        GameStatus.Abandoned => "",
        _ => "",
    };
}

public static class StoreInfo
{
    public static string Label(GameStore s) => s switch
    {
        GameStore.Steam => "Steam",
        GameStore.Epic => "Epic Games",
        GameStore.GOG => "GOG",
        GameStore.Ubisoft => "Ubisoft Connect",
        GameStore.Xbox => "Xbox",
        GameStore.EA => "EA app",
        GameStore.BattleNet => "Battle.net",
        GameStore.Riot => "Riot Games",
        GameStore.Emulator => "Émulation",
        GameStore.Local => "Jeux installés",
        _ => "Autres",
    };

    public static string Short(GameStore s) => s switch
    {
        GameStore.Epic => "Epic",
        GameStore.Ubisoft => "Ubisoft",
        GameStore.Local => "Local",
        _ => Label(s),
    };

    public static string Glyph(GameStore s) => s switch
    {
        GameStore.Steam => "",
        GameStore.Epic => "",
        GameStore.Xbox => "",
        GameStore.Emulator => "",
        GameStore.Local => "",
        _ => "",
    };
}

/// <summary>
/// A game of the library. This object is both the persisted record (library.json)
/// and the bindable model used by the UI. Transient / computed members are [JsonIgnore].
/// </summary>
public partial class Game : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    // ---- Identity -------------------------------------------------------------------
    [ObservableProperty] private string _name = "";

    /// <summary>True when the user renamed the game: automatic metadata never overrides it.</summary>
    [ObservableProperty] private bool _nameLocked;

    /// <summary>Name candidates computed at import time (used to identify the game online).</summary>
    public List<string>? NameHints { get; set; }

    // ---- Launch ---------------------------------------------------------------------
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SourceText))]
    private GameSourceKind _sourceKind;

    /// <summary>Original file the user imported (.exe, .lnk or .url). Never modified.</summary>
    [ObservableProperty] private string _sourcePath = "";

    /// <summary>What is actually started: an executable, a URL (steam://...) or a managed copy of a shortcut.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUrl), nameof(CanOpenLocation), nameof(LaunchDisplay))]
    private string _launchTarget = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LaunchDisplay), nameof(HasArguments))]
    private string? _arguments;

    [ObservableProperty] private string? _workingDirectory;

    /// <summary>Resolved target of a .lnk (informational).</summary>
    [ObservableProperty] private string? _targetPath;

    /// <summary>Copy of the original shortcut kept in the data folder (relative path).</summary>
    [ObservableProperty] private string? _shortcutCopy;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(SourceText))]
    private int? _steamAppId;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StoreLabel), nameof(CanHaveAchievements), nameof(AchievementsSynced), nameof(AchievementsHint), nameof(HasAchievements))]
    private GameStore _store;

    /// <summary>Install folder (used to follow the game's process when a launcher starts it).</summary>
    [ObservableProperty] private string? _installDir;

    /// <summary>Store specific identifier (Epic app name, Ubisoft id, Xbox package family...).</summary>
    [ObservableProperty] private string? _storeId;

    // ---- Organisation ---------------------------------------------------------------
    [ObservableProperty] private List<string> _collections = new();
    [ObservableProperty] private bool _isHidden;

    /// <summary>
    /// In the library of a launcher (Steam, Epic) but not installed on this PC: kept apart in "Non installés",
    /// never shown with the installed games. Playing it asks the launcher to install it.
    /// </summary>
    [ObservableProperty] private bool _isNotInstalled;

    /// <summary>Shown with the other games (not hidden, installed).</summary>
    [JsonIgnore] public bool IsShown => !IsHidden && !IsNotInstalled;
    public List<PlaySession> Sessions { get; set; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasScreenshots))]
    private List<Screenshot>? _screenshots;

    // ---- Achievements (official list from Steam, ticked by the player) ----------------
    /// <summary>null = not looked up yet; empty = looked up, the game has none (or is not on Steam).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAchievements), nameof(AchievementsUnlocked), nameof(AchievementsProgressText), nameof(AchievementsRatio), nameof(AchievementsShortText))]
    private List<Achievement>? _achievements;

    /// <summary>Steam game the list comes from (may be found by name for games identified elsewhere).</summary>
    [ObservableProperty] private int? _achievementsAppId;

    /// <summary>Latest official announcements (loaded when the game page opens, never saved).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNews))]
    [property: JsonIgnore]
    private List<Services.Online.GameNews>? _news;

    [JsonIgnore] public bool HasNews => News is { Count: > 0 };
    /// <summary>Steam game whose announcements are shown.</summary>
    [JsonIgnore] public int? NewsAppId => SteamAppId ?? AchievementsAppId;

    /// <summary>Live Steam figures (players now, price, Metacritic…), loaded when the game page opens, never saved.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSteamStats))]
    [property: JsonIgnore]
    private Services.Online.SteamStats? _steamStats;

    [JsonIgnore] public bool HasSteamStats => SteamStats != null;

    /// <summary>HowLongToBeat search for this game (opened in the browser).</summary>
    [JsonIgnore] public string HowLongToBeatUrl => "https://howlongtobeat.com/?q=" + Uri.EscapeDataString(Name);

    /// <summary>Dominant colour of the cover (#RRGGBB), used for ambient lighting.</summary>
    [ObservableProperty] private string? _accentColor;

    /// <summary>The cover was generated by Arcadia (no artwork found online).</summary>
    [ObservableProperty] private bool _coverGenerated;

    // ---- Tiles of the console themes (built by TileArt from the cover / illustration / logo) ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SquareTilePath))]
    private string? _tileSquare;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(WideTilePath), nameof(WideTileNeedsLogo))]
    private string? _tileWide;

    /// <summary>Source images the tiles were built from: when it no longer matches, the tiles are rebuilt.</summary>
    [ObservableProperty] private string? _tileSignature;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTrailers))] private List<Trailer>? _trailers;

    /// <summary>Console of an emulated game ("Nintendo Switch"...).</summary>
    [ObservableProperty] private string? _console;

    // ---- Progress ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusLabel), nameof(StatusGlyph), nameof(HasStatusBadge))]
    private GameStatus _status;

    [ObservableProperty] private int _rating;
    [ObservableProperty] private string? _notes;

    // ---- Launch options ----
    [ObservableProperty] private bool _runAsAdmin;
    [ObservableProperty] private string? _preLaunchCommand;
    [ObservableProperty] private string? _postExitCommand;
    [ObservableProperty] private WindowOnLaunch _windowOnLaunch;

    /// <summary>Play time last read from Steam.</summary>
    public long SteamPlayTimeSeconds { get; set; }

    /// <summary>
    /// The platform that keeps this game's play time (Steam, Epic Games): Arcadia copies its hours and does not add
    /// its own sessions to them. Null: the play time is measured by Arcadia.
    /// </summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PlayTimeSourceText))]
    private GameStore? _playtimeSource;

    [JsonIgnore]
    public string PlayTimeSourceText => PlaytimeSource switch
    {
        GameStore.Steam => "selon Steam",
        GameStore.Epic => "selon Epic Games",
        _ => "mesuré par Arcadia",
    };

    // ---- Artwork (paths relative to the data folder) --------------------------------
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CoverFullPath), nameof(HasCover), nameof(WideArtPath), nameof(SquareTilePath), nameof(WideTilePath))]
    private string? _cover;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HeroFullPath), nameof(HasHero), nameof(WideArtPath), nameof(ShowWideLogo), nameof(WideTilePath), nameof(WideTileNeedsLogo))]
    private string? _hero;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LogoFullPath), nameof(HasLogo), nameof(ShowWideLogo), nameof(WideTileNeedsLogo))]
    private string? _logo;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IconFullPath))]
    private string? _icon;

    /// <summary>The cover was chosen by the user: automatic refreshes keep it.</summary>
    [ObservableProperty] private bool _customCover;

    // ---- Metadata -------------------------------------------------------------------
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasDescription))]
    private string? _description;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(MetaLine))] private string? _developer;
    [ObservableProperty] private string? _publisher;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MetaLine))] private string? _genres;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(MetaLine), nameof(ReleaseText))] private int? _releaseYear;
    [ObservableProperty] private MetadataState _metadataState;
    [ObservableProperty] private string? _metadataSource;

    // ---- Usage ----------------------------------------------------------------------
    [ObservableProperty] private bool _isFavorite;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AddedText), nameof(IsNew))]
    private DateTime _addedAt = DateTime.Now;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LastPlayedText), nameof(IsNew))]
    private DateTime? _lastPlayedAt;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PlayCountText))]
    private int _playCount;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PlayTimeText), nameof(CardSubtitle))]
    private long _playTimeSeconds;

    // ---- Transient state (never saved) ------------------------------------------------
    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isMissing;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CardSubtitle))]
    [property: JsonIgnore]
    private bool _isRunning;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isFetching;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isSelected;

    /// <summary>Shown under the title when several games share the same name (e.g. "Steam" / "Local").</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CardSubtitle))]
    [property: JsonIgnore]
    private string? _versionLabel;

    // ---- Computed -------------------------------------------------------------------
    [JsonIgnore] public string? CoverFullPath => AppPaths.Resolve(Cover);
    [JsonIgnore] public string? HeroFullPath => AppPaths.Resolve(Hero);
    [JsonIgnore] public string? LogoFullPath => AppPaths.Resolve(Logo);
    [JsonIgnore] public string? IconFullPath => AppPaths.Resolve(Icon);
    [JsonIgnore] public bool HasCover => !string.IsNullOrEmpty(Cover);
    [JsonIgnore] public bool HasHero => !string.IsNullOrEmpty(Hero);
    [JsonIgnore] public bool HasLogo => !string.IsNullOrEmpty(Logo);
    /// <summary>Landscape artwork for console-style tiles: the wide illustration, or the cover when there is none.</summary>
    [JsonIgnore] public string? WideArtPath => HasHero ? HeroFullPath : CoverFullPath;
    /// <summary>Wide illustrations carry no title: the logo is drawn over them.</summary>
    [JsonIgnore] public bool ShowWideLogo => HasHero && HasLogo;
    /// <summary>Square tile of the console themes: the built tile, or the cover until it exists.</summary>
    [JsonIgnore] public string? SquareTilePath => AppPaths.Resolve(TileSquare) ?? CoverFullPath;
    /// <summary>Wide (16:9) tile of the console themes: the built tile, or the illustration / cover until it exists.</summary>
    [JsonIgnore] public string? WideTilePath => AppPaths.Resolve(TileWide) ?? WideArtPath;
    /// <summary>Only the fallback wide art needs the logo drawn over it (the built tile already has it).</summary>
    [JsonIgnore] public bool WideTileNeedsLogo => TileWide == null && ShowWideLogo;
    [JsonIgnore] public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    [JsonIgnore] public bool HasArguments => !string.IsNullOrWhiteSpace(Arguments);
    [JsonIgnore] public bool IsUrl => LaunchHelper.IsUri(LaunchTarget);
    [JsonIgnore] public bool CanOpenLocation => !IsUrl;

    [JsonIgnore]
    public string LaunchDisplay => string.IsNullOrWhiteSpace(Arguments) ? LaunchTarget : $"{LaunchTarget}  {Arguments}";

    [JsonIgnore]
    public string MetaLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Developer)) parts.Add(Developer!);
            if (ReleaseYear is int y) parts.Add(y.ToString());
            if (!string.IsNullOrWhiteSpace(Genres)) parts.Add(Genres!);
            return string.Join("   ·   ", parts);
        }
    }

    [JsonIgnore] public string ReleaseText => ReleaseYear?.ToString() ?? "—";
    [JsonIgnore] public string PlayTimeText => Format.Duration(PlayTimeSeconds);
    [JsonIgnore] public string LastPlayedText => LastPlayedAt is DateTime d ? Format.Relative(d) : "Jamais";
    [JsonIgnore] public string AddedText => AddedAt.ToString("d MMM yyyy", Format.Fr);
    [JsonIgnore] public string PlayCountText => PlayCount == 0 ? "Aucune" : PlayCount.ToString();
    /// <summary>Set by the library: "new" badges are hidden when most of the library was just imported.</summary>
    [JsonIgnore] public static bool NewBadgesEnabled { get; set; } = true;

    [JsonIgnore] public bool IsRecentlyAdded => LastPlayedAt == null && (DateTime.Now - AddedAt).TotalDays < 3;
    [JsonIgnore] public bool IsNew => NewBadgesEnabled && IsRecentlyAdded;

    public void RefreshNewBadge() => OnPropertyChanged(nameof(IsNew));

    [JsonIgnore]
    public string? CardSubtitle
    {
        get
        {
            if (IsRunning) return "En cours";
            var time = PlayTimeSeconds >= 60 ? Format.DurationShort(PlayTimeSeconds) : null;
            if (VersionLabel != null) return time != null ? $"{VersionLabel}  ·  {time}" : VersionLabel;
            return time;
        }
    }

    [JsonIgnore] public string StoreLabel => StoreInfo.Label(Store);
    [JsonIgnore] public bool HasScreenshots => Screenshots is { Count: > 0 };
    [JsonIgnore] public bool HasTrailers => Trailers is { Count: > 0 };

    /// <summary>
    /// Steam games (unlocked achievements read from Steam on this PC) and games launched outside a store
    /// (plain executables, shortcuts: official list from Steam, ticked by the player).
    /// </summary>
    [JsonIgnore] public bool CanHaveAchievements => Store is GameStore.Steam or GameStore.Local or GameStore.Other or GameStore.Unknown;
    /// <summary>Steam itself says what is unlocked: the list is read-only.</summary>
    [JsonIgnore] public bool AchievementsSynced => Store == GameStore.Steam;

    /// <summary>The game writes its achievements in local files that Arcadia follows live (not saved).</summary>
    [JsonIgnore] public bool AchievementsTracked { get => _achievementsTracked; set { if (SetProperty(ref _achievementsTracked, value)) OnPropertyChanged(nameof(AchievementsHint)); } }
    private bool _achievementsTracked;

    [JsonIgnore]
    public string AchievementsHint => AchievementsSynced
        ? "Synchronisé automatiquement avec vos succès Steam."
        : AchievementsTracked
            ? "Synchronisé en direct avec les succès enregistrés par le jeu sur ce PC. Vous pouvez aussi cocher un succès à la main."
            : "Cliquez sur un succès pour le cocher quand vous l'avez obtenu dans le jeu. Liste officielle de Steam.";
    [JsonIgnore] public bool HasAchievements => CanHaveAchievements && Achievements is { Count: > 0 };

    /// <summary>
    /// Display order: Steam games show the latest unlocked first; a list ticked by hand keeps the official
    /// order (cards must not move under the mouse).
    /// </summary>
    [JsonIgnore]
    public List<Achievement> AchievementsOrdered => Achievements is not { Count: > 0 } list ? new()
        : AchievementsSynced
            ? list.Where(a => a.IsUnlocked).OrderByDescending(a => a.UnlockedAt).Concat(list.Where(a => !a.IsUnlocked)).ToList()
            : list;
    [JsonIgnore] public int AchievementsUnlocked => Achievements?.Count(a => a.IsUnlocked) ?? 0;
    [JsonIgnore] public double AchievementsRatio => HasAchievements ? (double)AchievementsUnlocked / Achievements!.Count : 0;

    [JsonIgnore]
    public string AchievementsProgressText => HasAchievements
        ? $"{AchievementsUnlocked} / {Achievements!.Count}  ·  {Math.Round(AchievementsRatio * 100)} %"
        : "";

    /// <summary>"12 / 40 succès" for the console themes, null when the game has no list.</summary>
    [JsonIgnore] public string? AchievementsShortText => HasAchievements ? $"{AchievementsUnlocked} / {Achievements!.Count} succès" : null;

    /// <summary>Called after an achievement was ticked: updates the progress (and saves the library).</summary>
    public void RefreshAchievementProgress()
    {
        OnPropertyChanged(nameof(AchievementsUnlocked));
        OnPropertyChanged(nameof(AchievementsRatio));
        OnPropertyChanged(nameof(AchievementsProgressText));
        OnPropertyChanged(nameof(AchievementsShortText));
    }
    [JsonIgnore] public string StatusLabel => StatusInfo.Label(Status);
    [JsonIgnore] public string StatusGlyph => StatusInfo.Glyph(Status);
    [JsonIgnore] public bool HasStatusBadge => Status is GameStatus.Playing or GameStatus.Finished;
    [JsonIgnore] public string PlatformLabel => Console ?? StoreLabel;

    [JsonIgnore]
    public string SourceText => SourceKind switch
    {
        GameSourceKind.Executable => "Exécutable (.exe)",
        GameSourceKind.Shortcut => "Raccourci Windows (.lnk)",
        _ when LaunchTarget.StartsWith("steam:", StringComparison.OrdinalIgnoreCase) => "Raccourci Steam (.url)",
        _ => "Raccourci Internet (.url)",
    };

    public override string ToString() => Name;
}
