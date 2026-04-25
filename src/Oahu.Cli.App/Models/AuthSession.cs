using System;

namespace Oahu.Cli.App.Models;

/// <summary>Audible region the user is signed in against.</summary>
public enum CliRegion
{
    Us,
    Uk,
    De,
    Fr,
    It,
    Es,
    Jp,
    Au,
    Ca,
    In,
    Br,
}

/// <summary>One signed-in profile, as visible from the CLI boundary.</summary>
public sealed record AuthSession
{
    public required string ProfileAlias { get; init; }

    public required CliRegion Region { get; init; }

    public required string AccountId { get; init; }

    public string? AccountName { get; init; }

    public string? DeviceName { get; init; }

    /// <summary>UTC instant the access token expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsExpired => ExpiresAt is { } e && e <= DateTimeOffset.UtcNow;
}
