using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcadia.Services.Online;

public sealed record WikiHit(string Title, string? Description, string? Extract, string? ImageUrl, int Width, int Height, int Index)
{
    public bool LooksLikeGame =>
        (Description?.Contains("game", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Description?.Contains("jeu", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Extract?.Contains("video game", StringComparison.OrdinalIgnoreCase) ?? false)
        || (Extract?.Contains("jeu vidéo", StringComparison.OrdinalIgnoreCase) ?? false);

    public string CleanTitle => Regex.Replace(Title, @"\s*\([^)]*\)\s*$", "").Trim();
}

/// <summary>Fallback source for games that are not on Steam (console games, emulators, launchers...).</summary>
public sealed class WikipediaClient
{
    private readonly string _lang;
    private readonly string _suffix;

    public WikipediaClient(string lang = "en")
    {
        _lang = lang;
        _suffix = lang == "fr" ? " jeu vidéo" : " video game";
    }

    private string Api => $"https://{_lang}.wikipedia.org/w/api.php";

    /// <summary>
    /// The image declared in the article's infobox (cover, or logo for platforms like Roblox / Minecraft),
    /// rendered as a bitmap thumbnail (SVG logos included).
    /// </summary>
    public async Task<(string Url, int Width, int Height)?> InfoboxImageAsync(string title, CancellationToken ct = default)
    {
        try
        {
            var url = $"{Api}?action=parse&format=json&formatversion=2&prop=wikitext&section=0&redirects=1&page={Uri.EscapeDataString(title)}";
            using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(url, ct));
            var wikitext = doc.RootElement.Obj("parse").Str("wikitext");
            if (wikitext == null) return null;
            var m = Regex.Match(wikitext, @"\|\s*(?:image|logo|cover)\s*=\s*(?:\[\[)?(?:File:|Fichier:|Image:)?([^|\]\n<{]+?\.(?:png|jpe?g|svg|webp|gif))", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var file = "File:" + m.Groups[1].Value.Trim();
            var infoUrl = $"{Api}?action=query&format=json&formatversion=2&prop=imageinfo&iiprop=url%7Csize&iiurlwidth=800&titles={Uri.EscapeDataString(file)}";
            using var info = JsonDocument.Parse(await Http.Client.GetStringAsync(infoUrl, ct));
            var ii = info.RootElement.Obj("query").Arr("pages").SelectMany(p => p.Arr("imageinfo")).FirstOrDefault();
            var thumb = ii.Str("thumburl") ?? ii.Str("url");
            if (thumb == null || thumb.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
            return (thumb, ii.Int("thumbwidth") ?? ii.Int("width") ?? 0, ii.Int("thumbheight") ?? ii.Int("height") ?? 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<List<WikiHit>> SearchAsync(string term, CancellationToken ct = default)
    {
        var url = $"{Api}?action=query&format=json&formatversion=2&redirects=1" +
                  "&generator=search&gsrlimit=6&gsrsearch=" + Uri.EscapeDataString(term + _suffix) +
                  "&prop=pageimages%7Cdescription%7Cextracts&piprop=thumbnail%7Coriginal&pithumbsize=800&pilicense=any" +
                  "&exintro=1&explaintext=1&exsentences=3&exlimit=max";
        using var resp = await Http.Client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        var hits = new List<WikiHit>();
        foreach (var p in doc.RootElement.Obj("query").Arr("pages"))
        {
            var title = p.Str("title");
            if (string.IsNullOrWhiteSpace(title)) continue;
            var thumb = p.Obj("thumbnail");
            var original = p.Obj("original");
            var img = thumb.Str("source") ?? original.Str("source");
            var w = thumb.Int("width") ?? original.Int("width") ?? 0;
            var h = thumb.Int("height") ?? original.Int("height") ?? 0;
            if (img != null && img.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) img = null;
            hits.Add(new WikiHit(title, p.Str("description"), p.Str("extract"), img, w, h, p.Int("index") ?? 99));
        }
        return hits.OrderBy(h => h.Index).ToList();
    }
}
