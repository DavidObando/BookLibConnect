using System;
using Oahu.Cli.Tui.Tokens;

namespace Oahu.Cli.Tui.Icons;

/// <summary>
/// Single-width Unicode glyphs paired with ASCII fallbacks for hostile terminals.
///
/// Every icon ships:
///   • <see cref="Glyph"/>          — the Unicode form, single-cell guaranteed.
///   • <see cref="AsciiFallback"/>  — emitted when ASCII mode is forced.
///   • <see cref="Color"/>          — the semantic colour to render with.
///   • <see cref="ScreenReaderLabel"/> — what to announce when a screen reader is active.
/// </summary>
public readonly record struct Icon(string Glyph, string AsciiFallback, SemanticColor Color, string ScreenReaderLabel)
{
    /// <summary>Returns the right glyph for the current rendering mode (ASCII forced or not).</summary>
    public string Render(bool useAscii) => useAscii ? AsciiFallback : Glyph;
}

/// <summary>The icon set referenced by every widget.</summary>
public static class Icons
{
    public static Icon Success => new("✓", "+", Tokens.Tokens.StatusSuccess, "Success");

    public static Icon Error => new("✗", "X", Tokens.Tokens.StatusError, "Error");

    public static Icon Warning => new("!", "!", Tokens.Tokens.StatusWarning, "Warning");

    public static Icon Info => new("·", ".", Tokens.Tokens.StatusInfo, "Info");

    public static Icon Disabled => new("⊘", "-", Tokens.Tokens.TextTertiary, "Disabled");

    public static Icon Prompt => new("❯", ">", Tokens.Tokens.Brand, "Prompt");

    public static Icon Filled => new("●", "*", Tokens.Tokens.TextPrimary, "Filled");

    /// <summary>Single binary "in-flight" glyph for rows with their own phase bars (§6.6.1, §16).</summary>
    public static Icon Working => new("◐", "*", Tokens.Tokens.StatusInfo, "Working");

    public static Icon Empty => new("○", "o", Tokens.Tokens.TextTertiary, "Empty");

    public static Icon ArrowRight => new("→", "->", Tokens.Tokens.TextSecondary, "Arrow right");

    public static Icon ArrowUp => new("↑", "^", Tokens.Tokens.TextSecondary, "Arrow up");

    public static Icon ArrowDown => new("↓", "v", Tokens.Tokens.TextSecondary, "Arrow down");

    /// <summary>True when the runtime forces ASCII glyphs (env override or hostile terminal).</summary>
    public static bool ForceAscii =>
        string.Equals(Environment.GetEnvironmentVariable("OAHU_ASCII_ICONS"), "1", StringComparison.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);
}
