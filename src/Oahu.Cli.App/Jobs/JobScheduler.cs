using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Jobs;

public sealed class JobSchedulerOptions
{
    public int MaxParallelism { get; init; } = 1;

    /// <summary>Bound on the worker channel; <see cref="IJobService.SubmitAsync"/> async-waits when full.</summary>
    public int ChannelCapacity { get; init; } = 64;
}

/// <summary>
/// Bounded-Channel-backed job scheduler. <see cref="IJobService.SubmitAsync"/> awaits
/// when the worker buffer is full (so backpressure is invisible to the caller — never
/// throws "queue full"). Per-job updates are fanned out to any number of observers
/// via per-subscriber bounded channels; slow observers cannot stall the workers.
/// </summary>
public sealed class JobScheduler : IJobService, IAsyncDisposable
{
    private readonly IJobExecutor executor;
    private readonly JsonlHistoryStore? history;
    private readonly ILogger logger;
    private readonly JobSchedulerOptions options;
    private readonly Channel<JobRequest> work;
    private readonly CancellationTokenSource shutdownCts = new();
    private readonly Task[] workers;
    private readonly ConcurrentDictionary<Guid, Channel<JobUpdate>> subscribers = new();
    private readonly ConcurrentDictionary<string, JobLifecycle> jobs = new();
    private int disposed;

    public JobScheduler(
        IJobExecutor executor,
        JsonlHistoryStore? history = null,
        JobSchedulerOptions? options = null,
        ILogger<JobScheduler>? logger = null)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.history = history;
        this.options = options ?? new JobSchedulerOptions();
        this.logger = logger ?? NullLogger<JobScheduler>.Instance;

        work = Channel.CreateBounded<JobRequest>(new BoundedChannelOptions(this.options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
        workers = new Task[Math.Max(1, this.options.MaxParallelism)];
        for (var i = 0; i < workers.Length; i++)
        {
            workers[i] = Task.Run(WorkerLoopAsync);
        }
    }

    public async Task SubmitAsync(JobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lifecycle = new JobLifecycle(request, DateTimeOffset.UtcNow);
        if (!jobs.TryAdd(request.Id, lifecycle))
        {
            throw new InvalidOperationException($"Job {request.Id} is already known to the scheduler.");
        }

        await Publish(new JobUpdate { JobId = request.Id, Phase = JobPhase.Queued }).ConfigureAwait(false);
        try
        {
            await work.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Channel write failed (caller cancellation, channel completed during shutdown, etc.).
            // Roll back the lifecycle so it doesn't linger forever with no worker to drive it.
            if (jobs.TryRemove(request.Id, out var orphan))
            {
                try
                {
                    await Publish(new JobUpdate
                    {
                        JobId = request.Id,
                        Phase = JobPhase.Canceled,
                        Message = "Submission canceled before queueing.",
                    }).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort
                }
                orphan.Cts.Dispose();
            }
            throw;
        }
    }

    public IAsyncEnumerable<JobUpdate> ObserveAll(CancellationToken cancellationToken = default)
    {
        var (ch, key) = RegisterSubscriber();
        return Drain(ch, key, _ => true, cancellationToken);
    }

    public IAsyncEnumerable<JobUpdate> ObserveAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        var (ch, key) = RegisterSubscriber();
        return Drain(ch, key, u => u.JobId == jobId, cancellationToken);
    }

    public bool Cancel(string jobId)
    {
        if (jobs.TryGetValue(jobId, out var lc))
        {
            lc.Cts.Cancel();
            return true;
        }
        return false;
    }

    public JobSnapshot? GetSnapshot(string jobId) =>
        jobs.TryGetValue(jobId, out var lc) ? Snapshot(lc) : null;

    public IReadOnlyList<JobSnapshot> ListActive()
    {
        var list = new List<JobSnapshot>(jobs.Count);
        foreach (var (_, lc) in jobs)
        {
            list.Add(Snapshot(lc));
        }
        return list;
    }

