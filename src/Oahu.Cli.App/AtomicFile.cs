using System.IO;
using System.Text.Json;

namespace Oahu.Cli.App;

/// <summary>
/// Helpers for serialising files atomically: write to a sibling <c>.tmp</c>,
/// flush to disk, then move-overwrite onto the destination. The move on POSIX
/// (rename(2)) and on Windows (MoveFileEx with REPLACE_EXISTING) is atomic at
/// the directory-entry level, so a crash either leaves the old file untouched
/// or the new file fully present.
/// </summary>
public static class AtomicFile
{
    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Serialise <paramref name="value"/> as JSON to <paramref name="path"/> atomically.</summary>
    public static void WriteAllJson<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        // Write + flush(true): fsync the file's bytes so the rename does not promote a partially written file.
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, options ?? DefaultJsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Read JSON from <paramref name="path"/>, returning <c>default</c> if the file does not exist.</summary>
    public static T? ReadJson<T>(string path, JsonSerializerOptions? options = null)
    {
        if (!File.Exists(path))
        {
            return default;
        }
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, options ?? DefaultJsonOptions);
    }
}
