using System;

namespace Oahu.Cli.App.Models;

/// <summary>One book waiting to be downloaded (or in flight). Lives in <c>queue.json</c>.</summary>
public sealed record QueueEntry
{
    public required string Asin { get; init; }

    public required string Title { get; init; }

    public DownloadQuality Quality { get; init; } = DownloadQuality.High;

    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Free-form profile alias the entry should be downloaded against (null = default profile).</summary>
    public string? ProfileAlias { get; init; }
}
