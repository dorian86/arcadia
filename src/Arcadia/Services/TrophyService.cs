using Arcadia.Models;

namespace Arcadia.Services;

public enum TrophyTier { Bronze, Argent, Or, Platine }

/// <summary>Everything the trophies are measured on, computed once from the library.</summary>
public sealed class TrophyStats
{
    public int GameCount { get; init; }
    public double TotalHours { get; init; }
    public double MaxGameHours { get; init; }
    public int SessionCount { get; init; }
    public double LongestSessionHours { get; init; }
    public int Finished { get; init; }
    public int Favorites { get; init; }
    public int Rated { get; init; }
    public int Collections { get; init; }
    public int AchievementsUnlocked { get; init; }
    public int PerfectGames { get; init; }
    public int MaxStreakDays { get; init; }
    public int MaxGamesInAWeek { get; init; }
    public bool NightSession { get; init; }
    public bool ThemeTried { get; init; }
    public int OtherTrophies { get; init; }
    public int OtherTrophiesTotal { get; init; }

    public static TrophyStats From(IReadOnlyCollection<Game> games, int collections, bool themeTried, int otherTrophies = 0, int otherTotal = 0)
    {
        var sessions = games.SelectMany(g => g.Sessions.Select(s => (Game: g, Session: s))).ToList();
        var withList = games.Where(g => g.HasAchievements).ToList();
        return new TrophyStats
        {
            GameCount = games.Count,
            TotalHours = games.Sum(g => g.PlayTimeSeconds) / 3600.0,
            MaxGameHours = games.Select(g => g.PlayTimeSeconds).DefaultIfEmpty(0).Max() / 3600.0,
            SessionCount = Math.Max(sessions.Count, games.Sum(g => g.PlayCount)),
            LongestSessionHours = sessions.Select(s => s.Session.Seconds).DefaultIfEmpty(0).Max() / 3600.0,
            Finished = games.Count(g => g.Status == GameStatus.Finished),
            Favorites = games.Count(g => g.IsFavorite),
            Rated = games.Count(g => g.Rating > 0),
            Collections = collections,
            AchievementsUnlocked = withList.Sum(g => g.AchievementsUnlocked),
            PerfectGames = withList.Count(g => g.AchievementsUnlocked == g.Achievements!.Count),
            MaxStreakDays = Streak(sessions.Select(s => s.Session.Start.Date)),
            MaxGamesInAWeek = GamesInAWeek(sessions.Select(s => (s.Session.Start, s.Game.Id))),
            NightSession = sessions.Any(s => s.Session.Start.Hour < 5 && s.Session.Seconds >= 300),
            ThemeTried = themeTried,
            OtherTrophies = otherTrophies,
            OtherTrophiesTotal = otherTotal,
        };
    }

    /// <summary>Longest run of consecutive days with at least one session.</summary>
    public static int Streak(IEnumerable<DateTime> days)
    {
        var sorted = days.Distinct().OrderBy(d => d).ToList();
        int best = 0, run = 0;
        DateTime? prev = null;
        foreach (var d in sorted)
        {
            run = prev != null && (d - prev.Value).TotalDays == 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = d;
        }
        return best;
    }

    /// <summary>Most different games played within any 7 days.</summary>
    public static int GamesInAWeek(IEnumerable<(DateTime Start, string GameId)> sessions)
    {
        var list = sessions.OrderBy(s => s.Start).ToList();
        var best = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var end = list[i].Start.AddDays(7);
            best = Math.Max(best, list.Skip(i).TakeWhile(s => s.Start < end).Select(s => s.GameId).Distinct().Count());
        }
        return best;
    }
}

/// <summary>A trophy of Arcadia: a milestone measured on the whole library (works for every game, even without achievements).</summary>
public sealed record TrophyDef(string Id, string Name, string Description, TrophyTier Tier, string Glyph,
    Func<TrophyStats, (double Value, double Target)> Progress, string Unit = "");

