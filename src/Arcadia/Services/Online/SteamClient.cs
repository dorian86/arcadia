using System.Text.Json;

namespace Arcadia.Services.Online;

public sealed record SteamSearchHit(int AppId, string Name);

public sealed class SteamItem
{
    public int AppId { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? Developer { get; init; }
    public string? Publisher { get; init; }
    public int? ReleaseYear { get; init; }
    public bool HasVerticalCover { get; init; }
    public List<string> CoverUrls { get; init; } = new();
    public string? CoverThumbUrl { get; init; }
    public string? HeaderUrl { get; init; }
    public List<string> HeroUrls { get; init; } = new();
    public List<string> LogoUrls { get; init; } = new();
}

/// <summary>
/// Public Steam store endpoints (no API key): store search, IStoreBrowseService/GetItems (names, French
/// descriptions, official artwork paths) and appdetails (genres). Requests are throttled.
/// </summary>
public sealed class SteamClient
{
    private const string AssetBase = "https://shared.akamai.steamstatic.com/store_item_assets/";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;

    private static async Task<string> GetThrottledAsync(string url, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var wait = _lastCall.AddMilliseconds(350) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastCall = DateTime.UtcNow;
        }
        finally { Gate.Release(); }

        using var resp = await Http.Client.GetAsync(url, ct);
        if ((int)resp.StatusCode == 429)
        {
            // rate limited: back off progressively (and slow down every following request)
            foreach (var wait in new[] { 10, 30, 60 })
            {
                Log.Warn($"Steam rate limit, retrying in {wait}s");
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
                _lastCall = DateTime.UtcNow.AddSeconds(2);
                using var retry = await Http.Client.GetAsync(url, ct);
                if ((int)retry.StatusCode == 429) continue;
                retry.EnsureSuccessStatusCode();
                return await retry.Content.ReadAsStringAsync(ct);
            }
            resp.EnsureSuccessStatusCode();
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public async Task<List<SteamSearchHit>> SearchAsync(string term, CancellationToken ct = default)
    {
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(term)}&l=english&cc=US";
        var json = await GetThrottledAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<SteamSearchHit>();
        foreach (var item in doc.RootElement.Arr("items"))
        {
            var id = item.Int("id");
            var name = item.Str("name");
            if (id is int i && !string.IsNullOrWhiteSpace(name)) list.Add(new SteamSearchHit(i, name));
        }
        return list;
    }

    public async Task<List<SteamItem>> GetItemsAsync(IEnumerable<int> appIds, CancellationToken ct = default, string language = "english")
    {
        var ids = appIds.Distinct().ToArray();
        if (ids.Length == 0) return new List<SteamItem>();
        var input = new
        {
            ids = ids.Select(i => new { appid = i }).ToArray(),
            context = new { language, country_code = "US", steam_realm = 1 },
            data_request = new { include_assets = true, include_basic_info = true, include_release = true },
        };
        var url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=" +
                  Uri.EscapeDataString(JsonSerializer.Serialize(input));
        var json = await GetThrottledAsync(url, ct);
        using var doc = JsonDocument.Parse(json);

        var byId = new Dictionary<int, SteamItem>();
        foreach (var it in doc.RootElement.Obj("response").Arr("store_items"))
        {
            if (it.Int("success") != 1) continue;
            var parsed = Parse(it);
            if (parsed != null) byId[parsed.AppId] = parsed;
        }
        return ids.Where(byId.ContainsKey).Select(i => byId[i]).ToList();
    }

    public async Task<(string? Genres, List<Models.Screenshot> Screenshots, List<Models.Trailer>? Trailers, string? Description)> GetDetailsAsync(int appId, CancellationToken ct = default)
    {
        try
        {
            var json = await GetThrottledAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=french&filters=basic,genres,screenshots,movies", ct);
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.Obj(appId.ToString()).Obj("data");
            var genres = data.Arr("genres").Select(g => g.Str("description")).Where(s => !string.IsNullOrWhiteSpace(s)).Take(3).ToList();
            var shots = data.Arr("screenshots")
                .Select(s => new Models.Screenshot { Thumb = s.Str("path_thumbnail") ?? "", Full = s.Str("path_full") ?? "" })
                .Where(s => s.Thumb.Length > 0 && s.Full.Length > 0)
                .Take(8).ToList();
            var movies = data.Arr("movies")
                .Select(m => new Models.Trailer { Id = m.Long("id") ?? 0, Name = m.Str("name") ?? "", Thumb = m.Str("thumbnail") ?? "", Dash = m.Str("dash_h264") ?? "" })
                .Where(m => m.Id > 0 && m.Dash.Length > 0)
                .Take(4).ToList();
            var ok = data.ValueKind == JsonValueKind.Object;
            var desc = data.Str("short_description");
            if (desc != null) desc = System.Net.WebUtility.HtmlDecode(desc).Trim();
            return (genres.Count > 0 ? string.Join(", ", genres) : null, shots, ok ? movies : null, string.IsNullOrWhiteSpace(desc) ? null : desc);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, new(), null, null);
        }
    }

    private static SteamItem? Parse(JsonElement it)
    {
        var appId = it.Int("appid") ?? it.Int("id");
        if (appId is not int id) return null;
        var name = it.Str("name") ?? "";
        var basic = it.Obj("basic_info");
        var assets = it.Obj("assets");
        var release = it.Obj("release");

        var format = assets.Str("asset_url_format");
        string? Asset(string? file) =>
            string.IsNullOrWhiteSpace(file) ? null :
            format != null ? AssetBase + format.Replace("${FILENAME}", file) :
            $"{AssetBase}steam/apps/{id}/{file}";
        string Legacy(string file) => $"{AssetBase}steam/apps/{id}/{file}";

        var capsule2x = Asset(assets.Str("library_capsule_2x"));
        var capsule = Asset(assets.Str("library_capsule"));
        var header = Asset(assets.Str("header")) ?? Legacy("header.jpg");
        var hasVertical = capsule != null || capsule2x != null;

        var covers = new List<string>();
        if (capsule2x != null) covers.Add(capsule2x);
        if (capsule != null) covers.Add(capsule);
        if (!hasVertical)
        {
            covers.Add(Legacy("library_600x900_2x.jpg"));
            covers.Add(Legacy("library_600x900.jpg"));
        }
        covers.Add(header);

        var heroes = new List<string>();
        var hero = Asset(assets.Str("library_hero"));
        if (hero != null) heroes.Add(hero);
        heroes.Add(Legacy("library_hero.jpg"));

        var logos = new List<string>();
        var logoAsset = Asset("logo.png");
        if (logoAsset != null) logos.Add(logoAsset);
        logos.Add(Legacy("logo.png"));

        int? year = null;
        var ts = release.Long("original_release_date") ?? release.Long("steam_release_date");
        if (ts is long t && t > 0) year = DateTimeOffset.FromUnixTimeSeconds(t).Year;

        static string? FirstName(JsonElement parent, string prop) =>
            parent.Arr(prop).Select(x => x.Str("name")).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        var description = basic.Str("short_description");
        if (description != null) description = System.Net.WebUtility.HtmlDecode(description).Trim();

        return new SteamItem
        {
            AppId = id,
            Name = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            Developer = FirstName(basic, "developers"),
            Publisher = FirstName(basic, "publishers"),
            ReleaseYear = year,
            HasVerticalCover = hasVertical,
            CoverUrls = covers.Distinct().ToList(),
            CoverThumbUrl = capsule ?? capsule2x ?? header,
            HeaderUrl = header,
            HeroUrls = heroes.Distinct().ToList(),
            LogoUrls = logos.Distinct().ToList(),
        };
    }
}
