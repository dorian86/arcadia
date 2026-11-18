using System.Text;

namespace Arcadia.Services;

/// <summary>
/// Achievements the player unlocked in a Steam game, read from the Steam client's own cache on this PC
/// (Steam\appcache\stats): works offline, without an API key and without a public profile.
/// <list type="bullet">
/// <item><c>UserGameStatsSchema_&lt;app&gt;.bin</c>: each achievement is one bit of a stat ("stats/&lt;stat&gt;/bits/&lt;bit&gt;/name");</item>
/// <item><c>UserGameStats_&lt;account&gt;_&lt;app&gt;.bin</c>: per stat, the bit field of what is unlocked ("data")
/// and the unlock time of each bit ("AchievementTimes").</item>
/// </list>
/// Both are binary KeyValues files. Never written, only read.
/// </summary>
public static class SteamLocalAchievements
{
    /// <summary>Unlocked achievements (Steam internal name → unlock time), or null when Steam has no data for this game.</summary>
    public static Dictionary<string, DateTime>? ReadUnlocked(int appId)
    {
        try
        {
            var root = StoreScanner.SteamRoot();
            if (root == null) return null;
            var dir = Path.Combine(root, "appcache", "stats");
            var schemaFile = Path.Combine(dir, $"UserGameStatsSchema_{appId}.bin");
            if (!File.Exists(schemaFile)) return null;

            // the account that logged in last, else whichever account played this game most recently
            var account = SteamPlaytime.CurrentAccountId(root);
            var userFile = account != null ? Path.Combine(dir, $"UserGameStats_{account}_{appId}.bin") : null;
            if (userFile == null || !File.Exists(userFile))
                userFile = Directory.EnumerateFiles(dir, $"UserGameStats_*_{appId}.bin").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (userFile == null) return new Dictionary<string, DateTime>(); // never played on this PC: nothing unlocked

            return Combine(Kv.Read(File.ReadAllBytes(schemaFile)), Kv.Read(File.ReadAllBytes(userFile)));
        }
        catch (Exception ex)
        {
            Log.Warn($"Steam achievements of app {appId} unreadable: {ex.Message}");
            return null;
        }
    }

    /// <summary>Maps the unlocked bits of the user file to the achievement names of the schema.</summary>
    public static Dictionary<string, DateTime> Combine(Kv schema, Kv user)
    {
        var result = new Dictionary<string, DateTime>();
        var stats = schema.Children.Values.FirstOrDefault()?.Child("stats"); // root key = app id
        var cache = user.Child("cache");
        if (stats == null || cache == null) return result;

        foreach (var (statId, stat) in stats.Children)
        {
            var bits = stat.Child("bits");
            var unlocked = cache.Child(statId);
            if (bits == null || unlocked == null) continue;
            var data = unlocked.Int("data") ?? 0;
            var times = unlocked.Child("AchievementTimes");
            foreach (var (bitKey, bit) in bits.Children)
            {
                if (!int.TryParse(bitKey, out var b) || b is < 0 or > 31 || (data & (1 << b)) == 0) continue;
                var name = bit.Str("name");
                if (string.IsNullOrEmpty(name)) continue;
                var unix = times?.Int(bitKey) ?? 0;
                result[name] = unix > 0 ? DateTimeOffset.FromUnixTimeSeconds((uint)unix).LocalDateTime : DateTime.MinValue;
            }
        }
        return result;
    }
}

/// <summary>Node of a Valve binary KeyValues file (only what the stats files use).</summary>
public sealed class Kv
{
    public Dictionary<string, Kv> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public object? Value { get; private set; }

    public Kv? Child(string key) => Children.TryGetValue(key, out var c) ? c : null;
    public string? Str(string key) => Child(key)?.Value as string;
    public int? Int(string key) => Child(key)?.Value switch { int i => i, ulong u => (int)u, _ => null };

    public static Kv Read(byte[] bytes)
    {
        var root = new Kv();
        var pos = 0;
        ReadInto(root, bytes, ref pos, 0);
        return root;
    }

    private static void ReadInto(Kv node, byte[] b, ref int pos, int depth)
    {
        if (depth > 32) throw new InvalidDataException("KeyValues nested too deeply");
        while (pos < b.Length)
        {
            var type = b[pos++];
            if (type is 0x08 or 0x0B) return; // end of object
            var key = ReadString(b, ref pos);
            var child = new Kv();
            switch (type)
            {
                case 0x00: ReadInto(child, b, ref pos, depth + 1); break;
                case 0x01: child.Value = ReadString(b, ref pos); break;
                case 0x02: child.Value = BitConverter.ToInt32(b, pos); pos += 4; break;
                case 0x03: child.Value = BitConverter.ToSingle(b, pos); pos += 4; break;
                case 0x04: case 0x06: child.Value = BitConverter.ToInt32(b, pos); pos += 4; break; // pointer, colour
                case 0x07: child.Value = BitConverter.ToUInt64(b, pos); pos += 8; break;
                case 0x0A: child.Value = BitConverter.ToInt64(b, pos); pos += 8; break;
                default: throw new InvalidDataException($"Unknown KeyValues type {type}");
            }
            node.Children[key] = child;
        }
    }

    private static string ReadString(byte[] b, ref int pos)
    {
        var end = Array.IndexOf(b, (byte)0, pos);
        if (end < 0) throw new InvalidDataException("Unterminated KeyValues string");
        var s = Encoding.UTF8.GetString(b, pos, end - pos);
        pos = end + 1;
        return s;
    }
}
