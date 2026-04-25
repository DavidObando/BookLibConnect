using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Oahu.Cli.Output;

/// <summary>
/// Tab-separated, no escapes, no colour, no Unicode borders.
/// Suitable for piping into awk/cut/jq-without-jq workflows.
/// </summary>
public sealed class PlainOutputWriter : IOutputWriter
{
    private readonly TextWriter writer;

    public PlainOutputWriter(OutputContext context, TextWriter writer)
    {
        Context = context;
        this.writer = writer;
    }

    public OutputContext Context { get; }

    public void WriteResource(string resourceName, IReadOnlyDictionary<string, object?> data)
    {
        foreach (var kv in data)
        {
            writer.Write(kv.Key);
            writer.Write('\t');
            writer.WriteLine(Format(kv.Value));
        }
    }

    public void WriteCollection(
        string resourceName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<OutputColumn> columns)
    {
        // Header row.
        writer.WriteLine(string.Join('\t', columns.Select(c => c.Header)));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join('\t', columns.Select(c => Format(row.TryGetValue(c.Key, out var v) ? v : null))));
        }
    }

    public void WriteMessage(string message)
    {
        if (Context.Quiet)
        {
            return;
        }
        Console.Error.WriteLine(message);
    }

    public void WriteSuccess(string message)
    {
        if (Context.Quiet)
        {
            return;
        }
        Console.Error.WriteLine(message);
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        DateTimeOffset dto => dto.ToString("O"),
        DateTime dt => dt.ToString("O"),
        System.Collections.IEnumerable seq when value is not string => string.Join(",", seq.Cast<object?>().Select(Format)),
        _ => value.ToString() ?? string.Empty,
    };
}
