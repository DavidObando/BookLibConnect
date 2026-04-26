using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Queue;

/// <summary>
/// JSON-file-backed queue, atomically rewritten on every mutation. Lives in
/// <c>SharedUserDataDir/queue.json</c> so the future GUI sees the same queue
/// without a migration (per design §11). Concurrent CLI invocations serialise
/// via a process-wide lock; this is fine for command-mode usage where the
/// scheduler is the only writer in any given run.
/// </summary>
public sealed class JsonFileQueueService : IQueueService
{
    private readonly string path;
    private readonly object writeLock = new();

    public JsonFileQueueService(string path)
    {
        this.path = path;
    }

    public string Path => path;

    public Task<IReadOnlyList<QueueEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<QueueEntry>>(LoadLocked().ToArray());
    }

    public Task<bool> AddAsync(QueueEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (writeLock)
        {
            var list = LoadLocked();
            if (list.Any(e => string.Equals(e.Asin, entry.Asin, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }
            list.Add(entry);
            Persist(list);
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveAsync(string asin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asin);
        cancellationToken.ThrowIfCancellationRequested();
        lock (writeLock)
        {
            var list = LoadLocked();
            var removed = list.RemoveAll(e => string.Equals(e.Asin, asin, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                return Task.FromResult(false);
            }
            Persist(list);
            return Task.FromResult(true);
        }
    }

    public Task<bool> MoveAsync(string asin, int delta, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asin);
        cancellationToken.ThrowIfCancellationRequested();
        lock (writeLock)
        {
            var list = LoadLocked();
            var idx = list.FindIndex(e => string.Equals(e.Asin, asin, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                return Task.FromResult(false);
            }
            var target = idx + delta;
            if (target < 0 || target >= list.Count || target == idx)
            {
                return Task.FromResult(false);
            }
            (list[idx], list[target]) = (list[target], list[idx]);
            Persist(list);
            return Task.FromResult(true);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (writeLock)
        {
            Persist(new List<QueueEntry>());
        }
        return Task.CompletedTask;
    }

    private List<QueueEntry> LoadLocked()
    {
        var loaded = AtomicFile.ReadJson<List<QueueEntry>>(path);
        return loaded ?? new List<QueueEntry>();
    }

    private void Persist(List<QueueEntry> list) => AtomicFile.WriteAllJson(path, list);
}
