using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Output;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli download &lt;asin&gt;...</c>.
///
/// Submits one or more ASINs to the <see cref="IJobService"/> and waits for
/// every job to reach a terminal phase. JSON mode streams one
/// <c>download-update</c> document per <see cref="JobUpdate"/> followed by a
/// final <c>download-summary</c>; Pretty/Plain emits a per-phase line per
/// ASIN and a summary table.
///
/// Exit codes (per design §10):
///   <c>0</c> all jobs completed successfully.
///   <c>1</c> at least one job ended in <see cref="JobPhase.Failed"/> or <see cref="JobPhase.Canceled"/>.
///   <c>2</c> usage error (handled by <see cref="ParseErrorRewriter"/>).
///   <c>3</c> no active profile / auth required (surfaced by the executor).
/// </summary>
public static class DownloadCommand
{
    public const string UpdateResource = "download-update";
    public const string SummaryResource = "download-summary";

    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var asinArg = new Argument<string[]>("asin")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "One or more ASINs to download. Use '-' to read one ASIN per line from stdin.",
        };
        var qualityOpt = new Option<string?>("--quality")
        {
            Description = "Download quality: normal|high|extreme. Defaults to user setting.",
        };
        var profileOpt = new Option<string?>("--profile")
        {
            Description = "Profile alias to use (defaults to the active profile).",
        };
        var fromQueueOpt = new Option<bool>("--from-queue")
        {
            Description = "Drain the shared download queue instead of taking ASIN positionals.",
        };
        var exportOpt = new Option<string?>("--export")
        {
            Description = "Post-decrypt export: 'none' (default) or 'aax'.",
        };
        var outputDirOpt = new Option<string?>("--output-dir")
        {
            Description = "Override the export directory (only used with --export aax).",
        };

        var cmd = new Command("download", "Download (and decrypt) one or more audiobooks by ASIN.")
        {
            asinArg,
            qualityOpt,
            profileOpt,
            fromQueueOpt,
            exportOpt,
            outputDirOpt,
        };

        cmd.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var positional = parse.GetValue(asinArg) ?? Array.Empty<string>();
            var fromQueue = parse.GetValue(fromQueueOpt);
            var profile = parse.GetValue(profileOpt);
            var qualityRaw = parse.GetValue(qualityOpt);
            var exportRaw = parse.GetValue(exportOpt);
            var outputDir = parse.GetValue(outputDirOpt);

            DownloadQuality? quality = null;
            if (!string.IsNullOrEmpty(qualityRaw))
            {
                if (!Enum.TryParse<DownloadQuality>(qualityRaw, ignoreCase: true, out var parsed))
                {
                    CliEnvironment.Error.WriteLine(
                        $"oahu-cli: --quality '{qualityRaw}' is not valid. Use one of: normal|high|extreme.");
                    return 2;
                }
                quality = parsed;
            }

            bool exportToAax = false;
            if (!string.IsNullOrEmpty(exportRaw))
            {
                switch (exportRaw.ToLowerInvariant())
                {
                    case "none":
                        exportToAax = false;
                        break;
                    case "aax":
                        exportToAax = true;
                        break;
                    default:
                        CliEnvironment.Error.WriteLine(
                            $"oahu-cli: --export '{exportRaw}' is not valid. Use one of: none|aax.");
                        return 2;
                }
            }

            var requests = await ResolveRequestsAsync(positional, fromQueue, profile, quality, exportToAax, outputDir, ct).ConfigureAwait(false);
            if (requests.Count == 0)
            {
                if (fromQueue)
                {
                    CliEnvironment.Error.WriteLine("oahu-cli: queue is empty.");
                }
                else
                {
                    CliEnvironment.Error.WriteLine("oahu-cli: no ASINs supplied. Pass ASINs as arguments or --from-queue.");
                }
                return 2;
            }

            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var jobService = CliServiceFactory.JobServiceFactory();

            return await RunAsync(jobService, requests, writer, ct).ConfigureAwait(false);
        });

        return cmd;
    }

    public static async Task<int> RunAsync(
        IJobService jobService,
        IReadOnlyList<JobRequest> requests,
        IOutputWriter writer,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(requests.Select(r => r.Id), StringComparer.Ordinal);
        var titlesById = requests.ToDictionary(r => r.Id, r => r.Title, StringComparer.Ordinal);
        var asinsById = requests.ToDictionary(r => r.Id, r => r.Asin, StringComparer.Ordinal);
        var terminals = new ConcurrentDictionary<string, JobUpdate>(StringComparer.Ordinal);
        var lastPhase = new ConcurrentDictionary<string, JobPhase>(StringComparer.Ordinal);

        // Subscribe BEFORE submitting so we don't miss the Queued update.
        // Call ObserveAll synchronously here (not inside Task.Run) so the
        // subscriber is registered before the first SubmitAsync.
        using var observerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stream = jobService.ObserveAll(observerCts.Token);
        var observerTask = Task.Run(
            async () =>
            {
                await foreach (var u in stream.ConfigureAwait(false))
                {
                    if (!ids.Contains(u.JobId))
                    {
                        continue;
                    }

                    EmitUpdate(writer, u, asinsById, titlesById);

                    lastPhase[u.JobId] = u.Phase;
                    if (IsTerminal(u.Phase))
                    {
                        terminals[u.JobId] = u;
                        if (terminals.Count >= ids.Count)
                        {
                            break;
                        }
                    }
                }
            },
            CancellationToken.None);

        foreach (var req in requests)
        {
            await jobService.SubmitAsync(req, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await observerTask.ConfigureAwait(false);
        }
        finally
        {
            observerCts.Cancel();
        }

        WriteSummary(writer, requests, terminals);

        bool anyFailed = terminals.Values.Any(u => u.Phase is JobPhase.Failed or JobPhase.Canceled);
        bool allCompleted = terminals.Count == ids.Count
            && terminals.Values.All(u => u.Phase == JobPhase.Completed);

        if (allCompleted)
        {
            return 0;
        }
        return anyFailed ? 1 : 1;
    }

    private static bool IsTerminal(JobPhase p) =>
        p is JobPhase.Completed or JobPhase.Failed or JobPhase.Canceled;

    private static void EmitUpdate(
        IOutputWriter writer,
        JobUpdate u,
        IReadOnlyDictionary<string, string> asinsById,
        IReadOnlyDictionary<string, string> titlesById)
    {
        var asin = asinsById.GetValueOrDefault(u.JobId, string.Empty);
        var title = titlesById.GetValueOrDefault(u.JobId, string.Empty);

        if (writer.Context.Format == OutputFormat.Json)
        {
            writer.WriteResource(UpdateResource, new Dictionary<string, object?>
            {
                ["jobId"] = u.JobId,
                ["asin"] = asin,
                ["title"] = title,
                ["phase"] = u.Phase.ToString(),
                ["progress"] = u.Progress,
                ["message"] = u.Message,
                ["timestamp"] = u.Timestamp,
            });
            return;
        }

        // Pretty/Plain: only print phase boundaries (not per-tick progress) so
        // the output stays readable. Progress is summarised at the end.
        if (u.Progress is null || u.Progress is 0 or 1
            || u.Phase is JobPhase.Queued or JobPhase.Completed or JobPhase.Failed or JobPhase.Canceled)
        {
            var label = $"[{title}] {u.Phase}";
            if (!string.IsNullOrEmpty(u.Message))
            {
                label += $" — {u.Message}";
            }
            writer.WriteMessage(label);
        }
    }

    private static void WriteSummary(
        IOutputWriter writer,
        IReadOnlyList<JobRequest> requests,
        IReadOnlyDictionary<string, JobUpdate> terminals)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>(requests.Count);
        int completed = 0, failed = 0, canceled = 0, missing = 0;
        foreach (var req in requests)
        {
            if (!terminals.TryGetValue(req.Id, out var term))
            {
                missing++;
                rows.Add(new Dictionary<string, object?>
                {
                    ["jobId"] = req.Id,
                    ["asin"] = req.Asin,
                    ["title"] = req.Title,
                    ["status"] = "Pending",
                    ["error"] = null,
                });
                continue;
            }
            switch (term.Phase)
            {
                case JobPhase.Completed: completed++; break;
                case JobPhase.Failed: failed++; break;
                case JobPhase.Canceled: canceled++; break;
            }
            rows.Add(new Dictionary<string, object?>
            {
                ["jobId"] = req.Id,
                ["asin"] = req.Asin,
                ["title"] = req.Title,
                ["status"] = term.Phase.ToString(),
                ["error"] = term.Phase == JobPhase.Failed ? term.Message : null,
            });
        }

        if (writer.Context.Format == OutputFormat.Json)
        {
            writer.WriteResource(SummaryResource, new Dictionary<string, object?>
            {
                ["completed"] = completed,
                ["failed"] = failed,
                ["canceled"] = canceled,
                ["pending"] = missing,
                ["jobs"] = rows,
            });
            return;
        }

        writer.WriteCollection(SummaryResource, rows, new[]
        {
            new OutputColumn("asin", "ASIN"),
            new OutputColumn("title", "Title"),
            new OutputColumn("status", "Status"),
            new OutputColumn("error", "Error"),
        });
        if (failed == 0 && canceled == 0 && missing == 0)
        {
            writer.WriteSuccess($"Downloaded {completed} item(s).");
        }
    }

    private static Task<IReadOnlyList<JobRequest>> ResolveRequestsAsync(
        string[] positional,
        bool fromQueue,
        string? profileAlias,
        DownloadQuality? quality,
        bool exportToAax,
        string? outputDir,
        CancellationToken cancellationToken)
    {
        if (fromQueue)
        {
            return ResolveFromQueueAsync(profileAlias, quality, exportToAax, outputDir, cancellationToken);
        }

        var inputs = ExpandStdin(positional)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var list = new List<JobRequest>(inputs.Length);
        foreach (var asin in inputs)
        {
            list.Add(BuildRequest(asin, asin, profileAlias, quality, exportToAax, outputDir));
        }
        return Task.FromResult<IReadOnlyList<JobRequest>>(list);
    }

    private static async Task<IReadOnlyList<JobRequest>> ResolveFromQueueAsync(
        string? profileAlias,
        DownloadQuality? quality,
        bool exportToAax,
        string? outputDir,
        CancellationToken cancellationToken)
    {
        var queuePath = QueueCommand.QueuePath();
        var svc = new Oahu.Cli.App.Queue.JsonFileQueueService(queuePath);
        var entries = await svc.ListAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<JobRequest>(entries.Count);
        foreach (var e in entries)
        {
            list.Add(BuildRequest(e.Asin, string.IsNullOrEmpty(e.Title) ? e.Asin : e.Title, profileAlias ?? e.ProfileAlias, quality ?? e.Quality, exportToAax, outputDir));
        }
        return list;
    }

    private static JobRequest BuildRequest(string asin, string title, string? profileAlias, DownloadQuality? quality, bool exportToAax, string? outputDir) => new()
    {
        Asin = asin,
        Title = title,
        ProfileAlias = profileAlias,
        Quality = quality ?? DownloadQuality.High,
        ExportToAax = exportToAax,
        OutputDir = outputDir,
    };

    private static IEnumerable<string> ExpandStdin(string[] inputs)
    {
        foreach (var input in inputs)
        {
            if (input == "-")
            {
                string? line;
                while ((line = Console.In.ReadLine()) is not null)
                {
                    yield return line;
                }
            }
            else
            {
                yield return input;
            }
        }
    }
}
