using System.Text.Json;
using Arcadia.Models;
using Arcadia.Services.Online;

namespace Arcadia.Services;

/// <summary>A game of a launcher's library that is not installed on this PC (with the play time the launcher kept).</summary>
public sealed record OwnedGame(GameStore Store, string Name, string LaunchTarget, int? SteamAppId, string? StoreId,
                               long Seconds, DateTime? LastPlayed)
{
    public string Key => $"{Store}:{StoreId ?? SteamAppId?.ToString()}".ToLowerInvariant();
}

/// <summary>
/// The games the player owns but has not installed, read from what the launchers keep on the PC (read-only, no
/// login, nothing online):
/// <list type="bullet">
/// <item>Steam: the library of the last logged-in account (userdata\&lt;id&gt;\config\librarycache, family library
/// included) and every game it ever played (localconfig.vdf, with the hours); names and types from appinfo.vdf;</item>
/// <item>Epic Games (new launcher): cache\prod\account\&lt;id&gt;\library-cache.json, with the hours of playtime-cache.json.</item>
/// </list>
/// Xbox, EA, Ubisoft, Battle.net and Riot keep the list of owned games online only.
/// </summary>
public static class OwnedGames
{
    public static List<OwnedGame> ReadAll()
    {
        var all = new List<OwnedGame>();
        try { all.AddRange(ReadSteam()); } catch (Exception ex) { Log.Warn($"Steam library unreadable: {ex.Message}"); }
        try { all.AddRange(ReadEpic()); } catch (Exception ex) { Log.Warn($"Epic library unreadable: {ex.Message}"); }
        Log.Info($"Owned games not installed: {all.Count(g => g.Store == GameStore.Steam)} Steam, {all.Count(g => g.Store == GameStore.Epic)} Epic");
        return all;
    }

    // ------------------------------------------------------------------------------------
    // Steam
    // ------------------------------------------------------------------------------------

    /// <summary>Apps installed in one of the Steam libraries (appmanifest_&lt;id&gt;.acf).</summary>
    public static HashSet<int> InstalledSteamApps()
    {
        var set = new HashSet<int>();
        foreach (var steamapps in StoreScanner.SteamLibraries())
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(acf)["appmanifest_".Length..], out var id)) set.Add(id);
        return set;
    }

    public static List<OwnedGame> ReadSteam()
    {
        var root = StoreScanner.SteamRoot();
        if (root == null) return new List<OwnedGame>();
        var account = SteamPlaytime.CurrentAccountId(root);
        var usage = SteamPlaytime.Read();
        var ids = new HashSet<int>(usage.Keys);
        if (account != null)
        {
            var cache = Path.Combine(root, "userdata", account, "config", "librarycache");
            if (Directory.Exists(cache))
            {
                foreach (var f in Directory.EnumerateFiles(cache, "*.json"))
                    if (int.TryParse(Path.GetFileNameWithoutExtension(f), out var id)) ids.Add(id);
                var progress = Path.Combine(cache, "achievement_progress.json");
                if (File.Exists(progress)) ids.UnionWith(ParseAchievementProgress(File.ReadAllText(progress)));
            }
        }
        var installed = InstalledSteamApps();
        ids.ExceptWith(installed);
        ids.RemoveWhere(id => id <= 0 || StoreScanner.IsSteamTool(id));
        var info = SteamAppInfo.Read(ids);
        return Select(ids, info, usage);
    }

    /// <summary>Keeps the games (not tools, demos, soundtracks…) the cache knows the name of.</summary>
    internal static List<OwnedGame> Select(IEnumerable<int> ids, IReadOnlyDictionary<int, SteamAppSummary> info, IReadOnlyDictionary<int, SteamAppUsage> usage)
    {
        var list = new List<OwnedGame>();
        foreach (var id in ids)
        {
            if (!info.TryGetValue(id, out var app) || app.Type != "game" || string.IsNullOrWhiteSpace(app.Name)) continue;
            if (StoreScanner.IsSteamNonGameName(app.Name)) continue;
            usage.TryGetValue(id, out var u);
            list.Add(new OwnedGame(GameStore.Steam, NameCleaner.CleanOfficialName(app.Name), $"steam://rungameid/{id}", id, id.ToString(),
                u?.Seconds ?? 0, u?.LastPlayed));
        }
        return list;
    }

    internal static IEnumerable<int> ParseAchievementProgress(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var pair in doc.RootElement.Arr("mapCache"))
            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() > 0 && pair[0].TryGetInt32(out var id))
                yield return id;
    }

    // ------------------------------------------------------------------------------------
    // Epic Games
    // ------------------------------------------------------------------------------------

    public static List<OwnedGame> ReadEpic()
    {
        var accounts = Path.Combine(EpicLauncher.NewDataDir, "cache", "prod", "account");
        if (!Directory.Exists(accounts)) return new List<OwnedGame>();
        var catalogFile = Path.Combine(EpicLauncher.NewDataDir, "cache", "prod", "catalog-cache.json");
        var catalog = File.Exists(catalogFile) ? File.ReadAllText(catalogFile) : null;
        var playtime = EpicLauncher.ReadPlaytime();
        var installed = StoreScanner.ScanEpicInstalled().Select(g => g.StoreId).Where(s => s != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, OwnedGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(accounts, "library-cache.json", SearchOption.AllDirectories))
            foreach (var g in ParseEpicLibrary(File.ReadAllText(file), catalog, playtime))
                if (!installed.Contains(g.StoreId!)) result.TryAdd(g.StoreId!, g);
        return result.Values.ToList();
    }

    /// <summary>The owned games of library-cache.json (add-ons, Unreal Engine content and tools left out).</summary>
    internal static List<OwnedGame> ParseEpicLibrary(string libraryJson, string? catalogJson, IReadOnlyDictionary<string, long> playtime)
    {
        JsonDocument? catalog = null;
        try { if (catalogJson != null) catalog = JsonDocument.Parse(catalogJson); } catch (JsonException) { }
        using var cat = catalog;
        var items = cat?.RootElement.Obj("items") ?? default;

        var list = new List<OwnedGame>();
        using var doc = JsonDocument.Parse(libraryJson);
        foreach (var e in doc.RootElement.Arr("items"))
        {
            if (e.TryGetProperty("owned", out var owned) && owned.ValueKind == JsonValueKind.False) continue;
            if (e.Str("recordType") is { } type && type != "APPLICATION") continue;
            var id = e.Obj("appId");
            var ns = id.Str("namespaceId");
            var item = id.Str("itemId");
            var app = id.Str("appName");
            var title = e.Str("title");
            if (app == null || string.IsNullOrWhiteSpace(title)) continue;
            if (StoreScanner.IsEpicTool(new EpicInstall("", ns, item, app, null, title))) continue;
            if (item != null && items.ValueKind == JsonValueKind.Object && items.TryGetProperty(item, out var entry))
            {
                var info = entry.Obj("item");
                var mainGame = info.Obj("mainGameItem");
                if (mainGame.ValueKind == JsonValueKind.Object) continue; // DLC / add-on
                var categories = info.Arr("categories").Select(c => c.Str("path")).ToList();
                if (categories.Count > 0 && !categories.Contains("games")) continue;
            }
            playtime.TryGetValue(app, out var seconds);
            list.Add(new OwnedGame(GameStore.Epic, NameCleaner.CleanOfficialName(title), EpicLauncher.LaunchUri(ns, item, app), null, app, seconds, null));
        }
        return list;
    }
}
