using System.Globalization;
using System.Text.Json;

namespace Arcadia.Services.Online;

/// <summary>Live figures of a game on Steam (official public endpoints, no key, nothing scraped).</summary>
public sealed class SteamStats
{
    public int AppId { get; init; }
    public int? PlayersNow { get; init; }
    public bool IsFree { get; init; }
    public string? Price { get; init; }
    public string? PriceBefore { get; init; }
    public int Discount { get; init; }
    public int? Metacritic { get; init; }
    public int? Recommendations { get; init; }
    public int? Achievements { get; init; }

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public string PlayersText => PlayersNow is int n ? n.ToString("N0", Fr) : "—";
    public string PriceText => IsFree ? "Gratuit" : Price ?? "—";
    public bool HasDiscount => Discount > 0 && PriceBefore != null;
    public string DiscountText => $"-{Discount} %";
    public string RecommendationsText => Recommendations is int n ? n.ToString("N0", Fr) : "—";
    public string MetacriticText => Metacritic is int m ? m.ToString(Fr) : "—";
    public bool HasMetacritic => Metacritic != null;
    public string AchievementsText => Achievements is int n && n > 0 ? n.ToString(Fr) : "—";
    public string SteamDbUrl => $"https://steamdb.info/app/{AppId}/";
}

public static class SteamStatsClient
{
    public static async Task<SteamStats> GetAsync(int appId, CancellationToken ct = default)
    {
        var players = GetPlayersAsync(appId, ct);
        var details = GetDetailsJsonAsync(appId, ct);
        string? detailsJson = null;
        int? count = null;
        try { count = await players; } catch (Exception ex) { Log.Warn($"Steam players unavailable for {appId}: {ex.Message}"); }
        try { detailsJson = await details; } catch (Exception ex) { Log.Warn($"Steam details unavailable for {appId}: {ex.Message}"); }
        return Parse(appId, count, detailsJson);
    }

    private static async Task<int?> GetPlayersAsync(int appId, CancellationToken ct)
    {
        using var resp = await Http.Client.GetAsync($"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={appId}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return ParsePlayers(await resp.Content.ReadAsStringAsync(ct));
    }

    private static async Task<string?> GetDetailsJsonAsync(int appId, CancellationToken ct)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=fr&l=french"
                  + "&filters=basic,price_overview,metacritic,recommendations,achievements";
        using var resp = await Http.Client.GetAsync(url, ct);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct) : null;
    }

    internal static int? ParsePlayers(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement.Obj("response");
        return r.Long("result") == 1 && r.Long("player_count") is long n ? (int)n : null;
    }

    internal static SteamStats Parse(int appId, int? players, string? detailsJson)
    {
        bool free = false;
        string? price = null, before = null;
        int discount = 0;
        int? meta = null, recommendations = null, achievements = null;
        if (detailsJson != null)
        {
            using var doc = JsonDocument.Parse(detailsJson);
            if (doc.RootElement.TryGetProperty(appId.ToString(), out var app) && app.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Object)
            {
                free = data.TryGetProperty("is_free", out var f) && f.ValueKind == JsonValueKind.True;
                var po = data.Obj("price_overview");
                price = NonEmpty(po.Str("final_formatted"));
                before = NonEmpty(po.Str("initial_formatted"));
                discount = (int)(po.Long("discount_percent") ?? 0);
                meta = data.Obj("metacritic").Long("score") is long m ? (int)m : null;
                recommendations = data.Obj("recommendations").Long("total") is long r ? (int)r : null;
                achievements = data.Obj("achievements").Long("total") is long a ? (int)a : null;
            }
        }
        return new SteamStats
        {
            AppId = appId,
            PlayersNow = players,
            IsFree = free,
            Price = price,
            PriceBefore = discount > 0 ? before : null,
            Discount = discount,
            Metacritic = meta,
            Recommendations = recommendations,
            Achievements = achievements,
        };
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
