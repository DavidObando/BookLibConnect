using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

/// <summary>
/// End-to-end tests for <c>oahu-cli download</c>. Drives <see cref="RootCommandFactory"/>
/// with a <see cref="JobScheduler"/> backed by <see cref="FakeJobExecutor"/> so the test
/// surface exercises the full streaming + summary path without touching Core.
/// </summary>
[Collection("EnvVarSerial")]
public class DownloadCommandTests : IDisposable
{
    private readonly List<IAsyncDisposable> toDispose = new();

    public DownloadCommandTests()
    {
        CliServiceFactory.Reset();
    }

    public void Dispose()
    {
        foreach (var d in toDispose)
        {
            d.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        CliServiceFactory.Reset();
    }

    private void UseScheduler(IJobExecutor executor)
    {
        var sched = new JobScheduler(executor);
        toDispose.Add(sched);
        CliServiceFactory.JobServiceFactory = () => sched;
    }

    [Fact]
    public async Task NoArgs_ExitsTwo_AndPrintsHint()
    {
        UseScheduler(new FakeJobExecutor());
        var (exit, _, stderr) = await RunAsync("download");
        Assert.Equal(2, exit);
        Assert.Contains("no ASINs", stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidQuality_ExitsTwo()
    {
        UseScheduler(new FakeJobExecutor());
        var (exit, _, stderr) = await RunAsync("download", "B0001", "--quality", "ultra");
        Assert.Equal(2, exit);
        Assert.Contains("--quality", stderr);
    }

    [Fact]
    public async Task SingleAsin_Completes_Json_StreamsUpdatesAndSummary()
    {
        UseScheduler(new FakeJobExecutor(TimeSpan.FromMilliseconds(1)));
        var (exit, stdout, _) = await RunAsync("download", "B00ASIN1", "--json");
        Assert.Equal(0, exit);

        // Each JobUpdate becomes a line; the final document is the summary.
        Assert.Contains("\"resource\": \"download-update\"", stdout);
        Assert.Contains("\"phase\": \"Licensing\"", stdout);
        Assert.Contains("\"phase\": \"Downloading\"", stdout);
        Assert.Contains("\"phase\": \"Decrypting\"", stdout);
        Assert.Contains("\"phase\": \"Muxing\"", stdout);
        Assert.Contains("\"phase\": \"Completed\"", stdout);
        Assert.Contains("\"resource\": \"download-summary\"", stdout);
        Assert.Contains("\"completed\": 1", stdout);
        Assert.Contains("\"failed\": 0", stdout);
        Assert.Contains("\"asin\": \"B00ASIN1\"", stdout);
    }

    [Fact]
    public async Task FailingExecutor_ExitsOne_AndSummaryReportsFailure()
    {
        UseScheduler(new FakeJobExecutor(TimeSpan.FromMilliseconds(1), failAtDecrypt: true));
        var (exit, stdout, _) = await RunAsync("download", "B00DEAD", "--json");
        Assert.Equal(1, exit);
        Assert.Contains("\"failed\": 1", stdout);
        Assert.Contains("\"phase\": \"Failed\"", stdout);
    }

    [Fact]
    public async Task MultipleAsins_AllSucceed_ExitZero()
    {
        UseScheduler(new FakeJobExecutor(TimeSpan.FromMilliseconds(1)));
        var (exit, stdout, _) = await RunAsync("download", "B0001", "B0002", "B0003", "--json");
        Assert.Equal(0, exit);
        Assert.Contains("\"completed\": 3", stdout);
    }

    [Fact]
    public async Task DuplicateAsins_ArePresentedOnce()
    {
        UseScheduler(new FakeJobExecutor(TimeSpan.FromMilliseconds(1)));
        var (exit, stdout, _) = await RunAsync("download", "B0042", "b0042", "--json");
        Assert.Equal(0, exit);
        // Distinct (case-insensitive) — one job, one summary entry.
        Assert.Contains("\"completed\": 1", stdout);
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
