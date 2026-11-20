using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Arcadia.Services;

/// <summary>Fuzzy comparison of game titles ("GTA V" ~ "Grand Theft Auto V", "Portal" != "Portal 2"...).</summary>
public static partial class NameMatcher
{
    private static readonly Dictionary<string, string> Roman = new()
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7",
        ["viii"] = "8", ["ix"] = "9", ["x"] = "10", ["xi"] = "11", ["xii"] = "12", ["xiii"] = "13",
        ["xiv"] = "14", ["xv"] = "15", ["xvi"] = "16",
    };

    private static readonly string[] NoiseWords =
    {
        "soundtrack", "ost", "dlc", "season pass", "artbook", "art book", "wallpaper", "playtest",
        "dedicated server", "sdk", "editor", "benchmark", "upgrade", "expansion pass", "costume", "skin pack",
        "bundle", "pack", "trailer", "beta", "test server", "tools",
    };

    public static string RemoveDiacritics(string s)
    {
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Keys are asked for the same names again and again (every refresh of a 1000-game library): remembered.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> MatchKeys = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> SearchKeys = new();

    private static string Remember(System.Collections.Concurrent.ConcurrentDictionary<string, string> cache, string s, Func<string, string> compute)
    {
        if (cache.TryGetValue(s, out var known)) return known;
        if (cache.Count > 50_000) cache.Clear();
        var value = compute(s);
        cache[s] = value;
        return value;
    }

    /// <summary>Lowercase, accent-free, punctuation-free, roman numerals as digits, no leading "the".</summary>
    public static string MatchKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return Remember(MatchKeys, s, ComputeMatchKey);
    }

    private static string ComputeMatchKey(string s)
    {
        var x = RemoveDiacritics(s.ToLowerInvariant()).Replace("&", " and ");
        x = NonAlnum().Replace(x, " ");
        var tokens = x.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .Select(t => Roman.TryGetValue(t, out var n) ? n : t)
                      .ToList();
        if (tokens.Count > 1 && tokens[0] == "the") tokens.RemoveAt(0);
        return string.Join(' ', tokens);
    }

    /// <summary>Key without spaces: "Geometry Dash" == "GeometryDash".</summary>
    public static string Compact(string? s) => MatchKey(s).Replace(" ", "");

    /// <summary>Key used by the search box (keeps every word, accent-insensitive).</summary>
    public static string SearchKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return Remember(SearchKeys, s, ComputeSearchKey);
    }

    private static string ComputeSearchKey(string s)
    {
        var x = RemoveDiacritics(s.ToLowerInvariant());
        return NonAlnum().Replace(x, " ").Trim();
    }

    public static double Similarity(string? a, string? b)
    {
        var ka = MatchKey(a);
        var kb = MatchKey(b);
        if (ka.Length == 0 || kb.Length == 0) return 0;
        if (ka == kb || ka.Replace(" ", "") == kb.Replace(" ", "")) return 1.0;

        var ta = ka.Split(' ');
        var tb = kb.Split(' ');
        var (shortT, longT) = ta.Length <= tb.Length ? (ta, tb) : (tb, ta);

        if (StartsWithTokens(longT, shortT))
        {
            var extra = longT.Skip(shortT.Length).ToArray();
            var extraHasNumber = extra.Any(IsNumber);
            var shortHasNumber = shortT.Any(IsNumber);
            if (extraHasNumber && !shortHasNumber) return 0.6;          // "Portal" vs "Portal 2": a sequel, not the same game
            if (shortT.Length >= 2) return 0.88; // "Getting Over It" vs "Getting Over It with Bennett Foddy"
            return 0.7;
        }

        if (shortT.Length >= 2 && ContainsTokens(longT, shortT) && !longT.Except(shortT).Any(IsNumber))
            return 0.8;                                                   // "Future Soldier" in "Ghost Recon Future Soldier"

        return Dice(ka.Replace(" ", ""), kb.Replace(" ", ""));
    }

    /// <summary>Penalises soundtracks, DLC, demos... unless the query asks for them.</summary>
    public static double ResultPenalty(string query, string resultName)
    {
        var q = " " + MatchKey(query) + " ";
        var r = " " + MatchKey(resultName) + " ";
        foreach (var w in NoiseWords)
        {
            var token = " " + w + " ";
            if (r.Contains(token) && !q.Contains(token)) return 0.5;
        }
        if (r.Contains(" demo ") && !q.Contains(" demo ")) return 0.85;
        return 1.0;
    }

    private static bool IsNumber(string t) => t.Length > 0 && t.All(char.IsDigit);

    private static bool StartsWithTokens(string[] longT, string[] shortT)
    {
        if (shortT.Length > longT.Length) return false;
        for (var i = 0; i < shortT.Length; i++)
            if (longT[i] != shortT[i]) return false;
        return true;
    }

    private static bool ContainsTokens(string[] longT, string[] shortT)
    {
        for (var start = 0; start + shortT.Length <= longT.Length; start++)
        {
            var ok = true;
            for (var i = 0; i < shortT.Length && ok; i++) ok = longT[start + i] == shortT[i];
            if (ok) return true;
        }
        return false;
    }

    private static double Dice(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return a == b ? 1 : 0;
        var bigrams = new Dictionary<string, int>();
        for (var i = 0; i < a.Length - 1; i++)
        {
            var bg = a.Substring(i, 2);
            bigrams[bg] = bigrams.TryGetValue(bg, out var n) ? n + 1 : 1;
        }
        var matches = 0;
        for (var i = 0; i < b.Length - 1; i++)
        {
            var bg = b.Substring(i, 2);
            if (bigrams.TryGetValue(bg, out var n) && n > 0)
            {
                matches++;
                bigrams[bg] = n - 1;
            }
        }
        return 2.0 * matches / (a.Length - 1 + b.Length - 1);
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}
