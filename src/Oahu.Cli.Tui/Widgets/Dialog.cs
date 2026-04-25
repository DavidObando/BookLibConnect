using System;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// Centred bordered modal: title bar, body, optional <see cref="HintBar"/> footer.
/// Renders to an <see cref="IRenderable"/> (a Spectre <see cref="Panel"/>) so it
/// composes inside any <see cref="Layout"/>.
/// </summary>
public sealed class Dialog
{
    public required string Title { get; init; }

    public required IRenderable Body { get; init; }

    public HintBar? Footer { get; init; }

    public bool UseAscii { get; init; }

    public IRenderable Render()
    {
        IRenderable inner = Body;
        if (Footer is not null)
        {
            var rows = new Rows(Body, new Markup(string.Empty), Footer.Render());
            inner = rows;
        }

        var panel = new Panel(inner)
        {
            Border = UseAscii ? BoxBorder.Ascii : BoxBorder.Rounded,
            Header = new PanelHeader($"[{Tokens.Tokens.TextPrimary.Value.ToMarkup()}] {Markup.Escape(Title)} [/]"),
            Padding = new Padding(2, 1, 2, 1),
            BorderStyle = new Style(Tokens.Tokens.BorderNeutral),
        };
        return panel;
    }

    public void Write(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.Write(Render());
    }
}
