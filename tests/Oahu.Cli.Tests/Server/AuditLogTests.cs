using System.Collections.Generic;
using System.IO;
using Oahu.Cli.Server.Audit;
using Xunit;

namespace Oahu.Cli.Tests.Server;

public sealed class AuditLogTests
{
    [Fact]
    public void HashArgs_Is_Deterministic_And_Order_Independent()
    {
        var a = new Dictionary<string, object?> { ["asin"] = "B0123", ["limit"] = 10 };
        var b = new Dictionary<string, object?> { ["limit"] = 10, ["asin"] = "B0123" };
        Assert.Equal(AuditLog.HashArgs(a), AuditLog.HashArgs(b));
        var c = new Dictionary<string, object?> { ["asin"] = "B9999", ["limit"] = 10 };
        Assert.NotEqual(AuditLog.HashArgs(a), AuditLog.HashArgs(c));
    }

    [Fact]
    public void HashArgs_Empty_Args_Is_Sha256_Of_Empty()
    {
        var h = AuditLog.HashArgs(null);
        Assert.StartsWith("sha256:", h);
        Assert.Equal("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", h);
    }

    [Fact]
    public void Write_Appends_One_Json_Line()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oahu-audit-{System.Guid.NewGuid():n}.jsonl");
        try
        {
            var log = new AuditLog(path);
            log.Write("http", "http", "library_list", new Dictionary<string, object?> { ["filter"] = "asimov" }, "ok", 12);
            log.Write("stdio", "stdio", "queue_add", new Dictionary<string, object?> { ["asins"] = new[] { "B1" } }, "denied", 1);
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"tool\":\"library_list\"", lines[0]);
            Assert.Contains("\"outcome\":\"ok\"", lines[0]);
            Assert.Contains("\"argsHash\":\"sha256:", lines[0]);
            Assert.DoesNotContain("asimov", lines[0]); // args must be hashed, not logged in clear.
            Assert.Contains("\"outcome\":\"denied\"", lines[1]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
