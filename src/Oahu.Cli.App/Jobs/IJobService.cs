using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Jobs;

/// <summary>The Phase 3 / 4 face of the job system used by commands and (Phase 5) the server.</summary>
public interface IJobService
{
    /// <summary>
    /// Submit a job. The returned task completes when the scheduler has accepted the
    /// request (it may block briefly for backpressure when the worker channel is full).
    /// Use <see cref="ObserveAll"/> or <see cref="ObserveAsync(string, CancellationToken)"/> to track progress.
    /// </summary>
    Task SubmitAsync(JobRequest request, CancellationToken cancellationToken = default);

    /// <summary>Observe every update from every job (live + future).</summary>
    IAsyncEnumerable<JobUpdate> ObserveAll(CancellationToken cancellationToken = default);

    /// <summary>Observe updates for a specific job. Completes when that job reaches a terminal phase.</summary>
    IAsyncEnumerable<JobUpdate> ObserveAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Cooperatively cancel a running or queued job. Returns true if the job was found.</summary>
    bool Cancel(string jobId);

    /// <summary>
    /// Latest-known status of one in-flight job, or null if the job is unknown to the
    /// scheduler (either it has already reached a terminal state and rolled to history,
    /// or the id was never submitted). Lets HTTP/MCP clients poll without subscribing.
    /// </summary>
    JobSnapshot? GetSnapshot(string jobId);

    /// <summary>Latest-known status of every job currently tracked by the scheduler.</summary>
    IReadOnlyList<JobSnapshot> ListActive();

    /// <summary>Read the on-disk history (terminal-state job records).</summary>
    IAsyncEnumerable<JobRecord> ReadHistoryAsync(CancellationToken cancellationToken = default);
}
