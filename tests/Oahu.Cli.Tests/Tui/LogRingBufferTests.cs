using System;
using Microsoft.Extensions.Logging;
using Oahu.Cli.Tui.Logging;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

public class LogRingBufferTests
{
    [Fact]
    public void Append_Then_Snapshot_Is_Chronological()
    {
        var buf = new LogRingBuffer(capacity: 3);
        buf.Append(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Information, "cat", "one", null));
        buf.Append(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Warning, "cat", "two", null));
        buf.Append(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Error, "cat", "three", null));

        var snap = buf.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal("one", snap[0].Message);
        Assert.Equal("two", snap[1].Message);
        Assert.Equal("three", snap[2].Message);
    }

    [Fact]
    public void Append_Beyond_Capacity_Drops_Oldest()
    {
        var buf = new LogRingBuffer(capacity: 2);
        buf.Append(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Information, "cat", "one", null));
        buf.Append(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Information, "cat", "two", null));
        buf.Append(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Information, "cat", "three", null));

        var snap = buf.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Equal("two", snap[0].Message);
        Assert.Equal("three", snap[1].Message);
    }

    [Fact]
    public void Logger_Provider_Drops_Below_Minimum_Level()
    {
        var buf = new LogRingBuffer(capacity: 8, minimumLevel: LogLevel.Warning);
        var logger = buf.CreateLogger("x");
        logger.LogInformation("nope");
        logger.LogWarning("yes");
        logger.LogError("yes2");

        var snap = buf.Snapshot();
        Assert.Equal(2, snap.Count);
        Assert.Equal("yes", snap[0].Message);
        Assert.Equal("yes2", snap[1].Message);
    }

    [Fact]
    public void Clear_Drops_All_Entries()
    {
        var buf = new LogRingBuffer(capacity: 4);
        buf.Append(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Information, "x", "a", null));
        buf.Append(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Information, "x", "b", null));
        buf.Clear();
        Assert.Empty(buf.Snapshot());
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void FormatLine_Includes_Level_And_Message()
    {
        var entry = new LogEntry(
            new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero),
            LogLevel.Warning,
            "Cat",
            "Hello",
            null);
        var line = entry.FormatLine();
        Assert.Contains("WRN", line);
        Assert.Contains("Cat", line);
        Assert.Contains("Hello", line);
    }
}
