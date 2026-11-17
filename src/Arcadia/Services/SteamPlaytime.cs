using System.Text;

namespace Arcadia.Services;

/// <summary>Minimal reader for Valve's text KeyValues (.vdf / .acf) format.</summary>
public static class Vdf
{
    public sealed class Node
    {
        public Dictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Node? Child(string key) => Children.TryGetValue(key, out var n) ? n : null;
        public string? Value(string key) => Values.TryGetValue(key, out var v) ? v : null;
    }

    public static Node Parse(string text)
    {
        var pos = 0;
        var root = new Node();
        ParseInto(root, text, ref pos);
        return root;
    }

    private static void ParseInto(Node node, string s, ref int pos)
    {
        while (true)
        {
            var key = NextToken(s, ref pos);
            if (key == null || key == "}") return;
            var value = NextToken(s, ref pos);
            if (value == null) return;
            if (value == "{")
            {
                var child = new Node();
                ParseInto(child, s, ref pos);
                node.Children[key] = child;
            }
            else node.Values[key] = value;
        }
    }

    private static string? NextToken(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            var c = s[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            if (c is '{' or '}') { pos++; return c.ToString(); }
            if (c == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < s.Length && s[pos] != '"')
                {
                    if (s[pos] == '\\' && pos + 1 < s.Length) { sb.Append(s[pos + 1]); pos += 2; continue; }
                    sb.Append(s[pos++]);
                }
                pos++;
                return sb.ToString();
            }
            var start = pos;
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] is not ('{' or '}' or '"')) pos++;
            return s[start..pos];
        }
        return null;
    }
}

public sealed record SteamAppUsage(long Seconds, DateTime? LastPlayed);

/// <summary>
/// Reads the play time Steam already recorded locally (userdata\&lt;account&gt;\config\localconfig.vdf)
/// for the account that logged in last.
/// </summary>
public static class SteamPlaytime
{
    private const long SteamId64Base = 76561197960265728;

    public static Dictionary<int, SteamAppUsage> Read()
    {
        var result = new Dictionary<int, SteamAppUsage>();
        var root = StoreScanner.SteamRoot();
        if (root == null) return result;
        var file = LocalConfigFile(root);
        if (file == null) return result;

        var apps = Vdf.Parse(File.ReadAllText(file))
            .Child("UserLocalConfigStore")?.Child("Software")?.Child("Valve")?.Child("Steam")?.Child("apps");
        if (apps == null) return result;

        foreach (var (key, node) in apps.Children)
        {
            if (!int.TryParse(key, out var appId)) continue;
            long.TryParse(node.Value("Playtime"), out var minutes);
            DateTime? last = null;
            if (long.TryParse(node.Value("LastPlayed"), out var unix) && unix > 0)
                last = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
            if (minutes > 0 || last != null) result[appId] = new SteamAppUsage(minutes * 60, last);
        }
        Log.Info($"Steam play time read from {file}: {result.Count} app(s)");
        return result;
    }

    private static string? LocalConfigFile(string steamRoot)
    {
        try
        {
            // Account flagged "MostRecent" in loginusers.vdf, else the most recently written config
            var login = Path.Combine(steamRoot, "config", "loginusers.vdf");
            if (File.Exists(login))
            {
                var users = Vdf.Parse(File.ReadAllText(login)).Child("users");
                var recent = users?.Children.FirstOrDefault(u => u.Value.Value("MostRecent") == "1");
                if (recent?.Key is { } id64 && long.TryParse(id64, out var sid))
                {
                    var f = Path.Combine(steamRoot, "userdata", (sid - SteamId64Base).ToString(), "config", "localconfig.vdf");
                    if (File.Exists(f)) return f;
                }
            }
            var userdata = Path.Combine(steamRoot, "userdata");
            if (!Directory.Exists(userdata)) return null;
            return Directory.EnumerateDirectories(userdata)
                .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Warn($"Steam localconfig lookup failed: {ex.Message}");
            return null;
        }
    }
}
