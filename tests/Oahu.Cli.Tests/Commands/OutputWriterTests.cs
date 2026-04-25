using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Oahu.Cli.Output;
using Spectre.Console.Testing;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

public class OutputWriterTests
{
    [Fact]
    public void ResolveFormat_RespectsJsonAndPlain()
    {
        Assert.Equal(OutputFormat.Json, OutputContext.ResolveFormat(jsonFlag: true, plainFlag: false, stdoutIsRedirected: false));
        Assert.Equal(OutputFormat.Plain, OutputContext.ResolveFormat(false, true, false));
        Assert.Equal(OutputFormat.Pretty, OutputContext.ResolveFormat(false, false, false));
        Assert.Equal(OutputFormat.Plain, OutputContext.ResolveFormat(false, false, stdoutIsRedirected: true));
        Assert.Equal(OutputFormat.Json, OutputContext.ResolveFormat(true, true, false));
    }

    [Fact]
    public void Json_WriteResource_IncludesSchemaVersionAndResource()
    {
        var sw = new StringWriter();
        var w = new JsonOutputWriter(new OutputContext(OutputFormat.Json, false, false, false), sw);
        w.WriteResource("config-value", new Dictionary<string, object?> { ["key"] = "max-parallel-jobs", ["value"] = 5 });

        var node = JsonNode.Parse(sw.ToString())!;
        Assert.Equal("1", (string?)node["_schemaVersion"]);
        Assert.Equal("config-value", (string?)node["resource"]);
        Assert.Equal("max-parallel-jobs", (string?)node["key"]);
        Assert.Equal(5, (int?)node["value"]);
    }

    [Fact]
    public void Json_WriteCollection_IncludesCountAndItems()
    {
        var sw = new StringWriter();
        var w = new JsonOutputWriter(new OutputContext(OutputFormat.Json, false, false, false), sw);
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["asin"] = "A1", ["title"] = "T1" },
            new Dictionary<string, object?> { ["asin"] = "A2", ["title"] = "T2" },
        };
        w.WriteCollection("queue", rows, new[] { new OutputColumn("asin"), new OutputColumn("title") });

        var node = JsonNode.Parse(sw.ToString())!;
        Assert.Equal("queue", (string?)node["resource"]);
        Assert.Equal(2, (int?)node["count"]);
        Assert.Equal(2, ((JsonArray)node["items"]!).Count);
        Assert.Equal("A2", (string?)node["items"]![1]!["asin"]);
    }

    [Fact]
    public void Plain_WritesTabSeparatedRowsWithHeader()
    {
        var sw = new StringWriter();
        var w = new PlainOutputWriter(new OutputContext(OutputFormat.Plain, false, false, false), sw);
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["a"] = "x", ["b"] = "y" },
        };
        w.WriteCollection("t", rows, new[] { new OutputColumn("a", "A"), new OutputColumn("b", "B") });

        var lines = sw.ToString().TrimEnd('\r', '\n').Split('\n');
        Assert.Equal("A\tB", lines[0].TrimEnd('\r'));
        Assert.Equal("x\ty", lines[1].TrimEnd('\r'));
    }

    [Fact]
    public void Pretty_WritesTableWithoutThrowing()
    {
        var console = new TestConsole().EmitAnsiSequences();
        var w = new PrettyOutputWriter(new OutputContext(OutputFormat.Pretty, false, true, false), console);
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["asin"] = "A1", ["title"] = "T1" },
        };
        w.WriteCollection("queue", rows, new[] { new OutputColumn("asin", "ASIN"), new OutputColumn("title", "Title") });
        Assert.Contains("ASIN", console.Output);
        Assert.Contains("A1", console.Output);
    }

    [Fact]
    public void Quiet_SuppressesMessages()
    {
        var sw = new StringWriter();
        var w = new PlainOutputWriter(new OutputContext(OutputFormat.Plain, quiet: true, false, false), sw);
        w.WriteSuccess("done");
        Assert.Empty(sw.ToString());
    }
}
