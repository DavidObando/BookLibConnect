using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Queue;

/// <summary>
/// Thread-safe in-memory queue. Used by the test suite and by short-lived
/// scenarios that don't need cross-invocation persistence (e.g. <c>oahu-cli serve</c>
/// can be configured to keep its queue purely in-memory).
/// </summary>
public sealed class InMemoryQueueService : IQueueService
{
    private readonly object @lock = new();
    private readonly List<QueueEntry> entries = new();

    public Task<IReadOnlyList<QueueEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (@lock)
        {
            return Task.FromResult<IReadOnlyList<QueueEntry>>(entries.ToArray());
        }
    }

    public Task<bool> AddAsync(QueueEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (@lock)
        {
            if (entries.Any(e => string.Equals(e.Asin, entry.Asin, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }
            entries.Add(entry);
            return Task.FromResult(true);
        }
    }

    public Task<bool> RemoveAsync(string asin, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asin);
        cancellationToken.ThrowIfCancellationRequested();
        lock (@lock)
        {
            var removed = entries.RemoveAll(e => string.Equals(e.Asin, asin, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(removed > 0);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (@lock)
        {
            entries.Clear();
        }
        return Task.CompletedTask;
    }
}
