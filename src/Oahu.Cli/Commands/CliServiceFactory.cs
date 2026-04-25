using System;
using System.IO;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Core;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Paths;

namespace Oahu.Cli.Commands;

/// <summary>
/// Service-resolution seam for the auth/library/job commands.
///
/// 4b.1 wired both to the in-memory <see cref="FakeAuthService"/> /
/// <see cref="FakeLibraryService"/>. 4b.2 swaps the defaults to the
/// Core-backed wrappers (<see cref="CoreAuthService"/> /
/// <see cref="CoreLibraryService"/>) that talk to <c>Oahu.Core.AudibleClient</c>
/// against the GUI-shared profile config and library cache. 4c.1 adds
/// <see cref="JobServiceFactory"/> backed by <see cref="JobScheduler"/> +
/// <see cref="AudibleJobExecutor"/>. Tests override the factories to inject
/// seeded fakes.
/// </summary>
public static class CliServiceFactory
{
    private static readonly object Lock = new();
    private static IAuthService? authSingleton;
    private static ILibraryService? librarySingleton;
    private static IJobService? jobSingleton;

    public static Func<IAuthService> AuthServiceFactory { get; set; } = () =>
    {
        lock (Lock)
        {
            return authSingleton ??= new CoreAuthService();
        }
    };

    public static Func<ILibraryService> LibraryServiceFactory { get; set; } = () =>
    {
        lock (Lock)
        {
            return librarySingleton ??= new CoreLibraryService();
        }
    };

    /// <summary>
    /// Per-invocation override for <see cref="JobScheduler"/> parallelism.
    /// Set <i>before</i> the first call to <see cref="JobServiceFactory"/> resolves
    /// the singleton (e.g. by a command's SetAction handler before invoking
    /// <c>RunAsync</c>). Reset by <see cref="Reset"/>.
    /// </summary>
    public static int? OverrideMaxParallelism { get; set; }

    /// <summary>
    /// Resolves the process-singleton <see cref="IJobService"/>. Default:
    /// <see cref="JobScheduler"/> with <see cref="AudibleJobExecutor"/> and a
    /// <see cref="JsonlHistoryStore"/> at the same <c>history.jsonl</c> path
    /// the <c>history</c> command reads from. Tests override this factory to
    /// inject <c>FakeJobExecutor</c>.
    /// </summary>
    public static Func<IJobService> JobServiceFactory { get; set; } = () =>
    {
        lock (Lock)
        {
            if (jobSingleton is not null)
            {
                return jobSingleton;
            }

            var historyPath = Path.Combine(CliPaths.SharedUserDataDir, "history.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            var history = new JsonlHistoryStore(historyPath);
            var options = OverrideMaxParallelism is { } p
                ? new JobSchedulerOptions { MaxParallelism = p }
                : null;
            jobSingleton = new JobScheduler(new AudibleJobExecutor(), history, options);
            return jobSingleton;
        }
    };

    /// <summary>Test hook: drop cached singletons so the next resolve produces a fresh instance.</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            authSingleton = null;
            librarySingleton = null;
            if (jobSingleton is IAsyncDisposable iad)
            {
                _ = iad.DisposeAsync().AsTask();
            }
            jobSingleton = null;
            OverrideMaxParallelism = null;
        }
    }
}
