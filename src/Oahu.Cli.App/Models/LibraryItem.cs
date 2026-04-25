using System;

namespace Oahu.Cli.App.Models;

/// <summary>A single book entry as the CLI sees it. Subset of <c>Oahu.Core.Book</c>; no Core leakage.</summary>
public sealed record LibraryItem
{
    public required string Asin { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public string[] Authors { get; init; } = Array.Empty<string>();

    public string[] Narrators { get; init; } = Array.Empty<string>();

    public string? Series { get; init; }

    public double? SeriesPosition { get; init; }

    public TimeSpan? Runtime { get; init; }

    public DateTimeOffset? PurchaseDate { get; init; }

    public bool IsAvailable { get; init; } = true;

    public bool HasMultiplePartFiles { get; init; }
}
