using System;
using Spectre.Console;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// Thin wrapper over Spectre's <see cref="Table"/> that defaults to the design
/// system's borders, header style, and column-overflow policy. Use this instead
/// of constructing a <see cref="Table"/> directly so a future theme change picks
/// up tables for free.
/// </summary>
public static class StyledTable
{
    public static Table Create(bool useAscii = false)
    {
        var t = new Table
        {
            Border = useAscii ? TableBorder.Ascii : TableBorder.Rounded,
            BorderStyle = new Style(Tokens.Tokens.BorderNeutral),
            ShowRowSeparators = false,
            Expand = false,
        };
        return t;
    }

    /// <summary>Add a column whose header uses the primary text token in bold.</summary>
    public static Table AddBoldColumn(this Table table, string header, bool noWrap = false)
    {
        ArgumentNullException.ThrowIfNull(table);
        var col = new TableColumn($"[bold {Tokens.Tokens.TextPrimary.Value.ToMarkup()}]{Markup.Escape(header)}[/]");
        if (noWrap)
        {
            col.NoWrap();
        }
        table.AddColumn(col);
        return table;
    }
}
