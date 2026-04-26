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
        SafeWrite(() =>
        {
            foreach (var kv in data)
            {
                writer.Write(kv.Key);
                writer.Write('\t');
                writer.WriteLine(Format(kv.Value));
            }
        });
    }

    public void WriteCollection(
        string resourceName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<OutputColumn> columns)
    {
        SafeWrite(() =>
        {
            // Header row.
            writer.WriteLine(string.Join('\t', columns.Select(c => Escape(c.Header))));
            foreach (var row in rows)
            {
                writer.WriteLine(string.Join('\t', columns.Select(c => Format(row.TryGetValue(c.Key, out var v) ? v : null))));
            }
        });
    }

    public void WriteMessage(string message)
    {
        if (Context.Quiet)
        {
            return;
        }
        SafeWrite(() => Console.Error.WriteLine(message));
    }

    public void WriteSuccess(string message)
    {
        if (Context.Quiet)
        {
            return;
        }
        SafeWrite(() => Console.Error.WriteLine(message));
    }

    private static void SafeWrite(Action action)
    {
        try
        {
            action();
        }
        catch (IOException)
        {
            // Broken pipe (downstream `head`, `less`, etc. closed) — exit gracefully.
        }
        catch (ObjectDisposedException)
        {
            // Output stream torn down by host.
        }
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }
        if (s.IndexOfAny(new[] { '\\', '\t', '\n', '\r' }) < 0)
        {
            return s;
        }
        var sb = new System.Text.StringBuilder(s.Length + 4);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        DateTimeOffset dto => dto.ToString("O"),
        DateTime dt => dt.ToString("O"),
        System.Collections.IEnumerable seq when value is not string => string.Join(",", seq.Cast<object?>().Select(Format)),
        _ => Escape(value.ToString() ?? string.Empty),
    };
}
