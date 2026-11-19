using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace Arcadia.ViewModels;

public enum Section { Home, All, Favorites, Recent, Missing, Hidden, Collection, Platform, Status, Stats, Settings }

/// <summary>An entry of a dynamic sidebar list (collection or platform).</summary>
public partial class NavEntry : ObservableObject
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Glyph { get; init; } = "";
    public GameStore? Store { get; init; }
    public GameStatus? Status { get; init; }
    [ObservableProperty] private int _count;
    [ObservableProperty] private bool _isActive;
    public string? Badge => Count > 0 ? Count.ToString() : null;
    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Badge));
}

public partial class MainViewModel : ObservableObject
{
    /// <summary>Global access for bindings living outside the visual tree (context menus, templates).</summary>
    public static MainViewModel Instance { get; private set; } = null!;

    public static readonly StringComparer NameComparer = StringComparer.Create(Format.Fr, ignoreCase: true);
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _featuredTimer;
    private readonly SettingsService _settingsService;
    private bool _minimizedByLaunch;
    private Game? _selectionAnchor;

    /// <summary>Picks different "featured" games each session.</summary>
    private static readonly int SessionSeed = Random.Shared.Next(1_000_000);

    public MainViewModel(SettingsService settingsService)
    {
        Instance = this;
        _ui = Dispatcher.CurrentDispatcher;
        _settingsService = settingsService;
        Settings = settingsService.Settings;
        Metadata = new MetadataService(Settings, _ui);
        Importer = new ImportService(Library, Metadata, Settings, _ui);
        Launcher = new GameLauncher(_ui);
        Sound = new SoundService(Settings);
        Tv = new TvModeViewModel(this);
        Shell = new ConsoleShellViewModel(this);

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); Refresh(); };

        _featuredTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        _featuredTimer.Tick += (_, _) => { if (!PauseFeatured && Section == Section.Home && DetailGame == null) RotateFeatured(1, sound: false); };

        Library.Games.CollectionChanged += (_, _) => ScheduleRefresh();
        Library.Collections.CollectionChanged += (_, _) => ScheduleRefresh();
        Library.GameChanged += OnGameChanged;
        Launcher.GameExited += OnGameExited;
        Dialogs.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) Sound.Play(Sfx.Toggle);
        };
        Settings.PropertyChanged += (_, e) =>
        {
            OnWallpaperSettingChanged(e.PropertyName);
            switch (e.PropertyName)
            {
                case nameof(AppSettings.SortMode):
                    OnPropertyChanged(nameof(SortMode));
                    OnPropertyChanged(nameof(SortLabel));
                    Refresh();
                    break;
                case nameof(AppSettings.DynamicColors):
                    UpdateAmbient();
                    break;
                case nameof(AppSettings.StartWithWindows):
                    InstallService.SetStartWithWindows(Settings.StartWithWindows);
                    break;
            }
        };
    }

    // ------------------------------------------------------------------------------------
    // Services & collections
    // ------------------------------------------------------------------------------------

    public LibraryService Library { get; } = new();
    public AppSettings Settings { get; }
    public MetadataService Metadata { get; }
    public ImportService Importer { get; }
    public GameLauncher Launcher { get; }
    public SoundService Sound { get; }
    public TvModeViewModel Tv { get; }
    public ObservableCollection<object> Dialogs { get; } = new();
    public ObservableCollection<ToastMessage> Toasts { get; } = new();
    public ObservableCollection<NavEntry> CollectionItems { get; } = new();
    public ObservableCollection<NavEntry> PlatformItems { get; } = new();
    public ObservableCollection<NavEntry> StatusItems { get; } = new();
    public IReadOnlyList<AccentPreset> AccentPresets => ThemeService.Presets;

    public event Action? MinimizeRequested;
    public event Action? RestoreRequested;
    public event Action<string>? AccentChanged;

    // ------------------------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------------------------

    [ObservableProperty] private Section _section = Section.Home;
    [ObservableProperty] private string? _currentCollection;
    [ObservableProperty] private GameStore? _currentPlatform;
    [ObservableProperty] private GameStatus? _currentStatus;
    [ObservableProperty] private int _missingArtCount;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private IReadOnlyList<Game> _visibleGames = new List<Game>();
    [ObservableProperty] private int _visibleCount;
    [ObservableProperty] private Game? _detailGame;
    [ObservableProperty] private Game? _featuredGame;
    [ObservableProperty] private IReadOnlyList<Game> _featuredGames = new List<Game>();
    [ObservableProperty] private IReadOnlyList<Game> _recentlyPlayed = new List<Game>();
    [ObservableProperty] private IReadOnlyList<Game> _favoriteGames = new List<Game>();
    [ObservableProperty] private IReadOnlyList<Game> _recentlyAdded = new List<Game>();
    [ObservableProperty] private IReadOnlyList<Game> _sidebarRecent = new List<Game>();
    [ObservableProperty] private int _allCount;
    [ObservableProperty] private int _favoritesCount;
    [ObservableProperty] private int _recentCount;
    [ObservableProperty] private int _missingCount;
    [ObservableProperty] private int _hiddenCount;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private bool _isDragOver;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private bool _isFetchingMetadata;
    [ObservableProperty] private bool _isTvMode;
    [ObservableProperty] private Color _ambientColor = ThemeService.Current.A;
    [ObservableProperty] private StatsViewModel? _stats;

    public bool PauseFeatured { get; set; }
    public bool HasSelection => SelectedCount > 0;

    public SortMode SortMode
    {
        get => Settings.SortMode;
        set => Settings.SortMode = value;
    }

    public string SortLabel => SortMode switch
    {
        SortMode.NameAsc => "Nom (A → Z)",
        SortMode.NameDesc => "Nom (Z → A)",
        SortMode.AddedDesc => "Ajoutés récemment",
        SortMode.AddedAsc => "Ajoutés en premier",
        SortMode.LastPlayed => "Joués récemment",
        SortMode.PlayTime => "Temps de jeu",
        SortMode.Rating => "Mieux notés",
        _ => "",
    };

    public bool HasGames => AllCount > 0;

    public bool IsLibrarySection => Section is Section.All or Section.Favorites or Section.Recent or Section.Missing
        or Section.Hidden or Section.Collection or Section.Platform or Section.Status;

    public bool IsCollectionSection => Section == Section.Collection;

    public string PageTitle => Section switch
    {
        Section.Favorites => "Favoris",
        Section.Recent => "Récemment joués",
        Section.Missing => "Jeux introuvables",
        Section.Hidden => "Jeux masqués",
        Section.Collection => CurrentCollection ?? "Collection",
        Section.Platform => CurrentPlatform is { } p ? StoreInfo.Label(p) : "Plateforme",
        Section.Status => CurrentStatus is { } st ? StatusInfo.Plural(st) : "Progression",
        Section.Stats => "Statistiques",
        Section.Settings => "Paramètres",
        Section.Home => "Accueil",
        _ => string.IsNullOrWhiteSpace(SearchText) ? "Tous les jeux" : "Recherche",
    };

    public string PageSubtitle
    {
        get
        {
            var n = VisibleCount;
            var games = n <= 1 ? $"{n} jeu" : $"{n} jeux";
            if (!string.IsNullOrWhiteSpace(SearchText)) return $"{games} pour « {SearchText.Trim()} »";
            return Section switch
            {
                Section.Missing => $"{games} dont le fichier de lancement est introuvable",
                Section.Hidden => $"{games} masqué(s) de la bibliothèque",
                Section.Collection => $"Collection  ·  {games}",
                _ => games,
            };
        }
    }

    public string Greeting
    {
        get
        {
            var h = DateTime.Now.Hour;
            return h < 5 ? "Bonne nuit" : h < 12 ? "Bonjour" : h < 18 ? "Bon après-midi" : "Bonsoir";
        }
    }

    public string LibraryStats
    {
        get
        {
            if (AllCount == 0) return "Votre bibliothèque vous attend.";
            var total = Library.Games.Sum(g => g.PlayTimeSeconds);
            var games = AllCount == 1 ? "1 jeu" : $"{AllCount} jeux";
            return total >= 60 ? $"{games} dans votre bibliothèque  ·  {Format.Duration(total)} de jeu" : $"{games} dans votre bibliothèque";
        }
    }

    public string FeaturedLabel => FeaturedGame?.LastPlayedAt != null ? "REPRENDRE LA PARTIE" : FeaturedGame?.IsFavorite == true ? "FAVORI" : "À DÉCOUVRIR";

    public string EmptyTitle => !HasGames ? "Votre bibliothèque est vide"
        : !string.IsNullOrWhiteSpace(SearchText) ? "Aucun résultat"
        : Section switch
        {
            Section.Favorites => "Aucun favori pour l'instant",
            Section.Recent => "Aucun jeu lancé récemment",
            Section.Missing => "Tout est en ordre",
            Section.Hidden => "Aucun jeu masqué",
            Section.Collection => "Collection vide",
            _ => "Aucun jeu",
        };

    public string EmptyMessage => !HasGames ? "Ajoutez vos jeux en important leurs raccourcis (.lnk, .url) ou leurs exécutables (.exe),\nou laissez Arcadia détecter les jeux installés (Steam, Epic, GOG, Ubisoft, Xbox)."
        : !string.IsNullOrWhiteSpace(SearchText) ? "Essayez avec un autre nom, ou vérifiez l'orthographe."
        : Section switch
        {
            Section.Favorites => "Cliquez sur le cœur d'un jeu pour le retrouver ici.",
            Section.Recent => "Les jeux que vous lancez depuis Arcadia apparaîtront ici.",
            Section.Missing => "Tous vos jeux sont accessibles.",
            Section.Hidden => "Clic droit sur un jeu → « Masquer » pour le ranger ici.",
            Section.Collection => "Clic droit sur un jeu → « Collections… » pour l'ajouter ici.",
            _ => "",
        };

    public string EmptyGlyph => !HasGames ? "" : !string.IsNullOrWhiteSpace(SearchText) ? ""
        : Section switch
        {
            Section.Favorites => "", Section.Recent => "", Section.Missing => "",
            Section.Hidden => "", Section.Collection => "", _ => "",
        };

    public string AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    public string DataFolder => AppPaths.DataDir;
    public bool CanInstall => InstallService.CanInstall && !InstallService.IsRunningInstalled;
    public string InstallStatus => InstallService.IsRunningInstalled
        ? $"Arcadia est installé dans {InstallService.InstallDir}"
        : InstallService.CanInstall
            ? "Copie Arcadia dans vos programmes, crée un raccourci dans le menu Démarrer et l'ajoute à « Applications installées »."
            : "Version de développement : utilisez l'exécutable publié pour installer Arcadia.";

    // ------------------------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------------------------

    public void Initialize()
    {
        Library.Load();
        Refresh();
        Metadata.QueueEmptied += () => IsFetchingMetadata = false;
        var pending = Library.Games.Where(g => g.MetadataState == MetadataState.Pending).ToList();
        if (pending.Count > 0 && Settings.AutoFetchMetadata)
        {
            IsFetchingMetadata = true;
            foreach (var g in pending) Metadata.Enqueue(g);
        }
        _ = CheckPathsAsync(silent: true);
        _ = Importer.RepairIconsAsync();
        _ = Metadata.FillMissingColorsAsync(Library.Games);
        _featuredTimer.Start();
        _ = AutoDetectAsync();
        InitializeFeatures();
        InitializeThemes();
    }

    public void Shutdown()
    {
        Library.SaveNow();
        _settingsService.SaveNow();
    }

    private void OnGameChanged(Game game, string? property)
    {
        OnGameChangedForAchievements(game, property);
        OnGameChangedForTrophies(property);
        switch (property)
        {
            case nameof(Game.Name):
            case nameof(Game.IsFavorite):
            case nameof(Game.LastPlayedAt):
            case nameof(Game.PlayTimeSeconds):
            case nameof(Game.IsMissing):
            case nameof(Game.IsHidden):
            case nameof(Game.Collections):
            case nameof(Game.Store):
            case nameof(Game.AddedAt):
            case nameof(Game.Developer):
            case nameof(Game.Status):
            case nameof(Game.Rating):
            case nameof(Game.Cover):
                ScheduleRefresh();
                if (property == nameof(Game.Name) && game.CoverGenerated) QueueCoverRegeneration(game);
                if (property == nameof(Game.Cover)) QueueTiles(game);
                break;
            case nameof(Game.Icon):
                if (game.CoverGenerated) QueueCoverRegeneration(game);
                break;
            case nameof(Game.Hero):
            case nameof(Game.Logo):
                QueueTiles(game);
                break;
            case nameof(Game.IsSelected):
                SelectedCount = Library.Games.Count(g => g.IsSelected);
                OnPropertyChanged(nameof(HasSelection));
                break;
            case nameof(Game.AccentColor):
            case nameof(Game.HasHero):
                if (game == DetailGame || game == FeaturedGame) UpdateAmbient();
                break;
        }
    }

    private void OnGameExited(Game game)
    {
        _ = SyncSteamAchievementsAfterSessionAsync(game);
        if (_minimizedByLaunch && !Library.Games.Any(g => g.IsRunning))
        {
            _minimizedByLaunch = false;
            RestoreRequested?.Invoke();
        }
    }

    partial void OnSectionChanged(Section value)
    {
        if (value is Section.Home or Section.Settings or Section.Stats && !string.IsNullOrEmpty(SearchText)) SearchText = "";
        DetailGame = null;
        ClearSelection();
        if (value == Section.Stats) Stats = new StatsViewModel(Library.Games.ToList(), BuildTrophyItems());
        OnPropertyChanged(nameof(IsLibrarySection));
        OnPropertyChanged(nameof(IsCollectionSection));
        Refresh();
        UpdateAmbient();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Section is Section.Home or Section.Settings or Section.Stats)
        {
            Section = Section.All;
            return;
        }
        Refresh();
    }

    partial void OnFeaturedGameChanged(Game? value)
    {
        OnPropertyChanged(nameof(FeaturedLabel));
        UpdateAmbient();
    }

    partial void OnDetailGameChanged(Game? value)
    {
        UpdateAmbient();
        if (value != null) _ = Metadata.EnsureScreenshotsAsync(value);
        if (value != null) _ = SyncSteamAchievementsAsync(value);
        if (value != null) _ = LoadNewsAsync(value);
        OnDetailClosedForShell(value);
    }

    private void UpdateAmbient()
    {
        Game? source = DetailGame ?? (Section == Section.Home ? FeaturedGame : null);
        var color = Settings.DynamicColors ? ColorExtractor.Parse(source?.AccentColor) : null;
        AmbientColor = color ?? ThemeService.Current.A;
    }

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public void Refresh()
    {
        var everything = Library.Games.ToList();
        var all = everything.Where(g => !g.IsHidden).ToList();
        AllCount = all.Count;
        FavoritesCount = all.Count(g => g.IsFavorite);
        RecentCount = all.Count(g => g.LastPlayedAt != null);
        MissingCount = everything.Count(g => g.IsMissing);
        HiddenCount = everything.Count - all.Count;
        MissingArtCount = everything.Count(g => !g.HasCover || g.CoverGenerated);

        // "NOUVEAU" badges only make sense for a handful of games, not after importing a whole folder.
        var recentlyAdded = all.Count(g => g.IsRecentlyAdded);
        var badges = recentlyAdded <= Math.Max(6, all.Count / 5);
        if (badges != Game.NewBadgesEnabled)
        {
            Game.NewBadgesEnabled = badges;
            foreach (var g in everything) g.RefreshNewBadge();
        }

        UpdateDuplicateLabels(all);
        UpdateNavEntries(all);

        IEnumerable<Game> query = Section switch
        {
            Section.Favorites => all.Where(g => g.IsFavorite),
            Section.Recent => all.Where(g => g.LastPlayedAt != null),
            Section.Missing => everything.Where(g => g.IsMissing),
            Section.Hidden => everything.Where(g => g.IsHidden),
            Section.Collection => all.Where(g => CurrentCollection != null && g.Collections.Contains(CurrentCollection, StringComparer.CurrentCultureIgnoreCase)),
            Section.Platform => all.Where(g => g.Store == CurrentPlatform),
            Section.Status => all.Where(g => g.Status == CurrentStatus),
            _ => all,
        };

        var search = NameMatcher.SearchKey(SearchText);
        if (search.Length > 0)
        {
            var tokens = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(g =>
            {
                var key = NameMatcher.SearchKey($"{g.Name} {g.Developer} {g.Genres} {g.StoreLabel} {string.Join(' ', g.Collections)}");
                return tokens.All(t => key.Contains(t, StringComparison.Ordinal));
            });
        }

        query = Section == Section.Recent
            ? query.OrderByDescending(g => g.LastPlayedAt)
            : SortMode switch
            {
                SortMode.NameDesc => query.OrderByDescending(g => g.Name, NameComparer),
                SortMode.AddedDesc => query.OrderByDescending(g => g.AddedAt),
                SortMode.AddedAsc => query.OrderBy(g => g.AddedAt),
                SortMode.LastPlayed => query.OrderByDescending(g => g.LastPlayedAt ?? DateTime.MinValue).ThenBy(g => g.Name, NameComparer),
                SortMode.PlayTime => query.OrderByDescending(g => g.PlayTimeSeconds).ThenBy(g => g.Name, NameComparer),
                SortMode.Rating => query.OrderByDescending(g => g.Rating).ThenBy(g => g.Name, NameComparer),
                _ => query.OrderBy(g => g.Name, NameComparer),
            };

        var list = query.ToList();
        if (!list.SequenceEqual(VisibleGames)) VisibleGames = list;
        VisibleCount = list.Count;

        var recent = all.Where(g => g.LastPlayedAt != null).OrderByDescending(g => g.LastPlayedAt).Take(14).ToList();
        var favorites = all.Where(g => g.IsFavorite).OrderBy(g => g.Name, NameComparer).Take(20).ToList();
        var added = all.OrderByDescending(g => g.AddedAt).Take(14).ToList();
        if (!recent.SequenceEqual(RecentlyPlayed)) RecentlyPlayed = recent;
        if (!favorites.SequenceEqual(FavoriteGames)) FavoriteGames = favorites;
        if (!added.SequenceEqual(RecentlyAdded)) RecentlyAdded = added;
        var sidebar = recent.Take(4).ToList();
        if (!sidebar.SequenceEqual(SidebarRecent)) SidebarRecent = sidebar;

        // Featured carousel: recent games, favourites with artwork, then a few discoveries
        var withHero = all.Where(g => g.HasHero).OrderBy(g => (g.Id.GetHashCode() ^ SessionSeed) & 0x7fffffff).ToList();
        var featured = recent.Take(2)
            .Concat(favorites.Where(g => g.HasHero).Take(2))
            .Concat(withHero)
            .Distinct().Take(6).ToList();
        if (featured.Count == 0) featured = added.Take(1).ToList();
        if (!featured.SequenceEqual(FeaturedGames)) FeaturedGames = featured;
        if (FeaturedGame == null || !featured.Contains(FeaturedGame)) FeaturedGame = featured.FirstOrDefault();

        if (Section == Section.Stats) Stats = new StatsViewModel(everything, BuildTrophyItems());

        OnPropertyChanged(nameof(HasGames));
        if (IsShellTheme) Shell?.Rebuild();
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(LibraryStats));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(EmptyGlyph));
        OnPropertyChanged(nameof(FeaturedLabel));
        OnPropertyChanged(nameof(Greeting));
    }

    /// <summary>Several games with the same name: show where each one comes from.</summary>
    private static void UpdateDuplicateLabels(List<Game> games)
    {
        var groups = games.GroupBy(g => NameMatcher.Compact(g.Name)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var g in games)
        {
            var group = groups[NameMatcher.Compact(g.Name)];
            if (group.Count < 2) { g.VersionLabel = null; continue; }
            var storesDiffer = group.Select(x => x.Store).Distinct().Count() == group.Count;
            if (storesDiffer) { g.VersionLabel = StoreInfo.Short(g.Store); continue; }
            var target = g.TargetPath ?? g.LaunchTarget;
            var folder = LaunchHelper.IsUri(target) ? StoreInfo.Short(g.Store) : Path.GetFileName(Path.GetDirectoryName(target));
            g.VersionLabel = string.IsNullOrWhiteSpace(folder) ? StoreInfo.Short(g.Store) : folder;
        }
    }

    private void UpdateNavEntries(List<Game> all)
    {
        // Collections (keep instances so bindings stay stable)
        var names = Library.Collections.ToList();
        for (var i = CollectionItems.Count - 1; i >= 0; i--)
            if (!names.Contains(CollectionItems[i].Key)) CollectionItems.RemoveAt(i);
        for (var i = 0; i < names.Count; i++)
        {
            var entry = CollectionItems.FirstOrDefault(c => c.Key == names[i]);
            if (entry == null)
            {
                entry = new NavEntry { Key = names[i], Label = names[i], Glyph = "" };
                CollectionItems.Insert(Math.Min(i, CollectionItems.Count), entry);
            }
            entry.Count = all.Count(g => g.Collections.Contains(names[i], StringComparer.CurrentCultureIgnoreCase));
            entry.IsActive = Section == Section.Collection && CurrentCollection == names[i];
        }

        // Progress statuses in use
        foreach (var st in new[] { GameStatus.Playing, GameStatus.Backlog, GameStatus.Finished, GameStatus.Abandoned })
        {
            var count = all.Count(g => g.Status == st);
            var entry = StatusItems.FirstOrDefault(s => s.Status == st);
            if (count == 0) { if (entry != null) StatusItems.Remove(entry); continue; }
            if (entry == null)
            {
                entry = new NavEntry { Key = st.ToString(), Label = StatusInfo.Plural(st), Glyph = StatusInfo.Glyph(st), Status = st };
                var index = 0;
                while (index < StatusItems.Count && StatusItems[index].Status < st) index++;
                StatusItems.Insert(index, entry);
            }
            entry.Count = count;
            entry.IsActive = Section == Section.Status && CurrentStatus == st;
        }

        // Platforms with at least one game
        var platforms = all.GroupBy(g => g.Store).Where(g => g.Key != GameStore.Unknown)
                           .OrderByDescending(g => g.Count()).Select(g => (Store: g.Key, Count: g.Count())).ToList();
        for (var i = PlatformItems.Count - 1; i >= 0; i--)
            if (!platforms.Any(p => p.Store == PlatformItems[i].Store)) PlatformItems.RemoveAt(i);
        foreach (var (store, count) in platforms)
        {
            var entry = PlatformItems.FirstOrDefault(p => p.Store == store);
            if (entry == null)
            {
                entry = new NavEntry { Key = store.ToString(), Label = StoreInfo.Label(store), Glyph = StoreInfo.Glyph(store), Store = store };
                PlatformItems.Add(entry);
            }
            entry.Count = count;
            entry.IsActive = Section == Section.Platform && CurrentPlatform == store;
        }
    }

    // ------------------------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private void Navigate(Section section)
    {
        if (section != Section) Sound.Play(Sfx.Navigate);
        DetailGame = null;
        if (Section == section) Refresh();
        Section = section;
    }

    [RelayCommand]
    private void OpenCollection(string? name)
    {
        if (name == null) return;
        Sound.Play(Sfx.Navigate);
        DetailGame = null;
        CurrentCollection = name;
        if (Section == Section.Collection) { ClearSelection(); Refresh(); OnPropertyChanged(nameof(Section)); }
        else Section = Section.Collection;
    }

    [RelayCommand]
    private void OpenPlatform(NavEntry? entry)
    {
        if (entry?.Store is not { } store) return;
        Sound.Play(Sfx.Navigate);
        DetailGame = null;
        CurrentPlatform = store;
        if (Section == Section.Platform) { ClearSelection(); Refresh(); OnPropertyChanged(nameof(Section)); }
        else Section = Section.Platform;
    }

    [RelayCommand]
    private void OpenDetail(object? arg)
    {
        if (arg is not Game game) return;
        if (DetailGame == null) Sound.Play(Sfx.Open);
        DetailGame = game;
    }

    [RelayCommand]
    private void CloseDetail()
    {
        if (DetailGame != null) Sound.Play(Sfx.Back);
        DetailGame = null;
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private void SetSort(SortMode mode) => SortMode = mode;

    [RelayCommand]
    private void StepFeatured(object? delta) => RotateFeatured(int.TryParse(delta?.ToString(), out var d) ? d : 1, sound: true);

    private void RotateFeatured(int delta, bool sound)
    {
        if (FeaturedGames.Count < 2) return;
        var i = FeaturedGame == null ? 0 : FeaturedGames.ToList().IndexOf(FeaturedGame);
        FeaturedGame = FeaturedGames[((i + delta) % FeaturedGames.Count + FeaturedGames.Count) % FeaturedGames.Count];
        if (sound) Sound.Play(Sfx.Tick);
        _featuredTimer.Stop();
        _featuredTimer.Start();
    }

    [RelayCommand]
    private void ShowFeatured(Game? game)
    {
        if (game == null) return;
        FeaturedGame = game;
        Sound.Play(Sfx.Tick);
        _featuredTimer.Stop();
        _featuredTimer.Start();
    }

    // ------------------------------------------------------------------------------------
    // Selection
    // ------------------------------------------------------------------------------------

    /// <summary>Card click: opens the game, or (de)selects it with Ctrl / Shift / when a selection exists.</summary>
    [RelayCommand]
    private void CardClick(object? arg)
    {
        if (arg is not Game game) return;
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Shift) && _selectionAnchor != null && VisibleGames.Contains(_selectionAnchor))
        {
            var list = VisibleGames.ToList();
            var a = list.IndexOf(_selectionAnchor);
            var b = list.IndexOf(game);
            if (b >= 0)
            {
                for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++) list[i].IsSelected = true;
                Sound.Play(Sfx.Tick);
                return;
            }
        }
        if (mods.HasFlag(ModifierKeys.Control) || HasSelection)
        {
            ToggleSelect(game);
            return;
        }
        OpenDetail(game);
    }

    [RelayCommand]
    private void ToggleSelect(object? arg)
    {
        if (arg is not Game game) return;
        game.IsSelected = !game.IsSelected;
        if (game.IsSelected) _selectionAnchor = game;
        Sound.Play(Sfx.Tick);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var g in VisibleGames) g.IsSelected = true;
        _selectionAnchor = VisibleGames.FirstOrDefault();
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var g in Library.Games.Where(g => g.IsSelected)) g.IsSelected = false;
        _selectionAnchor = null;
    }

    private List<Game> Selection => Library.Games.Where(g => g.IsSelected).ToList();

    [RelayCommand]
    private void BulkFavorite()
    {
        var sel = Selection;
        var value = !sel.All(g => g.IsFavorite);
        foreach (var g in sel) g.IsFavorite = value;
        Sound.Play(value ? Sfx.FavoriteOn : Sfx.FavoriteOff);
        ShowToast(value ? $"{sel.Count} jeu(x) ajouté(s) aux favoris" : $"{sel.Count} jeu(x) retiré(s) des favoris", ToastKind.Success);
        ClearSelection();
    }

    [RelayCommand]
    private void BulkHide()
    {
        var sel = Selection;
        var value = !sel.All(g => g.IsHidden);
        foreach (var g in sel) g.IsHidden = value;
        ShowToast(value ? $"{sel.Count} jeu(x) masqué(s)" : $"{sel.Count} jeu(x) affiché(s) à nouveau", ToastKind.Success);
        ClearSelection();
    }

    [RelayCommand]
    private void BulkCollections()
    {
        var sel = Selection;
        if (sel.Count > 0) Dialogs.Add(new CollectionsDialogViewModel(this, sel));
    }

    [RelayCommand]
    private void BulkRemove()
    {
        var sel = Selection;
        if (sel.Count == 0) return;
        Dialogs.Add(new ConfirmDialogViewModel(this)
        {
            Title = $"Retirer {sel.Count} jeu(x) ?",
            Glyph = "",
            Message = "Ces jeux seront retirés d'Arcadia.\n\nLes fichiers des jeux et vos raccourcis d'origine ne seront ni modifiés ni supprimés.",
            ConfirmText = "Retirer",
            IsDanger = true,
            OnConfirm = () =>
            {
                foreach (var g in sel) Library.Remove(g);
                ShowToast($"{sel.Count} jeu(x) retiré(s) de la bibliothèque", ToastKind.Info);
                ClearSelection();
            },
        });
    }

    // ------------------------------------------------------------------------------------
    // Collections
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private void ManageCollections(object? arg)
    {
        if (arg is not Game game) return;
        var targets = game.IsSelected && SelectedCount > 1 ? Selection : new List<Game> { game };
        Dialogs.Add(new CollectionsDialogViewModel(this, targets));
    }

    [RelayCommand]
    private void NewCollection()
    {
        Dialogs.Add(new PromptDialogViewModel(this)
        {
            Title = "Nouvelle collection",
            Message = "Regroupez vos jeux comme vous le souhaitez : VR, Multijoueur, À finir…",
            Placeholder = "Nom de la collection",
            ConfirmText = "Créer",
            Validate = ValidateCollectionName,
            OnConfirm = name =>
            {
                Library.Collections.Add(name);
                OpenCollection(name);
            },
        });
    }

    [RelayCommand]
    private void RenameCollection(string? name)
    {
        if (name == null) return;
        Dialogs.Add(new PromptDialogViewModel(this)
        {
            Title = "Renommer la collection",
            Text = name,
            ConfirmText = "Renommer",
            Validate = n => string.Equals(n, name, StringComparison.CurrentCultureIgnoreCase) ? null : ValidateCollectionName(n),
            OnConfirm = newName =>
            {
                var i = Library.Collections.IndexOf(name);
                if (i >= 0) Library.Collections[i] = newName;
                foreach (var g in Library.Games.Where(g => g.Collections.Contains(name)))
                    g.Collections = g.Collections.Select(c => c == name ? newName : c).ToList();
                if (CurrentCollection == name) CurrentCollection = newName;
                Refresh();
            },
        });
    }

    [RelayCommand]
    private void DeleteCollection(string? name)
    {
        if (name == null) return;
        Dialogs.Add(new ConfirmDialogViewModel(this)
        {
            Title = $"Supprimer « {name} » ?",
            Glyph = "",
            Message = "La collection sera supprimée. Les jeux qu'elle contient restent dans votre bibliothèque.",
            ConfirmText = "Supprimer",
            IsDanger = true,
            OnConfirm = () =>
            {
                Library.Collections.Remove(name);
                foreach (var g in Library.Games.Where(g => g.Collections.Contains(name)))
                    g.Collections = g.Collections.Where(c => c != name).ToList();
                if (Section == Section.Collection && CurrentCollection == name) Section = Section.All;
            },
        });
    }

    public string? ValidateCollectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Le nom ne peut pas être vide.";
        if (name.Length > 40) return "40 caractères maximum.";
        if (Library.Collections.Contains(name.Trim(), StringComparer.CurrentCultureIgnoreCase)) return "Cette collection existe déjà.";
        return null;
    }

    // ------------------------------------------------------------------------------------
    // Game actions
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    public void Play(object? arg)
    {
        if (arg is not Game game) return;
        if (game.IsRunning)
        {
            ShowToast($"« {game.Name} » est déjà en cours d'exécution.", ToastKind.Info);
            return;
        }

        var result = Launcher.Launch(game);
        switch (result.Outcome)
        {
            case LaunchOutcome.Started:
                game.IsMissing = false;
                Sound.Play(Sfx.Launch);
                ShowToast($"Lancement de « {game.Name} »…", ToastKind.Success);
                var behavior = game.WindowOnLaunch;
                if (behavior == WindowOnLaunch.Close)
                {
                    var ct = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    ct.Tick += (_, _) => { ct.Stop(); CloseRequested?.Invoke(); };
                    ct.Start();
                }
                else if (behavior == WindowOnLaunch.Minimize || behavior == WindowOnLaunch.Default && Settings.MinimizeOnLaunch)
                {
                    _minimizedByLaunch = true;
                    // let the launch sound and animation play before hiding the window
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
                    t.Tick += (_, _) => { t.Stop(); MinimizeRequested?.Invoke(); };
                    t.Start();
                }
                break;
            case LaunchOutcome.NotFound:
                game.IsMissing = true;
                Sound.Play(Sfx.Error);
                Dialogs.Add(new ConfirmDialogViewModel(this)
                {
                    Title = "Jeu introuvable",
                    Glyph = "",
                    Message = $"Le fichier de lancement de « {game.Name} » est introuvable :\n\n{game.LaunchTarget}\n\nLe jeu a peut-être été déplacé ou désinstallé, ou il se trouve sur un disque déconnecté.",
                    ConfirmText = "Localiser le jeu…",
                    OnConfirm = () => _ = LocateAsync(game),
                });
                break;
            case LaunchOutcome.Failed:
                Sound.Play(Sfx.Error);
                ShowToast($"Impossible de lancer « {game.Name} » : {result.Error}", ToastKind.Error);
                break;
        }
    }

    [RelayCommand]
    public void ToggleFavorite(object? arg)
    {
        if (arg is not Game game) return;
        game.IsFavorite = !game.IsFavorite;
        Sound.Play(game.IsFavorite ? Sfx.FavoriteOn : Sfx.FavoriteOff);
    }

    [RelayCommand]
    private void ToggleHidden(object? arg)
    {
        if (arg is not Game game) return;
        game.IsHidden = !game.IsHidden;
        if (game.IsHidden && DetailGame == game) DetailGame = null;
        ShowToast(game.IsHidden ? $"« {game.Name} » est masqué (visible dans « Masqués »)" : $"« {game.Name} » est de nouveau visible", ToastKind.Info);
    }

    [RelayCommand]
    private void Edit(object? arg)
    {
        if (arg is Game game) Dialogs.Add(new EditGameViewModel(this, game));
    }

    [RelayCommand]
    private void SearchCover(object? arg)
    {
        if (arg is Game game) Dialogs.Add(new CoverSearchViewModel(this, game, game.Name));
    }

    [RelayCommand]
    private void Remove(object? arg)
    {
        if (arg is not Game game) return;
        Dialogs.Add(new ConfirmDialogViewModel(this)
        {
            Title = "Retirer de la bibliothèque ?",
            Glyph = "",
            Message = $"« {game.Name} » sera retiré d'Arcadia.\n\nLes fichiers du jeu et vos raccourcis d'origine ne seront ni modifiés ni supprimés.",
            ConfirmText = "Retirer",
            IsDanger = true,
            OnConfirm = () =>
            {
                if (DetailGame == game) DetailGame = null;
                Library.Remove(game);
                ShowToast($"« {game.Name} » a été retiré de la bibliothèque", ToastKind.Info);
            },
        });
    }

    [RelayCommand]
    private void OpenLocation(object? arg)
    {
        if (arg is Game game) GameLauncher.OpenLocation(game);
    }

    [RelayCommand]
    private void RefreshMetadata(object? arg)
    {
        if (arg is not Game game) return;
        game.CustomCover = false;
        game.MetadataState = MetadataState.Pending;
        Metadata.Enqueue(game);
        ShowToast($"Recherche des informations de « {game.Name} »…", ToastKind.Info);
    }

    [RelayCommand]
    private void OpenScreenshot(Screenshot? shot)
    {
        if (shot == null || DetailGame?.Screenshots is not { } shots) return;
        Dialogs.Add(new LightboxViewModel(this, shots, Math.Max(0, shots.IndexOf(shot))));
    }

    [RelayCommand]
    private Task Locate(object? arg) => arg is not Game game ? Task.CompletedTask : LocateAsync(game);

    private async Task LocateAsync(Game game)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Localiser « {game.Name} »",
            Filter = "Jeux et raccourcis (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Tous les fichiers (*.*)|*.*",
            DereferenceLinks = false,
        };
        try
        {
            var dir = LaunchHelper.IsUri(game.LaunchTarget) ? null : Path.GetDirectoryName(game.LaunchTarget);
            while (dir != null && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
            if (dir != null) dlg.InitialDirectory = dir;
        }
        catch { /* invalid path */ }

        if (dlg.ShowDialog() != true) return;
        try
        {
            await Importer.RelocateAsync(game, dlg.FileName);
            game.Store = StoreScanner.Detect(game);
            ShowToast($"« {game.Name} » a été relocalisé", ToastKind.Success);
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, ToastKind.Error);
        }
    }

    // ------------------------------------------------------------------------------------
    // Import & store scan
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task AddGames()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Ajouter des jeux",
            Filter = "Jeux et raccourcis (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Tous les fichiers (*.*)|*.*",
            Multiselect = true,
            DereferenceLinks = false,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dlg.ShowDialog() == true) await ImportPathsAsync(dlg.FileNames);
    }

    [RelayCommand]
    private void ImportFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choisir le dossier où chercher des jeux (sous-dossiers compris)",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dlg.ShowDialog() == true) SearchFolders(new[] { dlg.FolderName });
    }

    /// <summary>Opens the folder search: finds the games in the folders and lets the user pick them.</summary>
    public void SearchFolders(IEnumerable<string> folders) => Dialogs.Add(new ScanViewModel(this, folders));

    [RelayCommand]
    private void ScanPc() => Dialogs.Add(new ScanViewModel(this, (List<DetectedGame>?)null));

    private async Task AutoDetectAsync()
    {
        if (!Settings.AutoDetectGames) return;
        await Task.Delay(TimeSpan.FromSeconds(6));
        try
        {
            var found = await Task.Run(StoreScanner.ScanAll);
            var fresh = found.Where(d => !Importer.IsInLibrary(d) && !Settings.IgnoredDetections.Contains(d.Key)).ToList();
            if (fresh.Count == 0) return;
            // the first-launch assistant already offers them (step "Tes jeux")
            if (!Settings.OnboardingDone || Dialogs.OfType<OnboardingViewModel>().Any()) return;
            Sound.Play(Sfx.Notify);
            ShowToast(fresh.Count == 1 ? $"Nouveau jeu installé détecté : « {fresh[0].Name} »" : $"{fresh.Count} jeux installés ne sont pas encore dans Arcadia",
                ToastKind.Info, "Voir", () => Dialogs.Add(new ScanViewModel(this, found)), seconds: 12);
        }
        catch (Exception ex) { Log.Warn($"Auto-detection failed: {ex.Message}"); }
    }

    public async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        // a dropped folder is searched (games only), files are added as they are
        var list = paths.ToList();
        var folders = list.Where(Directory.Exists).ToList();
        if (folders.Count > 0)
        {
            SearchFolders(folders);
            list = list.Except(folders).ToList();
            if (list.Count == 0) return;
        }
        paths = list;
        IsImporting = true;
        try
        {
            var report = await Importer.ImportAsync(paths);
            if (report.Added.Count > 0)
            {
                IsFetchingMetadata = Settings.AutoFetchMetadata;
                Sound.Play(Sfx.Notify);
                ShowToast(report.Added.Count == 1
                    ? $"« {report.Added[0].Name} » a été ajouté à la bibliothèque"
                    : $"{report.Added.Count} jeux ajoutés à la bibliothèque", ToastKind.Success);
                if (Section is Section.Settings or Section.Stats) Section = Section.All;
                if (report.Added.Any(g => g.Store == GameStore.Emulator)) _ = RefreshEmulatorsAsync(delay: false);
            }
            if (report.Duplicates > 0)
                ShowToast(report.Duplicates == 1 ? "1 jeu déjà présent a été ignoré" : $"{report.Duplicates} jeux déjà présents ont été ignorés", ToastKind.Info);
            if (report.Added.Count == 0 && report.Duplicates == 0)
                ShowToast("Aucun jeu compatible trouvé (.exe, .lnk ou .url)", ToastKind.Warning);
            if (report.Errors.Count > 0)
                ShowToast($"{report.Errors.Count} fichier(s) n'ont pas pu être lus", ToastKind.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("Import failed", ex);
            ShowToast("L'importation a échoué : " + ex.Message, ToastKind.Error);
        }
        finally
        {
            IsImporting = false;
        }
    }

    // ------------------------------------------------------------------------------------
    // TV mode
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    public void EnterTvMode()
    {
        if (IsTvMode || AllCount == 0) return;
        Dialogs.Clear();
        DetailGame = null;
        Tv.Open();
        IsTvMode = true;
        Sound.Play(Sfx.Open);
    }

    [RelayCommand]
    public void ExitTvMode()
    {
        if (!IsTvMode) return;
        IsTvMode = false;
        Sound.Play(Sfx.Back);
    }

    // ------------------------------------------------------------------------------------
    // Settings / maintenance
    // ------------------------------------------------------------------------------------

    [RelayCommand]
    private void SelectAccent(string? name)
    {
        if (name == null) return;
        Settings.AccentPreset = name;
        AccentChanged?.Invoke(name);
        UpdateAmbient();
        Sound.Play(Sfx.Toggle);
    }

    [RelayCommand]
    private void TestSound() => Sound.Play(Sfx.Launch);

    [RelayCommand]
    private async Task Install()
    {
        try
        {
            await InstallService.InstallAsync(desktopShortcut: true);
            if (Settings.StartWithWindows) InstallService.SetStartWithWindows(true);
            Dialogs.Add(new ConfirmDialogViewModel(this)
            {
                Title = "Arcadia est installé",
                Glyph = "",
                Message = $"Arcadia a été copié dans :\n{InstallService.InstallDir}\n\nRaccourcis créés dans le menu Démarrer et sur le bureau. Vous pouvez supprimer l'exécutable téléchargé : redémarrez Arcadia depuis le menu Démarrer.",
                ConfirmText = "Redémarrer depuis la version installée",
                CancelText = "Plus tard",
                OnConfirm = () => RestartInstalledRequested?.Invoke(),
            });
        }
        catch (Exception ex)
        {
            ShowToast("Installation impossible : " + ex.Message, ToastKind.Error);
        }
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(InstallStatus));
    }

    public event Action? RestartInstalledRequested;

    [RelayCommand]
    private void RetryMetadata()
    {
        var targets = Library.Games.Where(g => g.MetadataState is MetadataState.NotFound or MetadataState.Pending).ToList();
        if (targets.Count == 0)
        {
            ShowToast("Tous vos jeux ont déjà leurs informations", ToastKind.Info);
            return;
        }
        IsFetchingMetadata = true;
        foreach (var g in targets)
        {
            g.MetadataState = MetadataState.Pending;
            Metadata.Enqueue(g);
        }
        ShowToast($"Recherche des jaquettes pour {targets.Count} jeu(x)…", ToastKind.Info);
    }

    [RelayCommand]
    private Task CheckPaths() => CheckPathsAsync(silent: false);

    private bool _checkingPaths;

    private async Task CheckPathsAsync(bool silent)
    {
        if (_checkingPaths)
        {
            if (!silent) ShowToast("Vérification déjà en cours…", ToastKind.Info);
            return;
        }
        _checkingPaths = true;
        try { await CheckPathsCoreAsync(silent); }
        finally { _checkingPaths = false; }
    }

    private async Task CheckPathsCoreAsync(bool silent)
    {
        var games = Library.Games.ToList();
        var results = await Task.Run(() => games.Select(g => (Game: g, Missing: !GameLauncher.TargetExists(g))).ToList());
        foreach (var (g, missing) in results) g.IsMissing = missing;
        var repaired = 0;
        try { repaired = await Importer.RepairFromShortcutsAsync(games); }
        catch (Exception ex) { Log.Warn($"Shortcut repair failed: {ex.Message}"); }
        if (repaired > 0) results = games.Select(g => (g, g.IsMissing)).ToList();
        if (silent) return;
        var n = results.Count(r => r.Missing);
        ShowToast(n == 0 ? "Tous vos jeux sont accessibles" : n == 1 ? "1 jeu est introuvable" : $"{n} jeux sont introuvables",
                  n == 0 ? ToastKind.Success : ToastKind.Warning);
    }

    public void RecheckPathsQuietly() => _ = CheckPathsAsync(silent: true);

    [RelayCommand]
    private void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ShowToast(ex.Message, ToastKind.Error); }
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser */ }
    }

    // ------------------------------------------------------------------------------------
    // Dialogs & toasts
    // ------------------------------------------------------------------------------------

    public void CloseDialog(object dialog) => Dialogs.Remove(dialog);

    public bool CloseTopDialog()
    {
        if (Dialogs.Count == 0) return false;
        var top = Dialogs[^1];
        if (top is CoverSearchViewModel search) search.CloseCommand.Execute(null);
        else if (top is ScanViewModel scan) scan.CloseCommand.Execute(null);
        else if (top is OnboardingViewModel onboarding) onboarding.SkipCommand.Execute(null);
        else Dialogs.Remove(top);
        return true;
    }

    public void ShowToast(string text, ToastKind kind = ToastKind.Info, string? actionText = null, Action? action = null, double? seconds = null)
    {
        ToastMessage? toast = null;
        toast = new ToastMessage(text, kind, actionText, action == null ? null : () =>
        {
            Toasts.Remove(toast!);
            action();
        });
        Toasts.Add(toast);
        while (Toasts.Count > 4) Toasts.RemoveAt(0);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds ?? (kind == ToastKind.Error ? 7 : 4.2)) };
        timer.Tick += (_, _) => { timer.Stop(); Toasts.Remove(toast); };
        timer.Start();
    }

    public static string? PickImageFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choisir une image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Tous les fichiers (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
