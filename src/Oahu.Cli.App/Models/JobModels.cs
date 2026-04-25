using System;

namespace Oahu.Cli.App.Models;

/// <summary>Coarse-grained job phase; finer per-phase progress lives in <see cref="JobUpdate"/>.</summary>
public enum JobPhase
{
    Queued,
    Licensing,
    Downloading,
    Decrypting,
    Muxing,
    Completed,
    Failed,
    Canceled,
}

/// <summary>A unit of work submitted to <c>IJobService</c>.</summary>
public sealed record JobRequest
{
    public required string Asin { get; init; }

    public required string Title { get; init; }

    public DownloadQuality Quality { get; init; } = DownloadQuality.High;

    public string? ProfileAlias { get; init; }

    /// <summary>When true, run the AAX exporter ("Muxing" phase) after decrypt.</summary>
    public bool ExportToAax { get; init; }

    /// <summary>Optional override for the export directory (only meaningful when <see cref="ExportToAax"/>).</summary>
    public string? OutputDir { get; init; }

    /// <summary>Stable identifier for cross-invocation tracking (history.jsonl).</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
}

/// <summary>A single observation emitted while a job runs. Streamed via <see cref="System.Collections.Generic.IAsyncEnumerable{T}"/>.</summary>
public sealed record JobUpdate
{
    public required string JobId { get; init; }

    public required JobPhase Phase { get; init; }

    /// <summary>Per-phase progress in [0, 1], or null if indeterminate.</summary>
    public double? Progress { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Snapshot persisted to <c>history.jsonl</c> when a job leaves a terminal state.</summary>
public sealed record JobRecord
{
    public required string Id { get; init; }

    public required string Asin { get; init; }

    public required string Title { get; init; }

    public required JobPhase TerminalPhase { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ProfileAlias { get; init; }

    /// <summary>
    /// Quality requested when the job was submitted. Optional for backwards
    /// compatibility with records produced before phase 4c.2: a missing
    /// value means "use the current default" on retry.
    /// </summary>
    public DownloadQuality? Quality { get; init; }
}
