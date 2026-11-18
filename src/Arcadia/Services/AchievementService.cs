using System.Globalization;
using System.Text.Json;
using Arcadia.Models;
using Arcadia.Services.Online;

namespace Arcadia.Services;

/// <summary>
/// Finds the official achievement list of a game on Steam (names and descriptions in French, icons, share of
/// players). Games already identified on Steam use their app id; the others are looked up by name, strictly.
/// </summary>
public sealed class AchievementService
{
    private const string IconBase = "https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/";
    private readonly SteamClient _steam = new();

    /// <summary>The Steam game holding the achievements of <paramref name="game"/>, or null when there is none.</summary>
    public async Task<int?> FindAppIdAsync(Game game, CancellationToken ct = default)
    {
        if (game.SteamAppId is int known) return known;
        var name = game.Name;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var hits = await _steam.SearchAsync(name, ct);
        // a wrong list is worse than none: the name must match (almost) exactly
        var best = hits.Take(10)
            .Select(h => (h.AppId, Score: NameMatcher.Similarity(name, h.Name) * NameMatcher.ResultPenalty(name, h.Name)))
            .Where(x => x.Score >= 0.9)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
        return best.Score > 0 ? best.AppId : null;
    }

    public async Task<List<Achievement>> FetchAsync(int appId, CancellationToken ct = default) =>
        Parse(await _steam.GetAchievementsJsonAsync(appId, ct), appId);

    /// <summary>Reads the answer of IPlayerService/GetGameAchievements.</summary>
    public static List<Achievement> Parse(string json, int appId)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<Achievement>();
        foreach (var a in doc.RootElement.Obj("response").Arr("achievements"))
        {
            var key = a.Str("internal_name");
            var name = a.Str("localized_name")?.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name)) continue;
            if (a.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True) continue;
            double? percent = null;
            if (a.TryGetProperty("player_percent_unlocked", out var p))
            {
                if (p.ValueKind == JsonValueKind.Number) percent = p.GetDouble();
                else if (double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) percent = d;
            }
            list.Add(new Achievement
            {
                Key = key,
                Name = name,
                Description = a.Str("localized_desc")?.Trim() is { Length: > 0 } desc ? desc : null,
                Hidden = a.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True,
                Percent = percent,
                Icon = Url(appId, a.Str("icon")),
                IconGray = Url(appId, a.Str("icon_gray")),
            });
        }
        return list;
    }

    private static string? Url(int appId, string? file) => string.IsNullOrWhiteSpace(file) ? null : $"{IconBase}{appId}/{file}";

    /// <summary>
    /// Sets what Steam reports as unlocked (Steam game): returns how many achievements changed and how many
    /// are newly unlocked.
    /// </summary>
    public static (int Changed, int Newly) ApplyUnlocks(IEnumerable<Achievement> list, IReadOnlyDictionary<string, DateTime> unlocked)
    {
        int changed = 0, newly = 0;
        foreach (var a in list)
        {
            if (unlocked.TryGetValue(a.Key, out var at))
            {
                // no recorded time (rare): keep the date already known, else today
                var when = at == DateTime.MinValue ? a.UnlockedAt ?? DateTime.Now : at;
                if (a.UnlockedAt == when) continue;
                if (a.UnlockedAt == null) newly++;
                a.UnlockedAt = when;
                changed++;
            }
            else if (a.UnlockedAt != null)
            {
                a.UnlockedAt = null; // reset in Steam
                changed++;
            }
        }
        return (changed, newly);
    }

    /// <summary>New list from Steam, keeping what the player already ticked.</summary>
    public static List<Achievement> Merge(List<Achievement> fresh, IEnumerable<Achievement>? previous)
    {
        var ticked = (previous ?? Enumerable.Empty<Achievement>())
            .Where(a => a.UnlockedAt != null)
            .GroupBy(a => a.Key)
            .ToDictionary(g => g.Key, g => g.First().UnlockedAt);
        foreach (var a in fresh)
            if (ticked.TryGetValue(a.Key, out var at)) a.UnlockedAt = at;
        return fresh;
    }
}
