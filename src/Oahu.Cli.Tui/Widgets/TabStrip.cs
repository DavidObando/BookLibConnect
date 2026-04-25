using System;
using System.Collections.Generic;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// The top tab strip. Renders a single horizontal row of
/// <c>1 Home  2 Library  3 Queue …</c> with the active tab painted in the
/// <c>Selected</c> token.
/// </summary>
public sealed class TabStrip
{
    public required IReadOnlyList<string> Titles { get; init; }

    public required int ActiveIndex { get; init; }

    public bool UseAscii { get; init; }

    public IRenderable Render()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Titles.Count; i++)
        {
            var num = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var label = num + " " + Titles[i];
            if (i > 0)
            {
                sb.Append("  ");
            }

            if (i == ActiveIndex)
            {
                sb.Append('[').Append(Tokens.Tokens.Selected.Value.ToMarkup()).Append(" bold]")
                  .Append(' ').Append(Markup.Escape(label)).Append(' ')
                  .Append("[/]");
            }
            else
            {
                sb.Append('[').Append(Tokens.Tokens.TextSecondary.Value.ToMarkup()).Append(']')
                  .Append(' ').Append(Markup.Escape(label)).Append(' ')
                  .Append("[/]");
            }
        }
        return new Markup(sb.ToString());
    }

    public void Write(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.Write(Render());
        console.WriteLine();
    }
}
