using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Queue;

/// <summary>The user-visible pending queue. Persists across CLI invocations.</summary>
public interface IQueueService
{
    Task<IReadOnlyList<QueueEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds an entry, returning <c>true</c> if it was new and <c>false</c> if a same-ASIN entry already existed.</summary>
    Task<bool> AddAsync(QueueEntry entry, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string asin, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
