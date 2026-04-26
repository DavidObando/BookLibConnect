using System;
using System.IO;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Core;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Paths;
using Oahu.Cli.App.Queue;

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
    private static IQueueService? queueSingleton;
    private static IConfigService? configSingleton;

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

    public static Func<IConfigService> ConfigServiceFactory { get; set; } = () =>
    {
        lock (Lock)
        {
            if (configSingleton is not null)
            {
                return configSingleton;
            }
            CliPaths.EnsureDirectories();
            configSingleton = new JsonConfigService(CliPaths.ConfigFile);
            return configSingleton;
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
            var activeJobsPath = Path.Combine(CliPaths.SharedUserDataDir, "active-jobs.json");
            var options = new JobSchedulerOptions
            {
                MaxParallelism = OverrideMaxParallelism ?? 1,
                ActiveJobsStatePath = activeJobsPath,
            };
            jobSingleton = new JobScheduler(new AudibleJobExecutor(), history, options);
            return jobSingleton;
        }
    };

    /// <summary>
    /// Resolves the process-singleton <see cref="IQueueService"/>. Default:
    /// <see cref="JsonFileQueueService"/> at <c>&lt;SharedUserDataDir&gt;/queue.json</c>
    /// (shared with the GUI per design §7). Tests override this factory to inject
    /// <see cref="InMemoryQueueService"/>.
    /// </summary>
    public static Func<IQueueService> QueueServiceFactory { get; set; } = () =>
    {
        lock (Lock)
        {
            if (queueSingleton is not null)
            {
                return queueSingleton;
            }
            var path = Path.Combine(CliPaths.SharedUserDataDir, "queue.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            queueSingleton = new JsonFileQueueService(path);
            return queueSingleton;
        }
    };

    /// <summary>Test hook: drop cached singletons so the next resolve produces a fresh instance.</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            authSingleton = null;
            librarySingleton = null;
            queueSingleton = null;
            if (jobSingleton is IAsyncDisposable iad)
            {
                _ = iad.DisposeAsync().AsTask();
            }
            jobSingleton = null;
            configSingleton = null;
            OverrideMaxParallelism = null;
        }
    }
}
