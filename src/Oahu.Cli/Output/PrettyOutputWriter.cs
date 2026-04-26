using System.Collections.Generic;
using System.Linq;
using Spectre.Console;

namespace Oahu.Cli.Output;

/// <summary>
/// Spectre-rendered tables with semantic colour. Falls back gracefully when the
/// underlying console doesn't emit ANSI (Spectre handles capability detection).
/// </summary>
public sealed class PrettyOutputWriter : IOutputWriter
{
    private readonly IAnsiConsole console;

    public PrettyOutputWriter(OutputContext context, IAnsiConsole console)
    {
        Context = context;
        this.console = console;
    }

    public OutputContext Context { get; }

    public void WriteResource(string resourceName, IReadOnlyDictionary<string, object?> data)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        foreach (var kv in data)
        {
            grid.AddRow(Markup.Escape(kv.Key), Markup.Escape(Format(kv.Value)));
        }
        SafeWrite(() => console.Write(grid));
    }

    public void WriteCollection(
        string resourceName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<OutputColumn> columns)
    {
        if (rows.Count == 0)
        {
            SafeWrite(() => console.MarkupLine("[grey](no items)[/]"));
            return;
        }

        var table = new Table().Border(Context.UseAscii ? TableBorder.Ascii : TableBorder.Rounded);
        foreach (var col in columns)
        {
            table.AddColumn(new Spectre.Console.TableColumn(Markup.Escape(col.Header)));
        }
        foreach (var row in rows)
        {
            table.AddRow(columns.Select(c => Markup.Escape(Format(row.TryGetValue(c.Key, out var v) ? v : null))).ToArray());
        }
        SafeWrite(() => console.Write(table));
    }

    public void WriteMessage(string message)
    {
        if (Context.Quiet)
        {
            return;
        }
        SafeWrite(() => console.WriteLine(message));
    }

    public void WriteSuccess(string message)
    {
        if (Context.Quiet)
        {
            return;
        }
        SafeWrite(() => console.MarkupLine($"[green]✓[/] {Markup.Escape(message)}"));
    }

    private static void SafeWrite(System.Action action)
    {
        try
        {
            action();
        }
        catch (System.IO.IOException)
        {
            // Broken pipe — downstream closed.
        }
        catch (System.ObjectDisposedException)
        {
            // Console torn down.
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        System.DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm"),
        System.DateTime dt => dt.ToString("yyyy-MM-dd HH:mm"),
        System.Collections.IEnumerable seq when value is not string => string.Join(", ", seq.Cast<object?>().Select(Format)),
        _ => value.ToString() ?? string.Empty,
    };
}
