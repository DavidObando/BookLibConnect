using System;
using System.Text;
using Oahu.Cli.Tui.Icons;
using Oahu.Cli.Tui.Tokens;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// Single-line "spinner + verb + hint + optional metric" widget used for opaque waits.
///
/// Per design §6.6.1, the indicator is a static <c>◐</c> in Phase 2 (no live spinner —
/// that needs the AppShell render loop, which lands in Phase 6). The verb + hint pair
/// is what carries the information, so layout / colour are correct from day one.
/// </summary>
public sealed class StatusLine
{
    public string Verb { get; init; } = string.Empty;

    public string? Hint { get; init; }

    public string? Metric { get; init; }

    public Icon? Indicator { get; init; }

    public bool UseAscii { get; init; }

    /// <summary>Render to a Spectre <see cref="IRenderable"/> for use inside layouts.</summary>
    public IRenderable Render()
    {
        var ind = (Indicator ?? Icons.Icons.Working).Render(UseAscii);
        var indCol = (Indicator ?? Icons.Icons.Working).Color;
        var sb = new StringBuilder();
        sb.Append('[').Append(indCol.Value.ToMarkup()).Append(']').Append(Markup.Escape(ind)).Append("[/]")
          .Append(' ')
          .Append('[').Append(Tokens.Tokens.TextPrimary.Value.ToMarkup()).Append(']').Append(Markup.Escape(Verb)).Append("[/]");
        if (!string.IsNullOrEmpty(Hint))
        {
            sb.Append(' ')
              .Append('[').Append(Tokens.Tokens.TextTertiary.Value.ToMarkup()).Append(']')
              .Append("· ").Append(Markup.Escape(Hint!))
              .Append("[/]");
        }
        if (!string.IsNullOrEmpty(Metric))
        {
            sb.Append(' ')
              .Append('[').Append(Tokens.Tokens.TextSecondary.Value.ToMarkup()).Append(']')
              .Append("· ").Append(Markup.Escape(Metric!))
              .Append("[/]");
        }
        return new Markup(sb.ToString());
    }

    /// <summary>Convenience: render directly to <paramref name="console"/>.</summary>
    public void Write(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.Write(Render());
        console.WriteLine();
    }
}
