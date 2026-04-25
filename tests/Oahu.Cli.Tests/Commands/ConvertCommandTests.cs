using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

[Collection("EnvVarSerial")]
public class ConvertCommandTests : IDisposable
{
    private readonly List<IAsyncDisposable> toDispose = new();

    public ConvertCommandTests()
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

    private CapturingExecutor UseCapturing()
    {
        var exec = new CapturingExecutor();
        var sched = new JobScheduler(exec);
        toDispose.Add(sched);
        CliServiceFactory.JobServiceFactory = () => sched;
        return exec;
    }

    [Fact]
    public async Task NoArgs_ExitsTwo()
    {
        UseCapturing();
        var (exit, _, stderr) = await RunAsync("convert");
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task SingleAsin_SetsExportToAax_True()
    {
        var exec = UseCapturing();
        var (exit, stdout, _) = await RunAsync("convert", "B00ASIN1", "--json");
        Assert.Equal(0, exit);
        Assert.Single(exec.Requests);
        Assert.True(exec.Requests[0].ExportToAax);
        Assert.Equal("B00ASIN1", exec.Requests[0].Asin);
        Assert.Contains("\"resource\": \"download-summary\"", stdout);
    }

    [Fact]
    public async Task OutputDir_Is_Forwarded()
    {
        var exec = UseCapturing();
        var (exit, _, _) = await RunAsync("convert", "B00ASIN1", "--output-dir", "/tmp/out", "--json");
        Assert.Equal(0, exit);
        Assert.Equal("/tmp/out", exec.Requests[0].OutputDir);
    }

    [Fact]
    public async Task DownloadCommand_ExportFlag_Aax_SetsExportToAax()
    {
        var exec = UseCapturing();
        var (exit, _, _) = await RunAsync("download", "B00ASIN1", "--export", "aax", "--output-dir", "/tmp/d", "--json");
        Assert.Equal(0, exit);
        Assert.True(exec.Requests[0].ExportToAax);
        Assert.Equal("/tmp/d", exec.Requests[0].OutputDir);
    }

    [Fact]
    public async Task DownloadCommand_ExportFlag_Invalid_ExitsTwo()
    {
        UseCapturing();
        var (exit, _, stderr) = await RunAsync("download", "B00ASIN1", "--export", "wav");
        Assert.Equal(2, exit);
        Assert.Contains("--export", stderr);
    }

    [Fact]
    public async Task DownloadCommand_NoExportFlag_DefaultsFalse()
    {
        var exec = UseCapturing();
        var (exit, _, _) = await RunAsync("download", "B00ASIN1", "--json");
        Assert.Equal(0, exit);
        Assert.False(exec.Requests[0].ExportToAax);
    }

    private static async Task<(int exit, string stdout, string stderr)> RunAsync(params string[] args)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var origCliOut = Oahu.Cli.CliEnvironment.Out;
        var origCliErr = Oahu.Cli.CliEnvironment.Error;
        var sw = new StringWriter();
        var ew = new StringWriter();
        Console.SetOut(sw);
        Console.SetError(ew);
        Oahu.Cli.CliEnvironment.Out = sw;
        Oahu.Cli.CliEnvironment.Error = ew;
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
            Oahu.Cli.CliEnvironment.Out = origCliOut;
            Oahu.Cli.CliEnvironment.Error = origCliErr;
        }
    }

    private sealed class CapturingExecutor : IJobExecutor
    {
        public List<JobRequest> Requests { get; } = new();

        public async IAsyncEnumerable<JobUpdate> ExecuteAsync(
            JobRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests.Add(request);
            yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Licensing };
            await Task.Yield();
            yield return new JobUpdate { JobId = request.Id, Phase = JobPhase.Completed };
        }
    }
}