    public IAsyncEnumerable<JobRecord> ReadHistoryAsync(CancellationToken cancellationToken = default) =>
        history?.ReadAllAsync(cancellationToken) ?? EmptyHistory(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        work.Writer.TryComplete();
        try
        {
            shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        foreach (var sub in subscribers.Values)
        {
            sub.Writer.TryComplete();
        }
        foreach (var (_, lc) in jobs)
        {
            lc.Cts.Dispose();
        }
        shutdownCts.Dispose();
    }

    private static JobSnapshot Snapshot(JobLifecycle lc) => new()
    {
        JobId = lc.Request.Id,
        Asin = lc.Request.Asin,
        Title = lc.Request.Title,
        Phase = lc.LastPhase,
        Progress = lc.LastProgress,
        Message = lc.LastMessage,
        StartedAt = lc.StartedAt,
        UpdatedAt = lc.LastUpdatedAt,
        Quality = lc.Request.Quality,
        ProfileAlias = lc.Request.ProfileAlias,
    };

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (await work.Reader.WaitToReadAsync(shutdownCts.Token).ConfigureAwait(false))
            {
                while (work.Reader.TryRead(out var request))
                {
                    await RunOneAsync(request).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job worker loop crashed.");
        }
    }

    private async Task RunOneAsync(JobRequest request)
    {
        if (!jobs.TryGetValue(request.Id, out var lifecycle))
        {
            return;
        }

        var ct = lifecycle.Cts.Token;
        JobPhase terminal = JobPhase.Completed;
        string? error = null;

        try
        {
            await foreach (var update in executor.ExecuteAsync(request, ct).ConfigureAwait(false))
            {
                await Publish(update).ConfigureAwait(false);
                if (IsTerminal(update.Phase))
                {
                    terminal = update.Phase;
                    error = update.Phase == JobPhase.Failed ? update.Message : null;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            terminal = JobPhase.Canceled;
            await Publish(new JobUpdate { JobId = request.Id, Phase = JobPhase.Canceled, Message = "Canceled" }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            terminal = JobPhase.Failed;
            error = ex.Message;
            await Publish(new JobUpdate { JobId = request.Id, Phase = JobPhase.Failed, Message = ex.Message }).ConfigureAwait(false);
            logger.LogError(ex, "Job {Id} ({Asin}) failed.", request.Id, request.Asin);
        }
        finally
        {
            if (jobs.TryRemove(request.Id, out var removed))
            {
                removed.Cts.Dispose();
            }
            history?.Append(new JobRecord
            {
                Id = request.Id,
                Asin = request.Asin,
                Title = request.Title,
                TerminalPhase = terminal,
                StartedAt = lifecycle.StartedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                ErrorMessage = error,
                ProfileAlias = request.ProfileAlias,
                Quality = request.Quality,
            });
        }
    }

    private static bool IsTerminal(JobPhase p) =>
        p is JobPhase.Completed or JobPhase.Failed or JobPhase.Canceled;

    private async ValueTask Publish(JobUpdate update)
    {
        // Update the per-job lifecycle so GetSnapshot/ListActive return fresh state.
        if (jobs.TryGetValue(update.JobId, out var lc))
        {
            lc.LastPhase = update.Phase;
            lc.LastProgress = update.Progress ?? lc.LastProgress;
            lc.LastMessage = update.Message ?? lc.LastMessage;
            lc.LastUpdatedAt = update.Timestamp;
        }
        foreach (var (key, ch) in subscribers)
        {
            // Per-subscriber backpressure: drop oldest rather than stall the worker.
            // (Subscribers wanting reliable delivery must keep up with their channel.)
            // The channel is configured DropOldest, so TryWrite should always succeed
            // unless the writer was already completed. If it failed, the subscriber is
            // gone (its iterator finished/disposed) — drop it from the map and move on.
            // We never block the worker on a slow/dead observer.
            if (!ch.Writer.TryWrite(update))
            {
                subscribers.TryRemove(key, out _);
                ch.Writer.TryComplete();
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private (Channel<JobUpdate> Channel, Guid Key) RegisterSubscriber()
    {
        // Register synchronously at call time (NOT lazily inside the async
        // iterator) so that callers which subscribe-then-submit are
        // guaranteed to see every update produced after the call returns.
        var ch = System.Threading.Channels.Channel.CreateBounded<JobUpdate>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var key = Guid.NewGuid();
        subscribers.TryAdd(key, ch);
        return (ch, key);
    }

    private async IAsyncEnumerable<JobUpdate> Drain(
        Channel<JobUpdate> ch,
        Guid key,
        Func<JobUpdate, bool> filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var u in ch.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (filter(u))
                {
                    yield return u;
                }
            }
        }
        finally
        {
            subscribers.TryRemove(key, out _);
            ch.Writer.TryComplete();
        }
    }

    private static async IAsyncEnumerable<JobRecord> EmptyHistory(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private sealed class JobLifecycle
    {
        public JobLifecycle(JobRequest request, DateTimeOffset startedAt)
        {
            Request = request;
            StartedAt = startedAt;
            LastUpdatedAt = startedAt;
            LastPhase = JobPhase.Queued;
        }

        public JobRequest Request { get; }

        public DateTimeOffset StartedAt { get; }

        public CancellationTokenSource Cts { get; } = new();

        public JobPhase LastPhase { get; set; }

        public double? LastProgress { get; set; }

        public string? LastMessage { get; set; }

        public DateTimeOffset LastUpdatedAt { get; set; }
    }
}
