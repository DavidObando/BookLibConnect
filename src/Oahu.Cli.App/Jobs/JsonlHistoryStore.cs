using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger logger;

    public JsonlHistoryStore(string path, ILogger<JsonlHistoryStore>? logger = null)
    {
        this.path = path;
        this.logger = logger ?? NullLogger<JsonlHistoryStore>.Instance;
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
        var lineNumber = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JobRecord? rec = null;
            try
            {
                rec = JsonSerializer.Deserialize<JobRecord>(line, options);
            }
            catch (JsonException ex)
            {
                // Skip torn records; the design's "append-only" guarantee tolerates a single torn tail line.
                // Still surface the issue so operators can detect repeated corruption.
                logger.LogWarning(
                    ex,
                    "Skipping malformed history record at {Path}:{LineNumber}: {Reason}",
                    path,
                    lineNumber,
                    ex.Message);
            }
            if (rec is not null)
            {
                yield return rec;
            }
        }
    }

    /// <summary>
    /// Deletes history records that match any of the supplied filters,
    /// rewriting the file atomically. Records are KEPT iff:
    ///   - their ASIN is NOT in <paramref name="asins"/> (when non-empty), AND
    ///   - their <c>CompletedAt</c> is NOT before <paramref name="before"/> (when set), AND
    ///   - they fall within the most recent <paramref name="keep"/> records overall
    ///     (when set). The <paramref name="keep"/> filter is applied last.
    /// Returns the number of deleted records.
    /// </summary>
    public async Task<int> DeleteAsync(
        IReadOnlyCollection<string>? asins = null,
        DateTimeOffset? before = null,
        int? keep = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        var asinSet = asins is { Count: > 0 }
            ? new HashSet<string>(asins, StringComparer.OrdinalIgnoreCase)
            : null;

        var all = new List<JobRecord>();
        await foreach (var rec in ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            all.Add(rec);
        }

        var kept = new List<JobRecord>(all.Count);
        foreach (var rec in all)
        {
            var matchesAsin = asinSet is not null && asinSet.Contains(rec.Asin);
            var matchesBefore = before is { } b && rec.CompletedAt < b;
            if (matchesAsin || matchesBefore)
            {
                continue;
            }
            kept.Add(rec);
        }

        if (keep is { } n && kept.Count > n)
        {
            kept = kept
                .OrderByDescending(r => r.CompletedAt)
                .Take(n)
                .OrderBy(r => r.CompletedAt)
                .ToList();
        }

        lock (writeLock)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var tmp = path + ".tmp." + Guid.NewGuid().ToString("n");
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                foreach (var rec in kept)
                {
                    writer.WriteLine(JsonSerializer.Serialize(rec, options));
                }
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }

        return all.Count - kept.Count;
    }
}
