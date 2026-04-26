using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Queue;

/// <summary>
/// JSON-file-backed queue, atomically rewritten on every mutation. Lives in
/// <c>SharedUserDataDir/queue.json</c> so the future GUI sees the same queue
/// without a migration (per design §11). Concurrent CLI invocations serialise
/// via an in-process monitor and a sibling lockfile (<c>queue.json.lock</c>)
/// held with <see cref="FileShare.None"/> for the duration of each
/// read-modify-write so that two concurrently invoked <c>oahu-cli</c>
/// processes do not lose updates.
/// </summary>
public sealed class JsonFileQueueService : IQueueService
{
    private readonly string path;
    private readonly string lockPath;
    private readonly object writeLock = new();

    public JsonFileQueueService(string path)
    {
        this.path = path;
        lockPath = path + ".lock";
    }

    public string Path => path;

    public Task<IReadOnlyList<QueueEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<QueueEntry>>(WithLock(() => LoadLocked().ToArray()));
    }

    public Task<bool> AddAsync(QueueEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WithLock(() =>
        {
            var list = LoadLocked();
            if (list.Any(e => string.Equals(e.Asin, entry.Asin, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            list.Add(entry);
            Persist(list);
            return true;
        }));
    }

    public Task<bool> RemoveAsync(string asin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asin);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WithLock(() =>
        {
            var list = LoadLocked();
            var removed = list.RemoveAll(e => string.Equals(e.Asin, asin, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                return false;
            }
            Persist(list);
            return true;
        }));
    }

    public Task<bool> MoveAsync(string asin, int delta, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asin);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WithLock(() =>
        {
            var list = LoadLocked();
            var idx = list.FindIndex(e => string.Equals(e.Asin, asin, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                return false;
            }
            var target = idx + delta;
            if (target < 0 || target >= list.Count)
            {
                return false;
            }
            if (target == idx)
            {
                return false;
            }
            (list[idx], list[target]) = (list[target], list[idx]);
            Persist(list);
            return true;
        }));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WithLock(() =>
        {
            Persist(new List<QueueEntry>());
            return 0;
        });
        return Task.CompletedTask;
    }

    private List<QueueEntry> LoadLocked()
    {
        var loaded = AtomicFile.ReadJson<List<QueueEntry>>(path);
        return loaded ?? new List<QueueEntry>();
    }

    private void Persist(List<QueueEntry> list) => AtomicFile.WriteAllJson(path, list);

    private T WithLock<T>(Func<T> body)
    {
        lock (writeLock)
        {
            var dir = System.IO.Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Cross-process exclusion: only one CLI invocation can hold this fd at a time.
            // FileShare.None requests an exclusive open; on Windows that is enforced for
            // every process, on macOS/Linux it serialises mutating CLI invocations against
            // each other in practice (see design §11, single-writer assumption). The handle
            // is released as soon as the using-block exits — no stale lock files survive.
            using var lockStream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return body();
        }
    }
}
