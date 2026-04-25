using System;

namespace Oahu.Cli.Tui.Shell;

/// <summary>
/// Mutable state shared across the AppShell chrome and screens.
/// Updated after sign-in, library sync, etc.
/// </summary>
public sealed class AppShellState
{
    /// <summary>Display name of the active profile (empty when not signed in).</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>Active Audible region.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Coalesced activity verb shown in the header ("idle", "syncing…", "2 jobs running").</summary>
    public string ActivityVerb { get; set; } = "idle";

    /// <summary>True when at least one profile is signed in.</summary>
    public bool IsSignedIn => !string.IsNullOrEmpty(Profile);

    /// <summary>Formatted header display: "profile@region" or "(not signed in)".</summary>
    public string ProfileDisplay =>
        string.IsNullOrEmpty(Profile)
            ? "(not signed in)"
            : string.IsNullOrEmpty(Region)
                ? Profile
                : $"{Profile}@{Region}";
}
