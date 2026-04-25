using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Jobs;

/// <summary>
/// Pluggable strategy for actually performing one <see cref="JobRequest"/>.
/// Phase 3 ships a <c>FakeJobExecutor</c> that scripts the phase transitions;
/// Phase 4 ships <c>AudibleJobExecutor</c> backed by <c>Oahu.Core.AudibleApi</c>
/// + <c>DownloadDecryptJob</c>.
/// </summary>
public interface IJobExecutor
{
    /// <summary>
    /// Executes <paramref name="request"/>. Yields one update per phase change (and
    /// optionally interim progress updates within a phase). Must yield a terminal
    /// update (Completed / Failed / Canceled) before returning.
    /// </summary>
    IAsyncEnumerable<JobUpdate> ExecuteAsync(JobRequest request, CancellationToken cancellationToken);
}
