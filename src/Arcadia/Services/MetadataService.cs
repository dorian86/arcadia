using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Windows.Threading;
using Arcadia.Models;
using Arcadia.Services.Online;

namespace Arcadia.Services;

public sealed class MetadataResult
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public int? SteamAppId { get; set; }
    public string? Description { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public string? Genres { get; set; }
    public int? ReleaseYear { get; set; }
    public List<string> CoverUrls { get; set; } = new();
    public List<string> HeroUrls { get; set; } = new();
    public List<string> LogoUrls { get; set; } = new();
    public List<Screenshot>? Screenshots { get; set; }
    public List<Trailer>? Trailers { get; set; }
    /// <summary>Matched through a variant of the name (e.g. the full game of a demo): keep the user's name.</summary>
    public bool KeepName { get; set; }
    public double Score { get; set; }
}

/// <summary>A cover proposed in the manual search dialog.</summary>
public sealed class CoverCandidate
{
    public string Title { get; init; } = "";
    public string Source { get; init; } = "";
    public string? Subtitle { get; init; }
    public string ThumbUrl { get; init; } = "";
    public List<string> CoverUrls { get; init; } = new();
    public bool IsWide { get; init; }
    public int? SteamAppId { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Identifies games online and downloads their artwork into the local cache.
/// Sources: Steam store (no key) → SteamGridDB (optional key) → Wikipedia.
/// </summary>
public sealed partial class MetadataService
{
    private readonly Dispatcher _ui;
    private readonly SteamClient _steam = new();
    private readonly WikipediaClient _wiki = new();
    private readonly WikipediaClient _wikiFr = new("fr");
    private readonly SteamGridDbClient _sgdb;
    private readonly Channel<Game> _queue = Channel.CreateUnbounded<Game>();
    private readonly ConcurrentDictionary<string, byte> _queued = new();
    private readonly ConcurrentDictionary<string, int> _retries = new();

    [System.Text.RegularExpressions.GeneratedRegex(@"s+(?:demo|playtest|prologue|beta|trial)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex DemoSuffix();

    public event Action? QueueEmptied;

    public bool IsBusy => !_queued.IsEmpty;

    public MetadataService(AppSettings settings, Dispatcher ui)
    {
        _ui = ui;
        _sgdb = new SteamGridDbClient(() => settings.SteamGridDbApiKey);
        for (var i = 0; i < 2; i++) _ = Task.Run(WorkerAsync);
    }

    /// <summary>Queues a game for automatic identification (UI thread).</summary>
    public void Enqueue(Game game)
    {
        if (!_queued.TryAdd(game.Id, 0)) return;
        game.IsFetching = true;
        _queue.Writer.TryWrite(game);
    }

    private async Task WorkerAsync()
    {
        await foreach (var game in _queue.Reader.ReadAllAsync())
        {
            try { await ProcessAsync(game); }
            catch (Exception ex) { Log.Error($"Metadata failed for '{game.Name}'", ex); }
            finally
            {
                _queued.TryRemove(game.Id, out _);
                await _ui.InvokeAsync(() =>
                {
                    game.IsFetching = false;
                    if (_queued.IsEmpty) QueueEmptied?.Invoke();
                });
            }
        }
    }

    private async Task ProcessAsync(Game game)
    {
        var hints = new List<string>();
        if (game.NameLocked) hints.Add(game.Name);
        if (game.NameHints != null) hints.AddRange(game.NameHints);
        if (hints.Count == 0) hints.Add(game.Name);

        MetadataResult? result;
        try
        {
            result = await IdentifyAsync(game.SteamAppId, hints, CancellationToken.None);
        }
        catch (Exception ex) when (Http.IsNetworkError(ex))
        {
            Log.Warn($"Offline while identifying '{game.Name}': {ex.Message}");
            // rate limited (429) rather than offline: try again later in this session
            if (ex.Message.Contains("429") && _retries.AddOrUpdate(game.Id, 1, (_, n) => n + 1) <= 3)
                _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => _ui.InvokeAsync(() => Enqueue(game)));
            return; // stays Pending: retried on next start
        }

        if (result == null)
        {
            Log.Info($"No match for '{game.Name}' (hints: {string.Join(" | ", hints)})");
            await _ui.InvokeAsync(async () =>
            {
                game.MetadataState = MetadataState.NotFound;
                if (!game.HasCover || game.CoverGenerated) await GenerateCoverAsync(game);
            });
            return;
        }

        Log.Info($"'{game.Name}' identified as '{result.Name}' via {result.Source} (score {result.Score:0.00})");
        await ApplyResultAsync(game, result, applyName: !game.NameLocked, applyCover: !game.CustomCover, manual: false);
    }

    // ------------------------------------------------------------------------------------
    // Identification
    // ------------------------------------------------------------------------------------

    public async Task<MetadataResult?> IdentifyAsync(int? steamAppId, IReadOnlyList<string> hints, CancellationToken ct)
    {
        if (steamAppId is int knownId)
        {
            var item = (await _steam.GetItemsAsync(new[] { knownId }, ct)).FirstOrDefault();
            if (item != null) return await FromSteamAsync(item, 1.0, ct);
        }

        var queries = hints.Where(h => !string.IsNullOrWhiteSpace(h))
                           .DistinctBy(NameMatcher.Compact)
                           .Take(4)
                           .ToList();
        // "X Demo" / "X Playtest": Steam often hides the demo page, the full game has the artwork
        var keepName = false;
        foreach (var q in queries.ToList())
        {
            var stripped = DemoSuffix().Replace(q, "").Trim();
            if (stripped.Length > 1 && stripped != q && !queries.Contains(stripped)) { queries.Add(stripped); keepName = true; }
        }

        (int Id, string Name, double Score)? best = null;
        for (var qi = 0; qi < queries.Count; qi++)
        {
            var q = queries[qi];
            var minScore = qi == 0 ? 0.75 : 0.9; // fallback names must match almost exactly
            var hits = await _steam.SearchAsync(q, ct);
            foreach (var hit in hits.Take(10))
            {
                var score = NameMatcher.Similarity(q, hit.Name) * NameMatcher.ResultPenalty(q, hit.Name);
                if (score < minScore) continue;
                if (best == null || score > best.Value.Score) best = (hit.AppId, hit.Name, score);
            }
            if (best?.Score >= 0.95) break;
        }

        if (best is { Score: >= 0.75 } match)
        {
            var item = (await _steam.GetItemsAsync(new[] { match.Id }, ct)).FirstOrDefault();
            if (item != null) 
            {
                var r = await FromSteamAsync(item, match.Score, ct);
                r.KeepName = keepName && !hints.Any(h => NameMatcher.Similarity(h, item.Name) >= 0.9);
                return r;
            }
        }

        if (_sgdb.IsConfigured)
        {
            var r = await TrySteamGridDbAsync(queries, ct);
            if (r != null) return r;
        }

        for (var qi = 0; qi < Math.Min(3, queries.Count); qi++)
        {
            var r = await TryWikipediaAsync(queries[qi], ct, qi == 0 ? 0.8 : 0.92);
            if (r != null) return r;
        }
        if (queries.Count > 0 && await TryWikipediaAsync(_wikiFr, queries[0], ct, 0.85) is { } fr) return fr;
        return null;
    }

    private async Task<MetadataResult> FromSteamAsync(SteamItem item, double score, CancellationToken ct)
    {
        var result = new MetadataResult
        {
            Name = item.Name,
            Source = "Steam",
            SteamAppId = item.AppId,
            Description = item.Description,
            Developer = item.Developer,
            Publisher = item.Publisher,
            ReleaseYear = item.ReleaseYear,
            CoverUrls = item.CoverUrls.ToList(),
            HeroUrls = item.HeroUrls.ToList(),
            LogoUrls = item.LogoUrls.ToList(),
            Score = score,
        };
        var details = await _steam.GetDetailsAsync(item.AppId, ct);
        result.Genres = details.Genres;
        if (details.Description != null) result.Description = details.Description; // French (names stay English)
        if (details.Screenshots.Count > 0) result.Screenshots = details.Screenshots;
        if (details.Trailers != null) result.Trailers = details.Trailers;

        if (!item.HasVerticalCover && _sgdb.IsConfigured)
        {
            try
            {
                var grids = await _sgdb.GridsForSteamAsync(item.AppId, ct);
                result.CoverUrls.InsertRange(0, grids.Take(2).Select(g => g.Url));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* optional */ }
        }
        return result;
    }

    private async Task<MetadataResult?> TrySteamGridDbAsync(IEnumerable<string> queries, CancellationToken ct)
    {
        try
        {
            foreach (var q in queries.Take(2))
            {
                var games = await _sgdb.SearchAsync(q, ct);
                var best = games.Select(g => (Game: g, Score: NameMatcher.Similarity(q, g.Name)))
                                .OrderByDescending(x => x.Score).FirstOrDefault();
                if (best.Game == null || best.Score < 0.8) continue;
                var grids = await _sgdb.GridsAsync(best.Game.Id, ct);
                if (grids.Count == 0) continue;
                var heroes = await _sgdb.HeroesAsync(best.Game.Id, ct);
                var logos = await _sgdb.LogosAsync(best.Game.Id, ct);
                return new MetadataResult
                {
                    Name = best.Game.Name,
                    Source = "SteamGridDB",
                    Score = best.Score,
                    CoverUrls = grids.Take(3).Select(g => g.Url).ToList(),
                    HeroUrls = heroes.Take(2).Select(h => h.Url).ToList(),
                    LogoUrls = logos.Take(2).Select(l => l.Url).ToList(),
                };
            }
        }
        catch (Exception ex) when (!Http.IsNetworkError(ex) && ex is not OperationCanceledException)
        {
            Log.Warn($"SteamGridDB lookup failed: {ex.Message}");
        }
        return null;
    }

    private Task<MetadataResult?> TryWikipediaAsync(string query, CancellationToken ct, double minScore = 0.8) =>
        TryWikipediaAsync(_wiki, query, ct, minScore);

    private static async Task<MetadataResult?> TryWikipediaAsync(WikipediaClient wiki, string query, CancellationToken ct, double minScore)
    {
        var hits = await wiki.SearchAsync(query, ct);
        var best = hits.Where(h => h.LooksLikeGame)
                       .Select(h => (Hit: h, Score: NameMatcher.Similarity(query, h.CleanTitle)))
                       .Where(x => x.Score >= minScore)
                       .OrderByDescending(x => x.Score).ThenBy(x => x.Hit.ImageUrl == null).ThenBy(x => x.Hit.Index)
                       .FirstOrDefault();
        if (best.Hit == null) return null;

        var image = best.Hit.ImageUrl;
        // No page image (often a logo in SVG): use the image declared in the infobox
        if (image == null && await wiki.InfoboxImageAsync(best.Hit.Title, ct) is { } infobox) image = infobox.Url;
        if (image == null) return null;

        return new MetadataResult
        {
            Name = best.Hit.CleanTitle,
            Source = "Wikipedia",
            Description = best.Hit.Extract,
            CoverUrls = new List<string> { image },
            Score = best.Score,
        };
    }

    // ------------------------------------------------------------------------------------
    // Generated covers
    // ------------------------------------------------------------------------------------

    /// <summary>Creates a stylised cover for a game that has no artwork (UI thread).</summary>
    public async Task GenerateCoverAsync(Game game)
    {
        if (game.HasCover && !game.CoverGenerated) return;
        try
        {
            // a logo (from a previous match) makes a nicer centrepiece than the exe icon
            byte[]? logo = null;
            if (game.LogoFullPath is { } lp && File.Exists(lp)) logo = await File.ReadAllBytesAsync(lp);
            var bytes = CoverGenerator.Generate(game, logo);
            var rel = Path.Combine("images", $"{game.Id}_cover_gen_{DateTime.UtcNow.Ticks:x}.jpg");
            await File.WriteAllBytesAsync(AppPaths.Resolve(rel)!, bytes);
            ReplaceManaged(game, nameof(Game.Cover), rel);
            game.CoverGenerated = true;
            game.CustomCover = false;
        }
        catch (Exception ex) { Log.Warn($"Cover generation failed for '{game.Name}': {ex.Message}"); }
    }

    // ------------------------------------------------------------------------------------
    // Manual search
    // ------------------------------------------------------------------------------------

    public async Task<List<CoverCandidate>> SearchCandidatesAsync(string query, CancellationToken ct)
    {
        var steamTask = Safe(() => SteamCandidatesAsync(query, ct));
        var sgdbTask = _sgdb.IsConfigured ? Safe(() => SgdbCandidatesAsync(query, ct)) : Task.FromResult<List<CoverCandidate>?>(new());
        var wikiTask = Safe(() => WikiCandidatesAsync(query, ct));
        await Task.WhenAll(steamTask, sgdbTask, wikiTask);
        ct.ThrowIfCancellationRequested();

        if (steamTask.Result == null && wikiTask.Result == null)
            throw new HttpRequestException("Aucun service d'images n'a répondu.");

        var list = new List<CoverCandidate>();
        list.AddRange(steamTask.Result ?? new());
        list.AddRange(sgdbTask.Result ?? new());
        list.AddRange(wikiTask.Result ?? new());
        return list;
    }

    private static async Task<List<CoverCandidate>?> Safe(Func<Task<List<CoverCandidate>>> f)
    {
        try { return await f(); }
        catch (OperationCanceledException) { return new(); }
        catch (Exception ex) { Log.Warn($"Cover search source failed: {ex.Message}"); return null; }
    }

    private async Task<List<CoverCandidate>> SteamCandidatesAsync(string query, CancellationToken ct)
    {
        var hits = await _steam.SearchAsync(query, ct);
        var items = await _steam.GetItemsAsync(hits.Take(10).Select(h => h.AppId), ct);
        return items.Select(i => new CoverCandidate
        {
            Title = NameCleaner.CleanOfficialName(i.Name),
            Source = "Steam",
            Subtitle = string.Join(" · ", new[] { i.ReleaseYear?.ToString(), i.Developer }.Where(s => !string.IsNullOrWhiteSpace(s))),
            ThumbUrl = i.CoverThumbUrl ?? i.CoverUrls.First(),
            CoverUrls = i.CoverUrls,
            IsWide = !i.HasVerticalCover,
            SteamAppId = i.AppId,
            Description = i.Description,
        }).ToList();
    }

    private async Task<List<CoverCandidate>> SgdbCandidatesAsync(string query, CancellationToken ct)
    {
        var list = new List<CoverCandidate>();
        foreach (var game in (await _sgdb.SearchAsync(query, ct)).Take(2))
        {
            var grids = await _sgdb.GridsAsync(game.Id, ct);
            list.AddRange(grids.Take(6).Select(g => new CoverCandidate
            {
                Title = game.Name,
                Source = "SteamGridDB",
                ThumbUrl = g.Thumb,
                CoverUrls = new List<string> { g.Url },
            }));
        }
        return list;
    }

    private async Task<List<CoverCandidate>> WikiCandidatesAsync(string query, CancellationToken ct)
    {
        var hits = await _wiki.SearchAsync(query, ct);
        return hits.Where(h => h.ImageUrl != null && h.LooksLikeGame && NameMatcher.Similarity(query, h.CleanTitle) >= 0.45)
                   .Take(5)
                   .Select(h => new CoverCandidate
                   {
                       Title = h.CleanTitle,
                       Source = "Wikipedia",
                       Subtitle = h.Description,
                       ThumbUrl = h.ImageUrl!,
                       CoverUrls = new List<string> { h.ImageUrl! },
                       IsWide = h.Height > 0 && (double)h.Width / h.Height > 0.8,
                       Description = h.Extract,
                   }).ToList();
    }

    /// <summary>Applies a cover picked in the search dialog (optionally with the matching game info).</summary>
    public async Task ApplyCandidateAsync(Game game, CoverCandidate candidate, bool updateInfo, CancellationToken ct = default)
    {
        if (updateInfo && candidate.SteamAppId is int appId)
        {
            var item = (await _steam.GetItemsAsync(new[] { appId }, ct)).FirstOrDefault();
            if (item != null)
            {
                var r = await FromSteamAsync(item, 1, ct);
                r.CoverUrls = candidate.CoverUrls.ToList();
                await ApplyResultAsync(game, r, applyName: true, applyCover: true, manual: true);
                await _ui.InvokeAsync(() => game.NameLocked = true);
                return;
            }
        }

        if (!updateInfo)
        {
            var cover = await DownloadCoverAsync(game.Id, candidate.CoverUrls);
            if (cover == null) throw new InvalidOperationException("Impossible de télécharger cette image.");
            await _ui.InvokeAsync(() => SetCover(game, cover, custom: true));
            return;
        }

        var result = new MetadataResult
        {
            Name = candidate.Title,
            Source = candidate.Source,
            Description = candidate.Description,
            CoverUrls = candidate.CoverUrls.ToList(),
        };
        await ApplyResultAsync(game, result, applyName: true, applyCover: true, manual: true);
        await _ui.InvokeAsync(() =>
        {
            game.NameLocked = true;
            // Artwork from a previous (possibly wrong) Steam match no longer applies.
            ReplaceManaged(game, nameof(Game.Hero), null);
            ReplaceManaged(game, nameof(Game.Logo), null);
            game.SteamAppId = null;
        });
    }

    /// <summary>Downloads a candidate cover to the temp folder (used by the edit dialog before saving).</summary>
    public async Task<string?> DownloadCandidateToTempAsync(CoverCandidate candidate)
    {
        foreach (var url in candidate.CoverUrls)
        {
            var bytes = await PrepareCoverBytesAsync(await TryGetImageBytesAsync(url));
            if (bytes == null) continue;
            var path = Path.Combine(AppPaths.TempDir, $"{Guid.NewGuid():N}{ImageUtil.DetectExtension(bytes)}");
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        return null;
    }

    /// <summary>Copies a user image into the cache and makes it the game's cover (UI thread).</summary>
    public async Task SetCustomCoverFromFileAsync(Game game, string file)
    {
        var bytes = await PrepareCoverBytesAsync(await File.ReadAllBytesAsync(file))
                    ?? throw new InvalidOperationException("Ce fichier n'est pas une image valide.");
        var rel = Path.Combine("images", $"{game.Id}_cover_{DateTime.UtcNow.Ticks:x}{ImageUtil.DetectExtension(bytes)}");
        await File.WriteAllBytesAsync(AppPaths.Resolve(rel)!, bytes);
        SetCover(game, rel, custom: true);
        game.AccentColor = await Task.Run(() => ColorOf(rel));
        if (game.MetadataState is MetadataState.NotFound or MetadataState.Pending) game.MetadataState = MetadataState.Manual;
    }

    // ------------------------------------------------------------------------------------
    // Applying & downloading
    // ------------------------------------------------------------------------------------

    public async Task ApplyResultAsync(Game game, MetadataResult r, bool applyName, bool applyCover, bool manual)
    {
        var cover = applyCover && r.CoverUrls.Count > 0 ? await DownloadCoverAsync(game.Id, r.CoverUrls) : null;
        var hero = r.HeroUrls.Count > 0 ? await DownloadFirstAsync(game.Id, "hero", r.HeroUrls) : null;
        var logo = r.LogoUrls.Count > 0 ? await DownloadFirstAsync(game.Id, "logo", r.LogoUrls) : null;
        var coverColor = cover != null ? await Task.Run(() => ColorOf(cover)) : null;

        await _ui.InvokeAsync(() =>
        {
            if (applyName && !r.KeepName && !string.IsNullOrWhiteSpace(r.Name)) game.Name = NameCleaner.CleanOfficialName(r.Name);
            if (cover != null) SetCover(game, cover, manual);
            if (coverColor != null) game.AccentColor = coverColor;
            if (r.Screenshots != null) game.Screenshots = r.Screenshots;
            if (r.Trailers != null) game.Trailers = r.Trailers;
            if (hero != null) ReplaceManaged(game, nameof(Game.Hero), hero);
            if (logo != null) ReplaceManaged(game, nameof(Game.Logo), logo);
            if (!string.IsNullOrWhiteSpace(r.Description)) game.Description = r.Description;
            if (!string.IsNullOrWhiteSpace(r.Developer)) game.Developer = r.Developer;
            if (!string.IsNullOrWhiteSpace(r.Publisher)) game.Publisher = r.Publisher;
            if (!string.IsNullOrWhiteSpace(r.Genres)) game.Genres = r.Genres;
            if (r.ReleaseYear != null) game.ReleaseYear = r.ReleaseYear;
            if (r.SteamAppId != null) game.SteamAppId = r.SteamAppId;
            game.MetadataSource = r.Source;
            game.MetadataState = manual ? MetadataState.Manual : MetadataState.Matched;
        });
    }

    private static string? ColorOf(string relativeCover)
    {
        var c = ColorExtractor.FromFile(AppPaths.Resolve(relativeCover));
        return c is { } color ? ColorExtractor.ToHex(color) : null;
    }

    /// <summary>Computes missing cover colours (libraries created before this feature).</summary>
    public async Task FillMissingColorsAsync(IEnumerable<Game> games)
    {
        foreach (var g in games.Where(g => g.AccentColor == null && g.Cover != null).ToList())
        {
            var hex = await Task.Run(() => ColorOf(g.Cover!));
            if (hex != null) await _ui.InvokeAsync(() => g.AccentColor = hex);
        }
    }

    /// <summary>Fetches Steam screenshots once for a game opened in the detail page.</summary>
    public async Task EnsureScreenshotsAsync(Game game)
    {
        if (game.Screenshots != null && game.Trailers != null || game.SteamAppId is not int appId) return;
        try
        {
            var details = await _steam.GetDetailsAsync(appId);
            await _ui.InvokeAsync(() =>
            {
                // an empty answer while offline must not prevent a later retry
                if (details.Screenshots.Count > 0 || details.Genres != null) game.Screenshots = details.Screenshots;
                if (details.Trailers != null) game.Trailers = details.Trailers;
                if (game.Genres == null && details.Genres != null) game.Genres = details.Genres;
            });
        }
        catch (Exception ex) { Log.Warn($"Screenshots unavailable for '{game.Name}': {ex.Message}"); }
    }

    private static void SetCover(Game game, string relative, bool custom)
    {
        ReplaceManaged(game, nameof(Game.Cover), relative);
        game.CoverGenerated = false;
        if (custom) game.CustomCover = true;
    }

    private static void ReplaceManaged(Game game, string property, string? value)
    {
        string? old;
        switch (property)
        {
            case nameof(Game.Cover): old = game.Cover; game.Cover = value; break;
            case nameof(Game.Hero): old = game.Hero; game.Hero = value; break;
            case nameof(Game.Logo): old = game.Logo; game.Logo = value; break;
            default: return;
        }
        if (old != null && old != value) AppPaths.TryDeleteManaged(old);
    }

    private async Task<string?> DownloadCoverAsync(string gameId, IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            var bytes = await PrepareCoverBytesAsync(await TryGetImageBytesAsync(url));
            if (bytes == null) continue;
            var rel = Path.Combine("images", $"{gameId}_cover_{DateTime.UtcNow.Ticks:x}{ImageUtil.DetectExtension(bytes)}");
            await File.WriteAllBytesAsync(AppPaths.Resolve(rel)!, bytes);
            return rel;
        }
        return null;
    }

    /// <summary>Validates an image and converts landscape artwork into a portrait cover.</summary>
    private async Task<byte[]?> PrepareCoverBytesAsync(byte[]? bytes)
    {
        if (bytes == null) return null;
        var (w, h) = ImageUtil.GetSize(bytes);
        if (w < 32 || h < 32) return null;
        if ((double)w / h > 0.8)
        {
            try { return await _ui.InvokeAsync(() => ImageUtil.ComposePortrait(bytes)); }
            catch (Exception ex) { Log.Warn($"Portrait composition failed: {ex.Message}"); }
        }
        return bytes;
    }

    private static async Task<string?> DownloadFirstAsync(string gameId, string kind, IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            var bytes = await TryGetImageBytesAsync(url);
            if (bytes == null || ImageUtil.GetSize(bytes).Width == 0) continue;
            var rel = Path.Combine("images", $"{gameId}_{kind}_{DateTime.UtcNow.Ticks:x}{ImageUtil.DetectExtension(bytes)}");
            await File.WriteAllBytesAsync(AppPaths.Resolve(rel)!, bytes);
            return rel;
        }
        return null;
    }

    private static async Task<byte[]?> TryGetImageBytesAsync(string url)
    {
        try
        {
            using var resp = await Http.Client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var type = resp.Content.Headers.ContentType?.MediaType;
            if (type != null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            return bytes.Length > 512 ? bytes : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"Download failed {url}: {ex.Message}");
            return null;
        }
    }
}
