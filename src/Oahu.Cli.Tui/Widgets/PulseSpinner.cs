using System;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// The single "thinking" spinner used across the TUI for opaque waits.
///
/// Per <c>OAHU_CLI_DESIGN_TUI_EXPLORATION.md §16.3</c>:
/// <list type="bullet">
///   <item>Frame set <c>● ◉ ◎ ○</c>, all single-width, identical width.
///         Replacing the spinner with a <c>✓</c> / <c>✗</c> on completion
///         must not shift any column.</item>
///   <item>Always paired with a verb and an Esc hint by the surrounding
///         widget (e.g. <see cref="StatusLine"/>); the spinner alone is
///         decoration.</item>
///   <item>Cadence: ~12 FPS in the alt-screen TUI loop, ~6 FPS for inline
///         renders (the AppShell render loop is the cadence source —
///         this widget just advances one frame per <see cref="Render"/>
///         call).</item>
///   <item>Disabled (replaced by a static <c>*</c> in ASCII) when
///         <c>useAscii</c> is set or the host is not a TTY / NO_COLOR.</item>
/// </list>
///
/// <see cref="PulseSpinner"/> is intentionally stateful: each instance owns
/// a frame counter that auto-increments per <see cref="Render"/> call so
/// callers can simply embed it into their layout and re-render to animate.
/// Use <see cref="Tick"/> to manually advance when rendering through a
/// pre-built <see cref="IRenderable"/>.
/// </summary>
public sealed class PulseSpinner
{
    private static readonly char[] Frames = ['●', '◉', '◎', '○', '◎', '◉'];

    private int frame;

    /// <summary>If true, render as a static <c>*</c> (ASCII-safe).</summary>
    public bool UseAscii { get; init; }

    /// <summary>Optional override of the colour token.</summary>
    public string? ColorMarkup { get; init; }

    /// <summary>The current single-width glyph (for embedding inline).</summary>
    public string Glyph => UseAscii ? "*" : Frames[frame % Frames.Length].ToString();

    /// <summary>Advance one frame without rendering.</summary>
    public void Tick() => frame = (frame + 1) % Frames.Length;

    /// <summary>
    /// Render the current frame and advance. The result is a single-width
    /// markup string sized to fit alongside text without column shift.
    /// </summary>
    public IRenderable Render()
    {
        var color = ColorMarkup ?? Tokens.Tokens.Brand.Value.ToMarkup();
        var glyph = Glyph;
        Tick();
        return new Markup($"[{color}]{Markup.Escape(glyph)}[/]");
    }

    /// <summary>Render to a markup-only string (advances the frame).</summary>
    public string RenderMarkup()
    {
        var color = ColorMarkup ?? Tokens.Tokens.Brand.Value.ToMarkup();
        var glyph = Glyph;
        Tick();
        return $"[{color}]{Markup.Escape(glyph)}[/]";
    }
}
