using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

/// <summary>
/// End-to-end tests for <c>oahu-cli history retry</c>. Drives the root command
/// against a <see cref="JobScheduler"/> backed by <see cref="FakeJobExecutor"/>
/// and a synthetic <see cref="JsonlHistoryStore"/>.
/// </summary>
[Collection("EnvVarSerial")]
public class HistoryRetryCommandTests : IDisposable
{
    private readonly string tempHistory;
    private readonly List<IAsyncDisposable> toDispose = new();

    public HistoryRetryCommandTests()
    {
        CliServiceFactory.Reset();
        tempHistory = Path.Combine(Path.GetTempPath(), $"oahu-cli-retry-{Guid.NewGuid():n}.jsonl");
    }

    public void Dispose()
    {
        foreach (var d in toDispose)
        {
            d.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        if (File.Exists(tempHistory))
        {
            File.Delete(tempHistory);
        }
        CliServiceFactory.Reset();
    }

    private void UseScheduler(IJobExecutor executor)
    {
        var sched = new JobScheduler(executor);
        toDispose.Add(sched);
        CliServiceFactory.JobServiceFactory = () => sched;
    }

    private void SeedHistory(params JobRecord[] records)
    {
        var store = new JsonlHistoryStore(tempHistory);
        foreach (var r in records)
        {
            store.Append(r);
        }
    }

    [Fact]
    public async Task Retry_UnknownId_ExitsOne()
    {
        UseScheduler(new FakeJobExecutor(TimeSpan.FromMilliseconds(1)));
        var (exit, _, stderr) = await RunAsync("history", "retry", "does-not-exist");
        Assert.Equal(1, exit);
        Assert.Contains("no history record", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JobRecord_Quality_Roundtrips_Through_JsonlHistoryStore()
    {
        var store = new JsonlHistoryStore(tempHistory);
        var rec = new JobRecord
        {
            Id = "abc123",
            Asin = "B0001",
            Title = "Hail Mary",
            TerminalPhase = JobPhase.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            ProfileAlias = "default",
            Quality = DownloadQuality.Extreme,
        };
        store.Append(rec);

        var read = ReadAll(store).Single();
        Assert.Equal(DownloadQuality.Extreme, read.Quality);
        Assert.Equal("abc123", read.Id);
    }

    [Fact]
    public void JobRecord_Without_Quality_Deserializes_As_Null()
    {
        // Pre-4c.2 records may lack the "quality" field. Use the live writer
        // to capture the canonical envelope, strip "quality", and re-read.
        var store = new JsonlHistoryStore(tempHistory);
        store.Append(new JobRecord
        {
            Id = "old1",
            Asin = "B0",
            Title = "T",
            TerminalPhase = JobPhase.Completed,
            StartedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2025-01-01T00:01:00Z"),
        });
        var raw = File.ReadAllText(tempHistory);
        // The default value of an optional `DownloadQuality?` is `null`, which
        // STJ may emit as `"quality":null`; normalize to "missing field".
        raw = System.Text.RegularExpressions.Regex.Replace(raw, ",\\s*\"quality\"\\s*:\\s*null", string.Empty);
        File.WriteAllText(tempHistory, raw);

        var read = ReadAll(store).Single();
        Assert.Null(read.Quality);
    }

    private static List<JobRecord> ReadAll(JsonlHistoryStore store)
    {
        var list = new List<JobRecord>();
        var enumerator = store.ReadAllAsync().GetAsyncEnumerator();
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                list.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        return list;
    }

    private static async Task<(int exit, string stdout, string stderr)> RunAsync(params string[] args)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var origCliOut = CliEnvironment.Out;
        var origCliErr = CliEnvironment.Error;
        var sw = new StringWriter();
        var ew = new StringWriter();
        Console.SetOut(sw);
        Console.SetError(ew);
        CliEnvironment.Out = sw;
        CliEnvironment.Error = ew;
        try
        {
            var root = RootCommandFactory.Create(() => Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            var parse = root.Parse(args);
            var exit = await parse.InvokeAsync();
            return (exit, sw.ToString(), ew.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
            CliEnvironment.Out = origCliOut;
            CliEnvironment.Error = origCliErr;
        }
    }
}
