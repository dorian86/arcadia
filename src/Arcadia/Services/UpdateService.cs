using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Arcadia.Services.Online;

namespace Arcadia.Services;

/// <summary>A published version of Arcadia (a GitHub release with Arcadia.exe attached).</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string Title, string Notes, string? DownloadUrl, long Size, string PageUrl);

/// <summary>
/// Updates through GitHub releases. The code can live in a private repository: Arcadia only reads the
/// public "releases" repository named in Arcadia.csproj (UpdateRepository), which holds the published
/// executables and their notes, nothing else. No account or token is needed, so nothing secret ships in the exe.
/// </summary>
public static class UpdateService
{
    public const string AssetName = "Arcadia.exe";

    /// <summary>"owner/repository" of the public releases repository, or null when not configured yet.</summary>
    public static string? Repository
    {
        get
        {
            var value = typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value?.Trim();
            return string.IsNullOrEmpty(value) || value.Contains("VOTRE-PSEUDO", StringComparison.OrdinalIgnoreCase) || !value.Contains('/') ? null : value;
        }
    }

    public static Version Current
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Only the published single-file exe can replace itself (not a development build).</summary>
#pragma warning disable IL3000 // empty Location is exactly how a single-file publish is recognised
    public static bool IsPublishedBuild => string.IsNullOrEmpty(typeof(UpdateService).Assembly.Location) && Environment.ProcessPath != null;
#pragma warning restore IL3000

    /// <summary>Test hook: ARCADIA_UPDATE_FEED = path of a release JSON file (end-to-end tests without GitHub).</summary>
    private static string? TestFeed => Environment.GetEnvironmentVariable("ARCADIA_UPDATE_FEED");

    public static bool IsConfigured => Repository != null || TestFeed != null;

    public static string? ReleasesPage => Repository is { } r ? $"https://github.com/{r}/releases" : null;

    // ------------------------------------------------------------------------------------
    // Check
    // ------------------------------------------------------------------------------------

    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        if (TestFeed is { } feed) return ParseRelease(await File.ReadAllTextAsync(feed, ct));
        if (Repository is not { } repo) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.Client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no release published yet
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Reads the GitHub "release" JSON. Tags look like "v1.2.0" (or "1.2").</summary>
    internal static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        var tag = root.Str("tag_name");
        if (tag == null || !TryParseVersion(tag, out var version)) return null;
        string? url = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (!string.Equals(a.Str("name"), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                url = a.Str("browser_download_url");
                size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
            }
        }
        return new ReleaseInfo(version, tag, root.Str("name") ?? tag, root.Str("body") ?? "", url, size, root.Str("html_url") ?? "");
    }

    internal static bool TryParseVersion(string tag, out Version version)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(new[] { '-', '+', ' ' });
        if (dash > 0) text = text[..dash];
        if (Version.TryParse(text.Contains('.') ? text : text + ".0", out var v))
        {
            version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    public static bool IsNewer(ReleaseInfo release) => release.Version > Current;

    // ------------------------------------------------------------------------------------
    // Install
    // ------------------------------------------------------------------------------------

    private static string UpdateFolder => Path.Combine(Path.GetTempPath(), "Arcadia-update");

    public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        if (release.DownloadUrl == null) throw new InvalidOperationException("cette version ne contient pas Arcadia.exe");
        Directory.CreateDirectory(UpdateFolder);
        var target = Path.Combine(UpdateFolder, $"Arcadia-{release.Version}.exe");
        var temp = target + ".part";
        if (File.Exists(release.DownloadUrl)) File.Copy(release.DownloadUrl, temp, overwrite: true); // test feed
        else
        using (var response = await Http.Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(temp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        // a Windows program starts with "MZ" and the size must match what GitHub announced
        var info = new FileInfo(temp);
        var header = new byte[2];
        await using (var check = File.OpenRead(temp)) _ = await check.ReadAsync(header, ct);
        if (header[0] != 'M' || header[1] != 'Z' || info.Length < 1_000_000 || (release.Size > 0 && info.Length != release.Size))
        {
            File.Delete(temp);
            throw new InvalidDataException("le fichier téléchargé est incomplet ou invalide");
        }
        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Swaps the running exe for the new one and starts it. Windows lets a running exe be renamed (not
    /// overwritten): the current file becomes "Arcadia.exe.old" and is deleted by the new version.
    /// Returns false (nothing changed) when the folder cannot be written to.
    /// </summary>
    public static bool InstallAndRestart(string newExe)
    {
        var current = Environment.ProcessPath;
        if (current == null || !IsPublishedBuild) return false;
        var old = current + ".old";
        try
        {
            if (File.Exists(old)) File.Delete(old);
            File.Move(current, old);
        }
        catch (Exception ex)
        {
            Log.Warn($"Update: cannot replace {current}: {ex.Message}");
            return false;
        }
        try
        {
            File.Copy(newExe, current, overwrite: true);
            Process.Start(new ProcessStartInfo(current, $"--updated {Environment.ProcessId}") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(current)! });
            Log.Info($"Update: installed {newExe} as {current}, restarting");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Update: install failed, restoring the previous version: {ex.Message}");
            try { if (File.Exists(current)) File.Delete(current); File.Move(old, current); } catch { /* best effort */ }
            return false;
        }
    }

    /// <summary>Started by the update: wait for the old process to quit, then clean up behind it.</summary>
    public static void FinishUpdate(int previousProcessId)
    {
        try { using var p = Process.GetProcessById(previousProcessId); p.WaitForExit(20_000); }
        catch { /* already gone */ }
        if (Environment.ProcessPath is { } current)
        {
            for (var i = 0; i < 10; i++)
            {
                try { if (File.Exists(current + ".old")) File.Delete(current + ".old"); break; }
                catch { Thread.Sleep(300); }
            }
        }
        try { if (Directory.Exists(UpdateFolder)) Directory.Delete(UpdateFolder, true); } catch { /* in use */ }
    }
}
