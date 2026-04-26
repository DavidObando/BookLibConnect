using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Oahu.Cli.Output;

/// <summary>
/// Emits documents conforming to <c>docs/cli-schemas/*.schema.json</c>.
/// Every document includes a top-level <c>_schemaVersion</c> string per design §9.
/// </summary>
public sealed class JsonOutputWriter : IOutputWriter
{
    public const string SchemaVersion = "1";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TextWriter writer;

    public JsonOutputWriter(OutputContext context, TextWriter writer)
    {
        Context = context;
        this.writer = writer;
    }

    public OutputContext Context { get; }

    public void WriteResource(string resourceName, IReadOnlyDictionary<string, object?> data)
    {
        var obj = new JsonObject
        {
            ["_schemaVersion"] = SchemaVersion,
            ["resource"] = resourceName,
        };
        foreach (var kv in data)
        {
            obj[kv.Key] = ToNode(kv.Value);
        }
        SafeWrite(() => writer.WriteLine(obj.ToJsonString(WriteOptions)));
    }

    public void WriteCollection(
        string resourceName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<OutputColumn> columns)
    {
        var array = new JsonArray();
        foreach (var row in rows)
        {
            var item = new JsonObject();
            foreach (var kv in row)
            {
                item[kv.Key] = ToNode(kv.Value);
            }
            array.Add(item);
        }

        var obj = new JsonObject
        {
            ["_schemaVersion"] = SchemaVersion,
            ["resource"] = resourceName,
            ["count"] = rows.Count,
            ["items"] = array,
        };
        SafeWrite(() => writer.WriteLine(obj.ToJsonString(WriteOptions)));
    }

    public void WriteMessage(string message)
    {
        // JSON mode keeps stdout free of free-form prose; messages go to stderr only when needed.
        // Quiet always suppresses; we deliberately never inject prose into the JSON document.
        if (Context.Quiet)
        {
            return;
        }
        SafeWrite(() => Console.Error.WriteLine(message));
    }

    public void WriteSuccess(string message) => WriteMessage(message);

    private static void SafeWrite(Action action)
    {
        try
        {
            action();
        }
        catch (IOException)
        {
            // Broken pipe — downstream consumer closed the stream.
        }
        catch (ObjectDisposedException)
        {
            // Output stream torn down.
        }
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        DateTimeOffset dto => JsonValue.Create(dto.ToString("O")),
        DateTime dt => JsonValue.Create(dt.ToString("O")),
        Enum e => JsonValue.Create(e.ToString()),
        JsonNode n => n,
        IReadOnlyDictionary<string, object?> dict => DictToNode(dict),
        System.Collections.IEnumerable seq => SeqToNode(seq),
        _ => JsonValue.Create(value.ToString()),
    };

    private static JsonObject DictToNode(IReadOnlyDictionary<string, object?> dict)
    {
        var obj = new JsonObject();
        foreach (var kv in dict)
        {
            obj[kv.Key] = ToNode(kv.Value);
        }
        return obj;
    }

    private static JsonArray SeqToNode(System.Collections.IEnumerable seq)
    {
        var arr = new JsonArray();
        foreach (var item in seq)
        {
            arr.Add(ToNode(item));
        }
        return arr;
    }
}
