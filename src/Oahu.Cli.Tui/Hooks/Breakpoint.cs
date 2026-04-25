namespace Oahu.Cli.Tui.Hooks;

/// <summary>
/// Three responsive breakpoints used across the design system, per
/// <c>docs/OAHU_CLI_DESIGN_TUI_EXPLORATION.md</c> §1.
/// </summary>
public enum BreakpointKind
{
    /// <summary>&lt; 80 columns.</summary>
    Compact,

    /// <summary>80 – 119 columns.</summary>
    Narrow,

    /// <summary>≥ 120 columns.</summary>
    Wide,
}

/// <summary>Maps a width to a <see cref="BreakpointKind"/>.</summary>
public static class Breakpoint
{
    public const int CompactMax = 79;
    public const int NarrowMax = 119;

    public static BreakpointKind For(int width) =>
        width <= CompactMax ? BreakpointKind.Compact :
        width <= NarrowMax ? BreakpointKind.Narrow :
        BreakpointKind.Wide;

    public static BreakpointKind For(TerminalSize size) => For(size.Width);
}
