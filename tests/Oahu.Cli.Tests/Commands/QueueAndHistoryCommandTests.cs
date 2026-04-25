using System;
using Oahu.Cli.App.Models;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

public class QueueAndHistoryCommandTests
{
    [Fact]
    public void QueueCommand_ToDictionary_HasStableKeys()
    {
        var entry = new QueueEntry { Asin = "A1", Title = "Book" };
        var dict = QueueCommand.ToDictionary(entry);
        Assert.Equal("A1", dict["asin"]);
        Assert.Equal("Book", dict["title"]);
        Assert.Equal("High", dict["quality"]);
        Assert.True(dict.ContainsKey("addedAt"));
        Assert.True(dict.ContainsKey("profileAlias"));
    }

    [Fact]
    public void HistoryCommand_ToDictionary_MapsTerminalPhaseToStatusString()
    {
        var rec = new JobRecord
        {
            Id = "j1",
            Asin = "A1",
            Title = "Book",
            TerminalPhase = JobPhase.Failed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = "boom",
        };
        var dict = HistoryCommand.ToDictionary(rec);
        Assert.Equal("j1", dict["id"]);
        Assert.Equal("Failed", dict["status"]);
        Assert.Equal("boom", dict["errorMessage"]);
    }
}
