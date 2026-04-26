using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Errors;
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
            Description = "Post-decrypt export: 'none' (default), 'aax', 'm4b', or 'both'.",
        };
        var outputDirOpt = new Option<string?>("--output-dir")
        {
            Description = "Override the export directory (only used with --export aax|m4b|both).",
        };
        var concurrencyOpt = new Option<int?>("--concurrency")
        {
            Description = "Maximum parallel downloads (default: 1). Must be >= 1.",
        };
        var noDecryptOpt = new Option<bool>("--no-decrypt")
        {
            Description = "Stop after the LocalLocked phase; leave the encrypted .aax on disk.",
        };
        var allNewOpt = new Option<bool>("--all-new")
        {
            Description = "Submit all library items not yet recorded as successfully downloaded.",
        };
        var limitOpt = new Option<int?>("--limit")
        {
            Description = "Cap the number of jobs submitted (only with --all-new). Default: 50.",
        };

        var cmd = new Command("download", "Download (and decrypt) one or more audiobooks by ASIN.")
        {
            asinArg,
            qualityOpt,
            profileOpt,
            fromQueueOpt,
            exportOpt,
            outputDirOpt,
            concurrencyOpt,
            noDecryptOpt,
            allNewOpt,
            limitOpt,
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
            var concurrency = parse.GetValue(concurrencyOpt);
            var noDecrypt = parse.GetValue(noDecryptOpt);
            var allNew = parse.GetValue(allNewOpt);
            var limit = parse.GetValue(limitOpt) ?? 50;
            if (concurrency is { } cVal && cVal < 1)
            {
                CliEnvironment.Error.WriteLine("oahu-cli: --concurrency must be >= 1.");
                return ExitCodes.UsageError;
            }
            if (limit < 1)
            {
                CliEnvironment.Error.WriteLine("oahu-cli: --limit must be >= 1.");
                return ExitCodes.UsageError;
            }
            if (allNew && (positional.Length > 0 || fromQueue))
            {
                CliEnvironment.Error.WriteLine("oahu-cli: --all-new is mutually exclusive with positional ASINs and --from-queue.");
                return ExitCodes.UsageError;
            }

            DownloadQuality? quality = null;
            if (!string.IsNullOrEmpty(qualityRaw))
            {
                if (!Enum.TryParse<DownloadQuality>(qualityRaw, ignoreCase: true, out var parsed))
                {
                    CliEnvironment.Error.WriteLine(
                        $"oahu-cli: --quality '{qualityRaw}' is not valid. Use one of: normal|high|extreme.");
                    return ExitCodes.UsageError;
                }
                quality = parsed;
            }

            bool exportToAax = false;
            bool exportToM4b = false;
            if (!string.IsNullOrEmpty(exportRaw))
            {
                switch (exportRaw.ToLowerInvariant())
                {
                    case "none":
                        break;
                    case "aax":
                        exportToAax = true;
                        break;
                    case "m4b":
                        exportToM4b = true;
                        break;
                    case "both":
                        exportToAax = true;
                        exportToM4b = true;
                        break;
                    default:
                        CliEnvironment.Error.WriteLine(
                            $"oahu-cli: --export '{exportRaw}' is not valid. Use one of: none|aax|m4b|both.");
                        return ExitCodes.UsageError;
                }
            }

            var requests = await ResolveRequestsAsync(positional, fromQueue, allNew, limit, profile, quality, exportToAax, exportToM4b, noDecrypt, outputDir, ct).ConfigureAwait(false);
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
                return ExitCodes.UsageError;
            }

            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));

            if (globals.DryRun)
            {
                EmitDryRunPlan(writer, requests);
                return ExitCodes.Success;
            }

            if (concurrency is { } cParallelism)
            {
                CliServiceFactory.OverrideMaxParallelism = cParallelism;
            }
            var jobService = CliServiceFactory.JobServiceFactory();

            return await RunAsync(jobService, requests, writer, ct).ConfigureAwait(false);
        });

        return cmd;
    }

    public static void EmitDryRunPlan(IOutputWriter writer, IReadOnlyList<JobRequest> requests)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>(requests.Count);
        foreach (var r in requests)
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["asin"] = r.Asin,
                ["title"] = r.Title,
                ["quality"] = r.Quality.ToString(),
                ["profile"] = r.ProfileAlias,
                ["exportToAax"] = r.ExportToAax,
                ["outputDir"] = r.OutputDir,
            });
        }
        writer.WriteCollection("download-plan", rows, new[]
        {
            new OutputColumn("asin", "ASIN"),
            new OutputColumn("title", "TITLE"),
            new OutputColumn("quality", "QUALITY"),
            new OutputColumn("exportToAax", "EXPORT"),
        });
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
            try
            {
                await jobService.SubmitAsync(req, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Submission failure (e.g. scheduler disposed, channel closed). Cancel the
                // observer so we don't hang waiting for terminals that will never arrive,
                // and surface the failure for this request.
                terminals[req.Id] = new JobUpdate { JobId = req.Id, Phase = JobPhase.Failed, Message = ex.Message };
                observerCts.Cancel();
            }
        }

        try
        {
            await observerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected if we cancelled above
        }
        finally
        {
            observerCts.Cancel();
        }

        WriteSummary(writer, requests, terminals);

        bool anyFailed = terminals.Values.Any(u => u.Phase == JobPhase.Failed);
        bool anyCanceled = terminals.Values.Any(u => u.Phase == JobPhase.Canceled);
        bool allCompleted = terminals.Count == ids.Count
            && terminals.Values.All(u => u.Phase == JobPhase.Completed);

        if (allCompleted)
        {
            return ExitCodes.Success;
        }
        if (anyFailed)
        {
            return ExitCodes.GenericFailure;
        }
        if (anyCanceled)
        {
            // 130 = SIGINT-style termination per design §10.
            return ExitCodes.Cancelled;
        }
        return ExitCodes.GenericFailure;
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
            // --quiet on JSON: still emit the final summary, but suppress per-update lines.
            if (writer.Context.Quiet)
            {
                return;
            }
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
        bool allNew,
        int limit,
        string? profileAlias,
        DownloadQuality? quality,
        bool exportToAax,
        bool exportToM4b,
        bool noDecrypt,
        string? outputDir,
        CancellationToken cancellationToken)
    {
        if (allNew)
        {
            return ResolveAllNewAsync(limit, profileAlias, quality, exportToAax, exportToM4b, noDecrypt, outputDir, cancellationToken);
        }

        if (fromQueue)
        {
            return ResolveFromQueueAsync(profileAlias, quality, exportToAax, exportToM4b, noDecrypt, outputDir, cancellationToken);
        }

        var inputs = ExpandStdin(positional)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var list = new List<JobRequest>(inputs.Length);
        foreach (var asin in inputs)
        {
            list.Add(BuildRequest(asin, asin, profileAlias, quality, exportToAax, exportToM4b, noDecrypt, outputDir));
        }
        return Task.FromResult<IReadOnlyList<JobRequest>>(list);
    }

    private static async Task<IReadOnlyList<JobRequest>> ResolveFromQueueAsync(
        string? profileAlias,
        DownloadQuality? quality,
        bool exportToAax,
        bool exportToM4b,
        bool noDecrypt,
        string? outputDir,
        CancellationToken cancellationToken)
    {
        var queuePath = QueueCommand.QueuePath();
        var svc = new Oahu.Cli.App.Queue.JsonFileQueueService(queuePath);
        var entries = await svc.ListAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<JobRequest>(entries.Count);
        foreach (var e in entries)
        {
            list.Add(BuildRequest(e.Asin, string.IsNullOrEmpty(e.Title) ? e.Asin : e.Title, profileAlias ?? e.ProfileAlias, quality ?? e.Quality, exportToAax, exportToM4b, noDecrypt, outputDir));
        }
        return list;
    }

    private static async Task<IReadOnlyList<JobRequest>> ResolveAllNewAsync(
        int limit,
        string? profileAlias,
        DownloadQuality? quality,
        bool exportToAax,
        bool exportToM4b,
        bool noDecrypt,
        string? outputDir,
        CancellationToken cancellationToken)
    {
        var library = CliServiceFactory.LibraryServiceFactory();
        var jobs = CliServiceFactory.JobServiceFactory();

        var items = await library.ListAsync(filter: null, cancellationToken).ConfigureAwait(false);
        var seenAsins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var rec in jobs.ReadHistoryAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rec.TerminalPhase == JobPhase.Completed)
            {
                seenAsins.Add(rec.Asin);
            }
        }

        var list = new List<JobRequest>();
        foreach (var item in items)
        {
            if (seenAsins.Contains(item.Asin))
            {
                continue;
            }
            list.Add(BuildRequest(item.Asin, string.IsNullOrEmpty(item.Title) ? item.Asin : item.Title, profileAlias, quality, exportToAax, exportToM4b, noDecrypt, outputDir));
            if (list.Count >= limit)
            {
                break;
            }
        }
        return list;
    }

    private static JobRequest BuildRequest(string asin, string title, string? profileAlias, DownloadQuality? quality, bool exportToAax, bool exportToM4b, bool noDecrypt, string? outputDir) => new()
    {
        Asin = asin,
        Title = title,
        ProfileAlias = profileAlias,
        Quality = quality ?? DownloadQuality.High,
        ExportToAax = exportToAax,
        ExportToM4b = exportToM4b,
        NoDecrypt = noDecrypt,
        OutputDir = outputDir,
    };

    private static IEnumerable<string> ExpandStdin(string[] inputs)
    {
        foreach (var input in inputs)
        {
            if (input == "-")
            {
                // Read entire stdin synchronously up-front. We are inside a synchronous
                // iterator, but stdin reads are bounded by the user's input — typically
                // pasted ASIN lists — so this does not introduce noticeable latency.
                while (true)
                {
                    string? line = null;
                    try
                    {
                        line = Console.In.ReadLine();
                    }
                    catch (IOException)
                    {
                        // Treat torn pipe as EOF.
                    }
                    if (line is null)
                    {
                        break;
                    }
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
