using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using Arcadia.Models;
using Arcadia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Arcadia.ViewModels;

/// <summary>A trophy as shown in the Statistics page or in the "trophy obtained" banner.</summary>
public sealed class TrophyItem
{
    public required TrophyDef Def { get; init; }
    public DateTime? UnlockedAt { get; init; }
    public double Value { get; init; }
    public double Target { get; init; }

    public string Name => Def.Name;
    public string Description => Def.Description;
    public string Glyph => Def.Glyph;
    public bool IsUnlocked => UnlockedAt != null;
    public string TierLabel => Def.Tier switch { TrophyTier.Bronze => "Bronze", TrophyTier.Argent => "Argent", TrophyTier.Or => "Or", _ => "Platine" };
    public Brush TierBrush => TierBrushes[Def.Tier];
    public double Ratio => Target <= 0 ? 0 : Math.Clamp(Value / Target, 0, 1);

    public string StatusText => UnlockedAt is DateTime d
        ? $"Obtenu le {d.ToString("d MMMM yyyy", Format.Fr)}"
        : Target <= 1 ? "À obtenir" : $"{Number(Math.Min(Value, Target))} / {Number(Target)}{(Def.Unit.Length > 0 ? " " + Def.Unit : "")}";

    private static string Number(double v) => v < 10 && v % 1 != 0 ? v.ToString("0.#", Format.Fr) : Math.Floor(v).ToString("N0", Format.Fr);

    private static readonly Dictionary<TrophyTier, Brush> TierBrushes = new()
    {
        [TrophyTier.Bronze] = Frozen(0xD0, 0x8A, 0x52),
        [TrophyTier.Argent] = Frozen(0xC4, 0xCC, 0xD8),
        [TrophyTier.Or] = Frozen(0xF5, 0xC5, 0x42),
        [TrophyTier.Platine] = Frozen(0x9F, 0xE7, 0xF5),
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Arcadia's trophies: checked after every change of the library, announced by a banner.</summary>
public partial class MainViewModel
{
    private TrophyService? _trophyService;
    private DispatcherTimer? _trophyTimer;
    private bool _trophiesStarted;
    private readonly Queue<TrophyDef> _trophyQueue = new();
    private bool _trophyShowing;

    public TrophyService Trophies => _trophyService ??= new TrophyService(Path.Combine(AppPaths.DataDir, "trophies.json"));

    /// <summary>The banner currently shown ("Trophée obtenu"), null when none.</summary>
    [ObservableProperty] private TrophyItem? _trophyBanner;

    private IReadOnlyCollection<Game> TrophyGames => Library.Games.Where(g => g.IsShown).ToList();

    /// <summary>First check a little after startup (Steam play time and identification have run by then).</summary>
    private async Task StartTrophiesAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        // first launch: after the assistant (the games it imports count, and no message on top of it)
        while (!Settings.OnboardingDone || Dialogs.OfType<OnboardingViewModel>().Any())
            await Task.Delay(TimeSpan.FromSeconds(2));
        _trophiesStarted = true;
        CheckTrophies();
        AnnounceRecap();
        Library.Games.CollectionChanged += (_, _) => ScheduleTrophyCheck();
        Library.Collections.CollectionChanged += (_, _) => ScheduleTrophyCheck();
    }

    /// <summary>Debounced: an import or a sync changes many games at once.</summary>
    private void ScheduleTrophyCheck()
    {
        if (!_trophiesStarted) return;
        _trophyTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => { _trophyTimer!.Stop(); CheckTrophies(); }, Dispatcher.CurrentDispatcher);
        _trophyTimer.Stop();
        _trophyTimer.Start();
    }

    private void OnGameChangedForTrophies(string? property)
    {
        switch (property)
        {
            case nameof(Game.PlayTimeSeconds):
            case nameof(Game.Status):
            case nameof(Game.IsFavorite):
            case nameof(Game.Rating):
            case nameof(Game.Collections):
            case nameof(Game.AchievementsUnlocked):
            case nameof(Game.Achievements):
            case nameof(Game.LastPlayedAt):
                ScheduleTrophyCheck();
                break;
        }
    }

    private void CheckTrophies()
    {
        try
        {
            var firstTime = !Trophies.Initialized;
            var fresh = Trophies.Check(TrophyGames, Library.Collections.Count);
            if (fresh.Count == 0) return;
            if (firstTime)
            {
                // the trophies the library already deserved: one message, no avalanche of banners
                ShowToast(fresh.Count == 1 ? "Vous avez déjà un trophée Arcadia !" : $"Vous avez déjà {fresh.Count} trophées Arcadia !",
                    ToastKind.Success, "Voir", () => Section = Section.Stats, seconds: 10);
                return;
            }
            foreach (var t in fresh) _trophyQueue.Enqueue(t);
            if (!_trophyShowing) _ = ShowTrophyBannersAsync();
        }
        catch (Exception ex) { Log.Warn($"Trophy check failed: {ex.Message}"); }
    }

    private async Task ShowTrophyBannersAsync()
    {
        _trophyShowing = true;
        try
        {
            while (_trophyQueue.Count > 0)
            {
                var def = _trophyQueue.Dequeue();
                Log.Info($"Trophy obtained: {def.Name} ({def.Tier})");
                TrophyBanner = new TrophyItem { Def = def, UnlockedAt = DateTime.Now };
                Sound.Play(Sfx.Trophy);
                await Task.Delay(TimeSpan.FromSeconds(5));
                TrophyBanner = null;
                await Task.Delay(500); // the banner slides out before the next one
            }
        }
        finally { _trophyShowing = false; }
    }

    /// <summary>Every trophy with its progress, obtained first (most recent first), then the closest to completion.</summary>
    public List<TrophyItem> BuildTrophyItems()
    {
        var stats = Trophies.Stats(TrophyGames, Library.Collections.Count);
        return TrophyService.All.Select(def =>
            {
                var (value, target) = def.Progress(stats);
                return new TrophyItem
                {
                    Def = def,
                    UnlockedAt = Trophies.Unlocked.TryGetValue(def.Id, out var at) ? at : null,
                    Value = value,
                    Target = target,
                };
            })
            .OrderByDescending(t => t.IsUnlocked)
            .ThenByDescending(t => t.UnlockedAt?.Date)
            .ThenByDescending(t => t.IsUnlocked ? (int)t.Def.Tier : 0) // same day: the most precious first
            .ThenByDescending(t => t.Ratio)
            .ThenBy(t => t.Def.Tier)
            .ToList();
    }

    public string TrophySummary(List<TrophyItem> items) =>
        string.Create(CultureInfo.CurrentCulture, $"{items.Count(t => t.IsUnlocked)} / {items.Count}");
}
