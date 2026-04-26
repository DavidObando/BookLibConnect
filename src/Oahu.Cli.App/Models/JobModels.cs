using System;

namespace Oahu.Cli.App.Models;

/// <summary>Coarse-grained job phase; finer per-phase progress lives in <see cref="JobUpdate"/>.</summary>
public enum JobPhase
{
    Queued,
    Licensing,
    Downloading,
    Decrypting,
    Exporting,
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

    /// <summary>When true, run the AAX exporter ("Exporting" phase) after decrypt.</summary>
    public bool ExportToAax { get; init; }

    /// <summary>
    /// When true, also produce a <c>.m4b</c> file via the same exporter. May be
    /// combined with <see cref="ExportToAax"/> ("both" mode) — design §4.1.
    /// </summary>
    public bool ExportToM4b { get; init; }

    /// <summary>
    /// When true, stop after the LocalLocked phase: the encrypted <c>.aax(c)</c>
    /// file is left on disk and no decryption is performed. Useful for offline
    /// archival or manual conversion. Implies <see cref="ExportToAax"/> /
    /// <see cref="ExportToM4b"/> are ignored.
    /// </summary>
    public bool NoDecrypt { get; init; }

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

/// <summary>
/// Latest-known status of an in-flight job. Returned by <c>IJobService.GetSnapshotAsync</c> /
/// <c>ListActiveAsync</c> so HTTP/MCP clients can poll progress without missing the early
/// <c>Queued</c>/<c>Licensing</c> updates that may fire before they connect.
/// </summary>
public sealed record JobSnapshot
{
    public required string JobId { get; init; }

    public required string Asin { get; init; }

    public required string Title { get; init; }

    public required JobPhase Phase { get; init; }

    public double? Progress { get; init; }

    public string? Message { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Timestamp of the most recent <see cref="JobUpdate"/> applied to this snapshot.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    public DownloadQuality? Quality { get; init; }

    public string? ProfileAlias { get; init; }
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
