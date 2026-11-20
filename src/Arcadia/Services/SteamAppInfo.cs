using System.Text;

namespace Arcadia.Services;

/// <summary>Name and type ("game", "tool", "demo", "dlc"…) of a Steam application.</summary>
public sealed record SteamAppSummary(int AppId, string Name, string Type);

/// <summary>
/// Reads Steam's local cache of applications (appcache\appinfo.vdf, binary, read-only) to name the games of the
/// library without going online. Formats 0x07564428 (keys as strings) and 0x07564429 (keys in a string table).
/// </summary>
public static class SteamAppInfo
{
    private const uint V28 = 0x07564428, V29 = 0x07564429;

    public static Dictionary<int, SteamAppSummary> Read(IEnumerable<int> wanted)
    {
        var root = StoreScanner.SteamRoot();
        var file = root != null ? Path.Combine(root, "appcache", "appinfo.vdf") : null;
        if (file == null || !File.Exists(file)) return new Dictionary<int, SteamAppSummary>();
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[fs.Length];
        fs.ReadExactly(bytes);
        return Parse(bytes, wanted.ToHashSet());
    }

    internal static Dictionary<int, SteamAppSummary> Parse(byte[] b, HashSet<int> wanted)
    {
        var result = new Dictionary<int, SteamAppSummary>();
        if (b.Length < 16) return result;
        var magic = BitConverter.ToUInt32(b, 0);
        if (magic != V28 && magic != V29) return result;
        var pos = 8;
        string[]? table = null;
        if (magic == V29)
        {
            var tableAt = BitConverter.ToInt64(b, 8);
            pos = 16;
            if (tableAt <= 0 || tableAt >= b.Length) return result;
            table = ReadStringTable(b, (int)tableAt);
        }
        while (pos + 8 <= b.Length)
        {
            var appId = BitConverter.ToUInt32(b, pos);
            if (appId == 0) break;
            var size = (int)BitConverter.ToUInt32(b, pos + 4);
            var start = pos + 8;
            var next = start + size;
            if (size <= 0 || next > b.Length) break;
            if (wanted.Contains((int)appId))
            {
                try
                {
                    // infoState, lastUpdated, picsToken, sha1, changeNumber, binary sha1 = 60 bytes
                    var kvPos = start + 60;
                    var common = FindCommon(b, ref kvPos, next, table);
                    if (common is { } c && c.Name != null)
                        result[(int)appId] = new SteamAppSummary((int)appId, c.Name.Trim(), (c.Type ?? "").Trim().ToLowerInvariant());
                }
                catch { /* malformed entry */ }
            }
            pos = next;
        }
        return result;
    }

    private static string[] ReadStringTable(byte[] b, int at)
    {
        var count = (int)BitConverter.ToUInt32(b, at);
        var list = new string[Math.Max(0, count)];
        var p = at + 4;
        for (var i = 0; i < count && p < b.Length; i++)
        {
            var end = Array.IndexOf(b, (byte)0, p);
            if (end < 0) end = b.Length;
            list[i] = Encoding.UTF8.GetString(b, p, end - p);
            p = end + 1;
        }
        return list;
    }

    /// <summary>Walks the binary KeyValues tree and returns appinfo/common/name and type.</summary>
    private static (string? Name, string? Type)? FindCommon(byte[] b, ref int p, int end, string[]? table)
    {
        string? name = null, type = null;
        var path = new List<string>();
        while (p < end)
        {
            var kind = b[p++];
            if (kind == 8)
            {
                if (path.Count == 0) break;
                path.RemoveAt(path.Count - 1);
                continue;
            }
            var key = ReadKey(b, ref p, table);
            switch (kind)
            {
                case 0: path.Add(key); break;
                case 1:
                    var value = ReadCString(b, ref p);
                    if (path.Count == 2 && path[1] == "common")
                    {
                        if (key == "name") name = value;
                        else if (key == "type") type = value;
                        if (name != null && type != null) return (name, type);
                    }
                    break;
                case 2: case 3: case 4: case 6: p += 4; break;
                case 7: case 10: p += 8; break;
                case 5:
                    while (p + 1 < end && (b[p] != 0 || b[p + 1] != 0)) p += 2;
                    p += 2;
                    break;
                default: return name != null ? (name, type) : null; // unknown type: stop here
            }
        }
        return name != null ? (name, type) : null;
    }

    private static string ReadKey(byte[] b, ref int p, string[]? table)
    {
        if (table == null) return ReadCString(b, ref p);
        var index = (int)BitConverter.ToUInt32(b, p);
        p += 4;
        return index >= 0 && index < table.Length ? table[index] : "";
    }

    private static string ReadCString(byte[] b, ref int p)
    {
        var end = Array.IndexOf(b, (byte)0, p);
        if (end < 0) end = b.Length;
        var s = Encoding.UTF8.GetString(b, p, end - p);
        p = end + 1;
        return s;
    }
}
