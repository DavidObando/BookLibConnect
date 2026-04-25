using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Jobs;

/// <summary>
/// Scripted executor used by tests and (via <c>oahu-cli ui-preview-jobs</c> later)
/// by humans verifying TUI animations. Walks Licensing → Downloading → Decrypting →
/// Muxing → Completed with token-checked sleep durations between phases.
/// </summary>
public sealed class FakeJobExecutor : IJobExecutor
{
    private readonly TimeSpan delayPerPhase;
    private readonly bool failAtDecrypt;

    public FakeJobExecutor(TimeSpan? delayPerPhase = null, bool failAtDecrypt = false)
    {
        this.delayPerPhase = delayPerPhase ?? TimeSpan.FromMilliseconds(5);
        this.failAtDecrypt = failAtDecrypt;
    }

    public async IAsyncEnumerable<JobUpdate> ExecuteAsync(
        JobRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Licensing, Message = "Requesting license" };
        await Task.Delay(delayPerPhase, cancellationToken).ConfigureAwait(false);

        yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Downloading, Progress = 0 };
        await Task.Delay(delayPerPhase, cancellationToken).ConfigureAwait(false);
        yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Downloading, Progress = 1 };

        if (failAtDecrypt)
        {
            yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Failed, Message = "Decrypt simulated failure" };
            yield break;
        }

        yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Decrypting, Progress = 0 };
        await Task.Delay(delayPerPhase, cancellationToken).ConfigureAwait(false);
        yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Decrypting, Progress = 1 };

        yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Muxing };
        await Task.Delay(delayPerPhase, cancellationToken).ConfigureAwait(false);

        yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Completed };
    }
}
