using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcadia.Services.Online;

/// <summary>An official announcement of a game (patch notes, events…) published on Steam.</summary>
public sealed class GameNews
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public DateTime Date { get; init; }
    public string Excerpt { get; init; } = "";
    public string? ImageUrl { get; init; }
    public string DateText => Format.Relative(Date);
}

/// <summary>
/// Official announcements of a game, from the public ISteamNews/GetNewsForApp endpoint (no key). Only the
/// developers' own feed is kept: the full feed mixes in press articles in every language.
/// </summary>
public static partial class SteamNews
{
    private const string ClanImages = "https://clan.akamai.steamstatic.com/images/";

    public static async Task<List<GameNews>> GetAsync(int appId, int count = 6, CancellationToken ct = default)
    {
        var url = $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appId}&count={count}&maxlength=0&feeds=steam_community_announcements&format=json";
        using var resp = await Http.Client.GetAsync(url, ct);
        if ((int)resp.StatusCode is 400 or 403 or 404) return new List<GameNews>(); // unknown app
        resp.EnsureSuccessStatusCode();
        return Parse(await resp.Content.ReadAsStringAsync(ct), appId);
    }

    public static List<GameNews> Parse(string json, int appId)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<GameNews>();
        foreach (var item in doc.RootElement.Obj("appnews").Arr("newsitems"))
        {
            var title = item.Str("title")?.Trim();
            var gid = item.Str("gid");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(gid)) continue;
            var contents = item.Str("contents") ?? "";
            var date = item.Long("date") is long unix && unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime : DateTime.MinValue;
            list.Add(new GameNews
            {
                Title = WebUtility.HtmlDecode(title),
                Url = $"https://store.steampowered.com/news/app/{appId}/view/{gid}",
                Date = date,
                Excerpt = Excerpt(contents),
                ImageUrl = FirstImage(contents),
            });
        }
        return list;
    }

    /// <summary>First picture of the post ([img src=…], [img]…[/img] or &lt;img src=…&gt;), with Steam's placeholder resolved.</summary>
    public static string? FirstImage(string contents)
    {
        var m = ImageTag().Match(contents);
        if (!m.Success) return null;
        var src = (m.Groups["a"].Success ? m.Groups["a"].Value : m.Groups["b"].Value).Trim().Trim('"');
        src = src.Replace("{STEAM_CLAN_IMAGE}/", ClanImages).Replace("{STEAM_CLAN_IMAGE}", ClanImages);
        return src.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? src : null;
    }

    /// <summary>Plain text of the beginning of the post (BBCode, HTML and links removed).</summary>
    public static string Excerpt(string contents, int max = 220)
    {
        var text = ImageBlock().Replace(contents, " ");
        text = Tag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = BareUrl().Replace(text, " ");
        text = Spaces().Replace(text, " ").Trim();
        if (text.Length <= max) return text;
        var cut = text.LastIndexOf(' ', max);
        return text[..(cut > max / 2 ? cut : max)].TrimEnd(',', '.', ';', ':', ' ') + "…";
    }

    [GeneratedRegex(@"\[img(?:\s+src=(?<a>""?[^\]""]+""?))?\](?<b>[^\[]*)|<img[^>]*src=""(?<a>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImageTag();

    [GeneratedRegex(@"\[img[^\]]*\][^\[]*\[/img\]|\[previewyoutube[^\]]*\]\[/previewyoutube\]|\[video[^\]]*\][^\[]*\[/video\]", RegexOptions.IgnoreCase)]
    private static partial Regex ImageBlock();

    [GeneratedRegex(@"\[/?[a-z0-9*]+(?:[ =][^\]]*)?\]|<[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex Tag();

    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BareUrl();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
