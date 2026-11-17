using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Arcadia.Models;
using Arcadia.Services.Online;

namespace Arcadia.Services;

/// <summary>
/// Steam serves trailers as DASH streams only. Windows' media stack cannot play DASH manifests,
/// but the segments are fragmented MP4: downloading the init segment + the first media segments of the
/// 480p video track and concatenating them gives a regular file the WPF MediaElement can play (video only).
/// </summary>
public static class TrailerService
{
    private const int MaxSeconds = 60;
    private const int MaxCachedFiles = 30;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string TrailersDir => Path.Combine(AppPaths.ImagesDir, "trailers");

    public static async Task<string?> GetVideoFileAsync(Trailer trailer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trailer.Dash)) return null;
        Directory.CreateDirectory(TrailersDir);
        var file = Path.Combine(TrailersDir, $"{trailer.Id}.mp4");
        if (File.Exists(file))
        {
            File.SetLastAccessTime(file, DateTime.Now);
            return file;
        }

        await Gate.WaitAsync(ct);
        try
        {
            if (File.Exists(file)) return file;
            var mpdText = await Http.Client.GetStringAsync(trailer.Dash, ct);
            var plan = Plan(mpdText, trailer.Dash);
            if (plan == null) return null;

            var tmp = file + ".part";
            await using (var output = File.Create(tmp))
            {
                foreach (var url in plan)
                {
                    ct.ThrowIfCancellationRequested();
                    using var resp = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode) break; // end of the stream reached early
                    await using var s = await resp.Content.ReadAsStreamAsync(ct);
                    await s.CopyToAsync(output, ct);
                }
            }
            if (new FileInfo(tmp).Length < 50_000) { File.Delete(tmp); return null; }
            File.Move(tmp, file, true);
            Prune();
            Log.Info($"Trailer cached: {trailer.Name} ({new FileInfo(file).Length / 1024} KB)");
            return file;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warn($"Trailer download failed ({trailer.Name}): {ex.Message}");
            return null;
        }
        finally { Gate.Release(); }
    }

    /// <summary>URLs to download: init segment + media segments of the ~480p video representation.</summary>
    internal static List<string>? Plan(string mpdText, string mpdUrl)
    {
        var doc = XDocument.Parse(mpdText);
        XNamespace ns = doc.Root!.Name.Namespace;
        var duration = ParseDuration(doc.Root.Attribute("mediaPresentationDuration")?.Value) ?? MaxSeconds;
        var video = doc.Descendants(ns + "AdaptationSet").FirstOrDefault(a => (string?)a.Attribute("contentType") == "video");
        if (video == null) return null;

        var reps = video.Elements(ns + "Representation")
            .Select(r => (Rep: r, Height: (int?)r.Attribute("height") ?? 0))
            .Where(r => r.Height > 0).OrderBy(r => r.Height).ToList();
        if (reps.Count == 0) return null;
        var chosen = reps.FirstOrDefault(r => r.Height >= 480).Rep ?? reps[^1].Rep;

        var template = chosen.Element(ns + "SegmentTemplate") ?? video.Element(ns + "SegmentTemplate");
        if (template == null) return null;
        var id = (string?)chosen.Attribute("id") ?? "0";
        var timescale = (double?)template.Attribute("timescale") ?? 1;
        var segDuration = ((double?)template.Attribute("duration") ?? 3 * timescale) / timescale;
        var start = (int?)template.Attribute("startNumber") ?? 1;
        var init = (string?)template.Attribute("initialization");
        var media = (string?)template.Attribute("media");
        if (init == null || media == null) return null;

        var baseUri = new Uri(mpdUrl);
        string Resolve(string relative) => new Uri(baseUri, relative).ToString();
        var count = (int)Math.Ceiling(Math.Min(duration, MaxSeconds) / segDuration);

        var urls = new List<string> { Resolve(init.Replace("$RepresentationID$", id)) };
        for (var n = start; n < start + count; n++)
        {
            var path = Regex.Replace(media.Replace("$RepresentationID$", id), @"\$Number(%0(\d+)d)?\$", m =>
                m.Groups[2].Success ? n.ToString(CultureInfo.InvariantCulture).PadLeft(int.Parse(m.Groups[2].Value), '0') : n.ToString(CultureInfo.InvariantCulture));
            urls.Add(Resolve(path));
        }
        return urls;
    }

    private static double? ParseDuration(string? iso)
    {
        if (iso == null) return null;
        try { return System.Xml.XmlConvert.ToTimeSpan(iso).TotalSeconds; } catch { return null; }
    }

    private static void Prune()
    {
        try
        {
            foreach (var f in new DirectoryInfo(TrailersDir).GetFiles("*.mp4").OrderByDescending(f => f.LastAccessTime).Skip(MaxCachedFiles))
                f.Delete();
        }
        catch { /* best effort */ }
    }
}
