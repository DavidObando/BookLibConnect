using System;
using System.Text;
using Oahu.Cli.Tui.Icons;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>The semantic state of a <see cref="TimelineItem"/>.</summary>
public enum TimelineState
{
    Loading,
    Success,
    Error,
    Warning,
    Info,
}

/// <summary>
/// One row in a vertical "timeline" (Doctor checks, Jobs phases, etc.). Layout invariant:
/// the status prefix is **always 4 cells wide** (icon + 3 spaces) so swapping
/// <c>◐</c> for <c>✓</c> never reflows the row — see §6.5 of the design doc.
/// </summary>
public sealed class TimelineItem
{
    public required string Title { get; init; }

    public string? Description { get; init; }

    public TimelineState State { get; init; } = TimelineState.Info;

    public bool UseAscii { get; init; }

    /// <summary>Optional second indented line for additional context.</summary>
    public string? Detail { get; init; }

    public IRenderable Render()
    {
        var icon = StateIcon(State);
        var glyph = icon.Render(UseAscii);

        // 4-char fixed-width prefix: glyph + 3 spaces. Layout never shifts on state change.
        var prefix = $"[{icon.Color.Value.ToMarkup()}]{Markup.Escape(glyph)}[/]   ";

        var sb = new StringBuilder();
        sb.Append(prefix)
          .Append('[').Append(Tokens.Tokens.TextPrimary.Value.ToMarkup()).Append(']')
          .Append(Markup.Escape(Title))
          .Append("[/]");

        if (!string.IsNullOrEmpty(Description))
        {
            sb.Append("  ")
              .Append('[').Append(Tokens.Tokens.TextSecondary.Value.ToMarkup()).Append(']')
              .Append(Markup.Escape(Description!))
              .Append("[/]");
        }

        if (!string.IsNullOrEmpty(Detail))
        {
            sb.Append('\n').Append("    ")
              .Append('[').Append(Tokens.Tokens.TextTertiary.Value.ToMarkup()).Append(']')
              .Append(Markup.Escape(Detail!))
              .Append("[/]");
        }

        return new Markup(sb.ToString());
    }

    public void Write(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.Write(Render());
        console.WriteLine();
    }

    private static Icon StateIcon(TimelineState s) => s switch
    {
        TimelineState.Loading => Icons.Icons.Working,
        TimelineState.Success => Icons.Icons.Success,
        TimelineState.Error => Icons.Icons.Error,
        TimelineState.Warning => Icons.Icons.Warning,
        TimelineState.Info => Icons.Icons.Info,
        _ => Icons.Icons.Info,
    };
}
