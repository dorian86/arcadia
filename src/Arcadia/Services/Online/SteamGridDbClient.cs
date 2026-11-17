using System.Net.Http.Headers;
using System.Text.Json;

namespace Arcadia.Services.Online;

public sealed record SgdbGame(int Id, string Name);
public sealed record SgdbImage(string Url, string Thumb);

/// <summary>Optional artwork source (requires a free API key from steamgriddb.com).</summary>
public sealed class SteamGridDbClient
{
    private const string Dimensions = "600x900,342x482,660x930";
    private readonly Func<string?> _apiKey;

    public SteamGridDbClient(Func<string?> apiKey) => _apiKey = apiKey;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey());

    private async Task<JsonElement?> GetDataAsync(string path, CancellationToken ct)
    {
        var key = _apiKey();
        if (string.IsNullOrWhiteSpace(key)) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.steamgriddb.com/api/v2/" + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        using var resp = await Http.Client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            Log.Warn($"SteamGridDB {path}: HTTP {(int)resp.StatusCode}");
            return null;
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True && root.TryGetProperty("data", out var data))
            return data.Clone();
        return null;
    }

    public async Task<List<SgdbGame>> SearchAsync(string term, CancellationToken ct = default)
    {
        var data = await GetDataAsync("search/autocomplete/" + Uri.EscapeDataString(term), ct);
        var list = new List<SgdbGame>();
        if (data is { ValueKind: JsonValueKind.Array } arr)
            foreach (var g in arr.EnumerateArray())
                if (g.Int("id") is int id && g.Str("name") is string name) list.Add(new SgdbGame(id, name));
        return list;
    }

    public Task<List<SgdbImage>> GridsAsync(int gameId, CancellationToken ct = default) =>
        ImagesAsync($"grids/game/{gameId}?dimensions={Dimensions}&types=static&nsfw=false&humor=false", ct);

    public Task<List<SgdbImage>> GridsForSteamAsync(int appId, CancellationToken ct = default) =>
        ImagesAsync($"grids/steam/{appId}?dimensions={Dimensions}&types=static&nsfw=false&humor=false", ct);

    public Task<List<SgdbImage>> HeroesAsync(int gameId, CancellationToken ct = default) =>
        ImagesAsync($"heroes/game/{gameId}?types=static&nsfw=false&humor=false", ct);

    public Task<List<SgdbImage>> LogosAsync(int gameId, CancellationToken ct = default) =>
        ImagesAsync($"logos/game/{gameId}?types=static&nsfw=false&humor=false", ct);

    private async Task<List<SgdbImage>> ImagesAsync(string path, CancellationToken ct)
    {
        var data = await GetDataAsync(path, ct);
        var list = new List<SgdbImage>();
        if (data is { ValueKind: JsonValueKind.Array } arr)
            foreach (var img in arr.EnumerateArray())
            {
                var url = img.Str("url");
                if (url == null || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new SgdbImage(url, img.Str("thumb") ?? url));
            }
        return list;
    }
}
