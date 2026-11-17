using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcadia.Services;

/// <summary>Crash-safe JSON persistence: write to a temp file, then atomically replace (keeping a .bak).</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Read<T>(string path) where T : class
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                using var stream = File.OpenRead(candidate);
                var value = JsonSerializer.Deserialize<T>(stream, Options);
                if (value != null)
                {
                    if (candidate != path) Log.Warn($"{path} unreadable, restored from backup");
                    return value;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to read {candidate}", ex);
            }
        }
        return null;
    }

    public static void Write<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Options);
            stream.Flush(true);
        }

        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(tmp, path);
    }
}
