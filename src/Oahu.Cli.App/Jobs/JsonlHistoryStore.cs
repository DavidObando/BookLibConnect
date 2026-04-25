using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Jobs;

/// <summary>
/// Append-only JSONL history store. Records are written one-per-line so a partial
/// record on crash can be skipped without corrupting the rest of the file.
/// </summary>
public sealed class JsonlHistoryStore
{
    private readonly string path;
    private readonly object writeLock = new();
    private readonly JsonSerializerOptions options;

    public JsonlHistoryStore(string path)
    {
        this.path = path;
        options = new JsonSerializerOptions(AtomicFile.DefaultJsonOptions)
        {
            WriteIndented = false,
        };
    }

    public string Path => path;

    public void Append(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (writeLock)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(JsonSerializer.Serialize(record, options));
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
    }

    public async IAsyncEnumerable<JobRecord> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        using var reader = new StreamReader(new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JobRecord? rec = null;
            try
            {
                rec = JsonSerializer.Deserialize<JobRecord>(line, options);
            }
            catch (JsonException)
            {
                // Skip torn records; the design's "append-only" guarantee tolerates a single torn tail line.
            }
            if (rec is not null)
            {
                yield return rec;
            }
        }
    }
}
