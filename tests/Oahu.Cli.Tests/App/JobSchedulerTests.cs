using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class JobSchedulerTests : IDisposable
{
    private readonly string tempHistory;

    public JobSchedulerTests()
    {
        tempHistory = Path.Combine(Path.GetTempPath(), $"oahu-cli-history-{Guid.NewGuid():n}.jsonl");
    }

    public void Dispose()
    {
        if (File.Exists(tempHistory))
        {
            File.Delete(tempHistory);
        }
    }

    private static JobRequest Req(string title = "Book") =>
        new() { Asin = $"B{Guid.NewGuid():n}", Title = title };

    [Fact]
    public async Task Submit_And_Observe_Reaches_Completed()
    {
        await using var sched = new JobScheduler(new FakeJobExecutor());
        var req = Req();

        var observed = new List<JobUpdate>();
        var observeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var task = Task.Run(async () =>
        {
            await foreach (var u in sched.ObserveAsync(req.Id, observeCts.Token))
            {
                observed.Add(u);
                if (u.Phase is JobPhase.Completed or JobPhase.Failed or JobPhase.Canceled)
                {
                    break;
                }
            }
        });

        await Task.Delay(50); // give the observer time to subscribe before we publish
        await sched.SubmitAsync(req);
        await task;

        Assert.Contains(observed, u => u.Phase == JobPhase.Licensing);
        Assert.Contains(observed, u => u.Phase == JobPhase.Downloading);
        Assert.Contains(observed, u => u.Phase == JobPhase.Decrypting);
        Assert.Contains(observed, u => u.Phase == JobPhase.Exporting);
        Assert.Equal(JobPhase.Completed, observed[^1].Phase);
    }

    [Fact]
    public async Task History_Receives_Terminal_Records()
    {
        var history = new JsonlHistoryStore(tempHistory);
        await using (var sched = new JobScheduler(new FakeJobExecutor(), history))
        {
            var r1 = Req("ok");
            var r2 = Req("fail");

            // Submit a fail-at-decrypt job by swapping executors; quickest: separate scheduler.
            await sched.SubmitAsync(r1);
            await WaitForFile(tempHistory, expectedRecords: 1);
        }

        await using (var failSched = new JobScheduler(new FakeJobExecutor(failAtDecrypt: true), history))
        {
            await failSched.SubmitAsync(Req("fail"));
            await WaitForFile(tempHistory, expectedRecords: 2);
        }

        var records = new List<JobRecord>();
        await foreach (var rec in history.ReadAllAsync())
        {
            records.Add(rec);
        }
        Assert.Equal(2, records.Count);
        Assert.Contains(records, r => r.TerminalPhase == JobPhase.Completed);
        Assert.Contains(records, r => r.TerminalPhase == JobPhase.Failed);
    }

    [Fact]
    public async Task Cancel_Stops_Job_And_Records_Canceled()
    {
        var history = new JsonlHistoryStore(tempHistory);
        var slow = new FakeJobExecutor(delayPerPhase: TimeSpan.FromSeconds(2));
        await using var sched = new JobScheduler(slow, history);
        var req = Req("slow");

        await sched.SubmitAsync(req);
        await Task.Delay(100);
        Assert.True(sched.Cancel(req.Id));

        await WaitForFile(tempHistory, expectedRecords: 1, timeout: TimeSpan.FromSeconds(10));
        var records = new List<JobRecord>();
        await foreach (var rec in history.ReadAllAsync())
        {
            records.Add(rec);
        }
        Assert.Single(records);
        Assert.Equal(JobPhase.Canceled, records[0].TerminalPhase);
    }

    [Fact]
    public async Task Bounded_Concurrency_Limit_Is_Enforced()
    {
        // Two parallel workers; submit four jobs; verify peak concurrent in-flight is <= 2.
        int active = 0, peak = 0;
        var executor = new ProbeExecutor(
            onStart: () => peak = Math.Max(peak, Interlocked.Increment(ref active)),
            onEnd: () => Interlocked.Decrement(ref active));

        await using var sched = new JobScheduler(executor, options: new JobSchedulerOptions { MaxParallelism = 2 });
        var ids = Enumerable.Range(0, 4).Select(_ => Req()).ToArray();

        var done = new TaskCompletionSource();
        var seenCompletions = 0;
        var observeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _ = Task.Run(async () =>
        {
            await foreach (var u in sched.ObserveAll(observeCts.Token))
            {
                if (u.Phase == JobPhase.Completed && Interlocked.Increment(ref seenCompletions) == 4)
                {
                    done.TrySetResult();
                }
            }
        });

        await Task.Delay(50);
        foreach (var r in ids)
        {
            await sched.SubmitAsync(r);
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(peak <= 2, $"peak {peak} exceeded MaxParallelism");
    }

    private static async Task WaitForFile(string path, int expectedRecords, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var fs = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs);
                    var count = 0;
                    while (await reader.ReadLineAsync().ConfigureAwait(false) is string line)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            count++;
                        }
                    }
                    if (count >= expectedRecords)
                    {
                        return;
                    }
                }
            }
            catch (IOException)
            {
                // file briefly locked by the writer on Windows; retry.
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"history file did not reach {expectedRecords} records within timeout");
    }

    private sealed class ProbeExecutor : IJobExecutor
    {
        private readonly Action onStart;
        private readonly Action onEnd;

        public ProbeExecutor(Action onStart, Action onEnd)
        {
            this.onStart = onStart;
            this.onEnd = onEnd;
        }

        public async IAsyncEnumerable<JobUpdate> ExecuteAsync(
            JobRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            onStart();
            try
            {
                yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Downloading };
                await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Completed };
            }
            finally
            {
                onEnd();
            }
        }
    }
}
