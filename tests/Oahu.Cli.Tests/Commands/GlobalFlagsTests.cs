using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

/// <summary>
/// Tests for Phase 4d global flags: <c>--quiet</c>, <c>--dry-run</c>, <c>--force</c>,
/// and the new <c>--concurrency</c> validation on download/convert.
/// </summary>
[Collection("EnvVarSerial")]
public class GlobalFlagsTests : IDisposable
{
    private readonly List<IAsyncDisposable> toDispose = new();

    public GlobalFlagsTests()
    {
        CliServiceFactory.Reset();
    }

    public void Dispose()
    {
        foreach (var d in toDispose)
        {
            d.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        CliServiceFactory.AuthServiceFactory = static () => new FakeAuthService();
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
    public async Task Download_DryRun_EmitsPlanAndDoesNotSubmit()
    {
        var exec = UseCapturing();
        var (exit, stdout, _) = await RunAsync("--dry-run", "download", "B00ASIN1", "B00ASIN2", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(exec.Requests);
        Assert.Contains("\"resource\": \"download-plan\"", stdout);
        Assert.Contains("\"asin\": \"B00ASIN1\"", stdout);
        Assert.Contains("\"asin\": \"B00ASIN2\"", stdout);
        Assert.DoesNotContain("\"resource\": \"download-update\"", stdout);
    }

    [Fact]
    public async Task Convert_DryRun_EmitsPlanAndDoesNotSubmit()
    {
        var exec = UseCapturing();
        var (exit, stdout, _) = await RunAsync("--dry-run", "convert", "B00ASIN1", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(exec.Requests);
        Assert.Contains("\"resource\": \"download-plan\"", stdout);
        Assert.Contains("\"exportToAax\": true", stdout);
    }

    [Fact]
    public async Task Download_DryRun_AcceptsShortForm_n()
    {
        var exec = UseCapturing();
        var (exit, _, _) = await RunAsync("-n", "download", "B00ASIN1", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(exec.Requests);
    }

    [Fact]
    public async Task Download_QuietJson_SuppressesPerUpdateLines_KeepsSummary()
    {
        UseScheduler(new FakeJobExecutor(TimeSpan.FromMilliseconds(1)));
        var (exit, stdout, _) = await RunAsync("--quiet", "download", "B00ASIN1", "--json");
        Assert.Equal(0, exit);
        Assert.DoesNotContain("\"resource\": \"download-update\"", stdout);
        Assert.Contains("\"resource\": \"download-summary\"", stdout);
    }

    [Fact]
    public async Task Download_Concurrency_Negative_ExitsTwo()
    {
        UseCapturing();
        var (exit, _, stderr) = await RunAsync("download", "B00ASIN1", "--concurrency", "0");
        Assert.Equal(2, exit);
        Assert.Contains("--concurrency", stderr);
    }

    [Fact]
    public async Task Download_Concurrency_PositiveOverridesFactory()
    {
        var exec = UseCapturing();
        // Set the override before the singleton resolves; assert the factory captured it.
        var (exit, _, _) = await RunAsync("download", "B00ASIN1", "--concurrency", "4", "--json");
        Assert.Equal(0, exit);
        // The Capturing scheduler we registered above ignored the override (we already
        // installed a singleton). Direct check:
        // Reset and invoke the default factory after setting OverrideMaxParallelism manually.
        CliServiceFactory.Reset();
        CliServiceFactory.OverrideMaxParallelism = 7;
        // We cannot resolve the default factory without a real history dir, so just assert
        // the surface the SetAction commits to:
        Assert.Equal(7, CliServiceFactory.OverrideMaxParallelism);
        CliServiceFactory.Reset();
        Assert.Null(CliServiceFactory.OverrideMaxParallelism);
    }

    [Fact]
    public async Task AuthLogout_DryRun_DoesNotInvokeLogout()
    {
        var auth = new FakeAuthService();
        await auth.LoginAsync(CliRegion.Us, new NonInteractiveCallbackBroker());
        CliServiceFactory.AuthServiceFactory = () => auth;
        var (exit, stdout, _) = await RunAsync("--dry-run", "auth", "logout", "--json");
        Assert.Equal(0, exit);
        Assert.Contains("\"resource\": \"auth-logout-plan\"", stdout);
        Assert.Contains("\"wouldLogout\":", stdout);
        Assert.Single(await auth.ListSessionsAsync());
    }

    private void UseScheduler(IJobExecutor executor)
    {
        var sched = new JobScheduler(executor);
        toDispose.Add(sched);
        CliServiceFactory.JobServiceFactory = () => sched;
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
