using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using Arcadia.Models;

namespace Arcadia.Services;

public sealed class LibraryData
{
    public int Version { get; set; } = 1;
    public List<Game> Games { get; set; } = new();
    public List<string> Collections { get; set; } = new();
}

/// <summary>Owns the game collection and saves it automatically (debounced, atomic writes).</summary>
public sealed class LibraryService
{
    private static readonly HashSet<string> TransientProperties = new()
    {
        nameof(Game.IsMissing), nameof(Game.IsRunning), nameof(Game.IsFetching), nameof(Game.IsSelected), nameof(Game.VersionLabel),
        nameof(Game.News), nameof(Game.HasNews), nameof(Game.SteamStats), nameof(Game.HasSteamStats),
    };

    private readonly DispatcherTimer _saveTimer;
    private bool _loading;

    public ObservableCollection<Game> Games { get; } = new();

    /// <summary>User collections, in display order.</summary>
    public ObservableCollection<string> Collections { get; } = new();

    /// <summary>Raised for every property change of any game (UI thread).</summary>
    public event Action<Game, string?>? GameChanged;

    public LibraryService()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        Games.CollectionChanged += (_, _) => ScheduleSave();
        Collections.CollectionChanged += (_, _) => ScheduleSave();
    }

    public void Load()
    {
        _loading = true;
        try
        {
            var data = JsonFile.Read<LibraryData>(AppPaths.LibraryFile);
            if (data?.Games == null) return;
            foreach (var c in data.Collections.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.CurrentCultureIgnoreCase))
                Collections.Add(c);
            var ids = new HashSet<string>();
            foreach (var g in data.Games)
            {
                if (string.IsNullOrWhiteSpace(g.Id)) g.Id = Guid.NewGuid().ToString("N");
                if (!ids.Add(g.Id)) continue;
                g.Collections ??= new List<string>();
                g.Sessions ??= new List<PlaySession>();
                StoreScanner.CorrectStore(g);
                foreach (var c in g.Collections.Where(c => !Collections.Contains(c, StringComparer.CurrentCultureIgnoreCase)).ToList())
                    Collections.Add(c);
                g.PropertyChanged += OnGamePropertyChanged;
                Games.Add(g);
            }
            Log.Info($"Library loaded: {Games.Count} games");
        }
        finally
        {
            _loading = false;
            _saveTimer.Stop();
        }
    }

    public void Add(Game game)
    {
        game.PropertyChanged += OnGamePropertyChanged;
        Games.Add(game);
    }

    /// <summary>Removes a game from the library. Only Arcadia's own cached files are deleted.</summary>
    public void Remove(Game game)
    {
        game.PropertyChanged -= OnGamePropertyChanged;
        Games.Remove(game);
        AppPaths.TryDeleteManaged(game.Cover);
        AppPaths.TryDeleteManaged(game.Hero);
        AppPaths.TryDeleteManaged(game.Logo);
        AppPaths.TryDeleteManaged(game.Icon);
        AppPaths.TryDeleteManaged(game.TileSquare);
        AppPaths.TryDeleteManaged(game.TileWide);
        AppPaths.TryDeleteManaged(game.ShortcutCopy);
        SaveNow();
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Game game) return;
        if (e.PropertyName == null || !TransientProperties.Contains(e.PropertyName)) ScheduleSave();
        GameChanged?.Invoke(game, e.PropertyName);
    }

    public void ScheduleSave()
    {
        if (_loading) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            JsonFile.Write(AppPaths.LibraryFile, new LibraryData { Games = Games.ToList(), Collections = Collections.ToList() });
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save library", ex);
        }
    }
}

public sealed class SettingsService
{
    private readonly DispatcherTimer _timer;
    public AppSettings Settings { get; }

    public SettingsService()
    {
        Settings = JsonFile.Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        Settings.CardSize = Math.Clamp(Settings.CardSize, 130, 300);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => { _timer.Stop(); SaveNow(); };
        Settings.PropertyChanged += (_, _) => { _timer.Stop(); _timer.Start(); };
    }

    public void SaveNow()
    {
        _timer.Stop();
        try { JsonFile.Write(AppPaths.SettingsFile, Settings); }
        catch (Exception ex) { Log.Error("Failed to save settings", ex); }
    }
}