/// <summary>Arcadia's own trophies: definitions, progress and the ones obtained (trophies.json).</summary>
public sealed class TrophyService
{
    public static readonly IReadOnlyList<TrophyDef> All = new List<TrophyDef>
    {
        // ---- Bronze
        new("first-launch", "Premiers pas", "Lancer un jeu depuis Arcadia", TrophyTier.Bronze, "", s => (s.SessionCount, 1)),
        new("library-10", "Petite collection", "Avoir 10 jeux dans la bibliothèque", TrophyTier.Bronze, "", s => (s.GameCount, 10), "jeux"),
        new("hours-10", "Ça commence", "Cumuler 10 heures de jeu", TrophyTier.Bronze, "", s => (s.TotalHours, 10), "h"),
        new("favorite", "Coup de cœur", "Ajouter un jeu aux favoris", TrophyTier.Bronze, "", s => (s.Favorites, 1)),
        new("rated", "Critique", "Donner une note à 5 jeux", TrophyTier.Bronze, "", s => (s.Rated, 5), "jeux"),
        new("collection", "Bien rangé", "Créer une collection", TrophyTier.Bronze, "", s => (s.Collections, 1)),
        new("finished-1", "Générique de fin", "Marquer un jeu comme terminé", TrophyTier.Bronze, "", s => (s.Finished, 1)),
        new("achievement-1", "Premier succès", "Obtenir un succès", TrophyTier.Bronze, "", s => (s.AchievementsUnlocked, 1)),
        new("night", "Oiseau de nuit", "Commencer une partie entre minuit et 5 h", TrophyTier.Bronze, "", s => (s.NightSession ? 1 : 0, 1)),
        new("theme", "Nostalgique", "Essayer un thème de console", TrophyTier.Bronze, "", s => (s.ThemeTried ? 1 : 0, 1)),
        // ---- Argent
        new("library-50", "Ludothèque", "Avoir 50 jeux dans la bibliothèque", TrophyTier.Argent, "", s => (s.GameCount, 50), "jeux"),
        new("hours-100", "Passionné", "Cumuler 100 heures de jeu", TrophyTier.Argent, "", s => (s.TotalHours, 100), "h"),
        new("finished-5", "Finisseur", "Terminer 5 jeux", TrophyTier.Argent, "", s => (s.Finished, 5), "jeux"),
        new("marathon", "Marathon", "Jouer 3 heures d'affilée", TrophyTier.Argent, "", s => (s.LongestSessionHours, 3), "h"),
        new("streak-7", "Fidèle", "Jouer 7 jours de suite", TrophyTier.Argent, "", s => (s.MaxStreakDays, 7), "jours"),
        new("variety", "Touche-à-tout", "Jouer à 5 jeux différents en une semaine", TrophyTier.Argent, "", s => (s.MaxGamesInAWeek, 5), "jeux"),
        new("achievement-100", "Chasseur de succès", "Obtenir 100 succès", TrophyTier.Argent, "", s => (s.AchievementsUnlocked, 100), "succès"),
        // ---- Or
        new("hours-500", "Vétéran", "Cumuler 500 heures de jeu", TrophyTier.Or, "", s => (s.TotalHours, 500), "h"),
        new("centurion", "Centurion", "Passer 100 heures sur un même jeu", TrophyTier.Or, "", s => (s.MaxGameHours, 100), "h"),
        new("perfect", "Perfectionniste", "Obtenir tous les succès d'un jeu", TrophyTier.Or, "", s => (s.PerfectGames, 1)),
        // ---- Platine
        new("platinum", "Légende d'Arcadia", "Obtenir tous les autres trophées", TrophyTier.Platine, "",
            s => (s.OtherTrophies, s.OtherTrophiesTotal)),
    };

    private sealed class Data
    {
        public Dictionary<string, DateTime> Unlocked { get; set; } = new();
        public bool Initialized { get; set; }
        public bool ThemeTried { get; set; }
    }

    private readonly string _file;
    private Data _data;

    public TrophyService(string file)
    {
        _file = file;
        _data = JsonFile.Read<Data>(file) ?? new Data();
    }

    public IReadOnlyDictionary<string, DateTime> Unlocked => _data.Unlocked;
    /// <summary>False until the first check: trophies already deserved then are granted silently.</summary>
    public bool Initialized => _data.Initialized;
    public bool ThemeTried => _data.ThemeTried;

    public void MarkThemeTried()
    {
        if (_data.ThemeTried) return;
        _data.ThemeTried = true;
        Save();
    }

    /// <summary>Grants every trophy whose goal is reached; returns the new ones (in tier order).</summary>
    public List<TrophyDef> Check(IReadOnlyCollection<Game> games, int collections, DateTime? now = null)
    {
        var when = now ?? DateTime.Now;
        var fresh = new List<TrophyDef>();
        var others = All.Where(t => t.Tier != TrophyTier.Platine).ToList();
        var stats = TrophyStats.From(games, collections, _data.ThemeTried);
        foreach (var t in others)
        {
            if (_data.Unlocked.ContainsKey(t.Id)) continue;
            var (value, target) = t.Progress(stats);
            if (value >= target) { _data.Unlocked[t.Id] = when; fresh.Add(t); }
        }
        var platinum = All.First(t => t.Tier == TrophyTier.Platine);
        if (!_data.Unlocked.ContainsKey(platinum.Id) && others.All(t => _data.Unlocked.ContainsKey(t.Id)))
        {
            _data.Unlocked[platinum.Id] = when;
            fresh.Add(platinum);
        }
        if (fresh.Count > 0 || !_data.Initialized)
        {
            _data.Initialized = true;
            Save();
        }
        return fresh;
    }

    /// <summary>Progress of every trophy, for the Statistics page.</summary>
    public TrophyStats Stats(IReadOnlyCollection<Game> games, int collections)
    {
        var others = All.Count(t => t.Tier != TrophyTier.Platine);
        var done = All.Count(t => t.Tier != TrophyTier.Platine && _data.Unlocked.ContainsKey(t.Id));
        return TrophyStats.From(games, collections, _data.ThemeTried, done, others);
    }

    private void Save()
    {
        try { JsonFile.Write(_file, _data); }
        catch (Exception ex) { Log.Warn($"Trophies not saved: {ex.Message}"); }
    }
}
