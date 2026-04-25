using System;
using System.Collections.Generic;
using Oahu.Cli.Tui.Logging;

namespace Oahu.Cli.Tui.Shell;

/// <summary>Configuration knobs for <see cref="AppShell"/>.</summary>
public sealed class AppShellOptions
{
    /// <summary>Force ASCII glyphs / borders. Honoured when <c>OAHU_ASCII_ICONS=1</c> or <c>--ascii</c>.</summary>
    public bool UseAscii { get; init; }

    /// <summary>Optional ring-buffer logger that feeds the Logs overlay (toggled with <c>L</c>).</summary>
    public LogRingBuffer? LogBuffer { get; init; }

    /// <summary>Display profile / region in the header. Empty = "not signed in".</summary>
    public string Profile { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    /// <summary>Version string shown on the right side of the header.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Custom tab list. Defaults to the six placeholder screens
    /// (<see cref="DefaultTabs"/>) when null.
    /// </summary>
    public IReadOnlyList<ITabScreen>? Tabs { get; init; }

    /// <summary>Synthesised activity verb shown in the header (e.g. "idle", "syncing library…").</summary>
    public Func<string>? ActivityVerb { get; init; }

    /// <summary>
    /// Mutable runtime state. When set, the header reads profile/region/activity
    /// from this instead of the init-only properties. Phase 7+.
    /// </summary>
    public AppShellState? State { get; init; }
}
