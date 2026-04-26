using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Errors;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Paths;
using Oahu.Cli.Output;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli history list|show</c>. <c>retry</c> is wired in phase 4c when the
/// real job executor lands.
/// </summary>
public static class HistoryCommand
{
    public const string SchemaResource = "history";

    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var cmd = new Command("history", "Inspect completed job history.");
        cmd.Subcommands.Add(CreateList(resolveGlobals));
        cmd.Subcommands.Add(CreateShow(resolveGlobals));
        cmd.Subcommands.Add(CreateRetry(resolveGlobals));
        cmd.Subcommands.Add(CreateDelete(resolveGlobals));
        return cmd;
    }

    private static Command CreateDelete(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var keepOpt = new Option<int?>("--keep") { Description = "Keep at most N most-recent records overall (applied last)." };
        var beforeOpt = new Option<string?>("--before") { Description = "Delete records completed before this date (yyyy-MM-dd or ISO 8601)." };
        var asinOpt = new Option<string[]>("--asin") { Description = "Delete records for one or more ASINs.", Arity = ArgumentArity.ZeroOrMore };

        var c = new Command("delete", "Delete records from the history file.") { keepOpt, beforeOpt, asinOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var keep = parse.GetValue(keepOpt);
            var beforeRaw = parse.GetValue(beforeOpt);
            var asins = parse.GetValue(asinOpt) ?? Array.Empty<string>();

            DateTimeOffset? before = null;
            if (!string.IsNullOrEmpty(beforeRaw))
            {
                if (!DateTimeOffset.TryParse(beforeRaw, out var parsed))
                {
                    CliEnvironment.Error.WriteLine($"oahu-cli: --before '{beforeRaw}' is not a valid date.");
                    return ExitCodes.UsageError;
                }
                before = parsed;
            }

            if (keep is null && before is null && asins.Length == 0)
            {
                CliEnvironment.Error.WriteLine("oahu-cli: history delete requires at least one of --keep, --before, --asin.");
                return ExitCodes.UsageError;
            }
            if (keep is { } k && k < 0)
            {
                CliEnvironment.Error.WriteLine("oahu-cli: --keep must be >= 0.");
                return ExitCodes.UsageError;
            }

            var historyPath = Path.Combine(CliPaths.SharedUserDataDir, "history.jsonl");
            var store = new JsonlHistoryStore(historyPath);

            if (globals.DryRun)
            {
                int wouldDelete = 0;
                await foreach (var rec in store.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    var matchesAsin = asins.Length > 0 && asins.Any(a => string.Equals(a, rec.Asin, StringComparison.OrdinalIgnoreCase));
                    var matchesBefore = before is { } b && rec.CompletedAt < b;
                    if (matchesAsin || matchesBefore)
                    {
                        wouldDelete++;
                    }
                }
                var dryWriter = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
                dryWriter.WriteResource("history-delete-result", new Dictionary<string, object?>
                {
                    ["deleted"] = 0,
                    ["wouldDelete"] = wouldDelete,
                    ["dryRun"] = true,
                });
                return ExitCodes.Success;
            }

            var deleted = await store.DeleteAsync(asins, before, keep, ct).ConfigureAwait(false);

            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            writer.WriteResource("history-delete-result", new Dictionary<string, object?>
            {
                ["deleted"] = deleted,
            });
            return ExitCodes.Success;
        });
        return c;
    }

    public static IReadOnlyDictionary<string, object?> ToDictionary(JobRecord record) => new Dictionary<string, object?>
    {
        ["id"] = record.Id,
        ["asin"] = record.Asin,
        ["title"] = record.Title,
        ["status"] = record.TerminalPhase.ToString(),
        ["startedAt"] = record.StartedAt,
        ["completedAt"] = record.CompletedAt,
        ["errorMessage"] = record.ErrorMessage,
        ["profileAlias"] = record.ProfileAlias,
        ["quality"] = record.Quality?.ToString(),
    };

    private static Command CreateList(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var sinceOpt = new Option<string?>("--since") { Description = "Filter to records on or after this date (yyyy-MM-dd or ISO 8601 timestamp)." };
        var statusOpt = new Option<string?>("--status") { Description = "Filter by status: completed|failed|canceled|all (default: all)." };
        var limitOpt = new Option<int?>("--limit") { Description = "Limit the number of records returned (most recent first when set)." };

        var c = new Command("list", "List terminal job records.") { sinceOpt, statusOpt, limitOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            DateTimeOffset? since = null;
            var sinceRaw = parse.GetValue(sinceOpt);
            if (!string.IsNullOrEmpty(sinceRaw))
            {
                if (!DateTimeOffset.TryParse(sinceRaw, out var parsed))
                {
                    CliEnvironment.Error.WriteLine($"oahu-cli: --since '{sinceRaw}' is not a valid date.");
                    return ExitCodes.UsageError;
                }
                since = parsed;
            }
            var status = parse.GetValue(statusOpt);
            var limit = parse.GetValue(limitOpt);

            var store = new JsonlHistoryStore(HistoryPath());
            var records = new List<JobRecord>();
            await foreach (var rec in store.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (since is not null && rec.CompletedAt < since)
                {
                    continue;
                }
                if (!StatusMatches(rec.TerminalPhase, status))
                {
                    continue;
                }
                records.Add(rec);
            }

            if (limit is int n && n > 0 && records.Count > n)
            {
                records = records
                    .OrderByDescending(r => r.CompletedAt)
                    .Take(n)
                    .OrderBy(r => r.CompletedAt)
                    .ToList();
            }

            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var rows = records.Select(ToDictionary).ToList();
            writer.WriteCollection(SchemaResource, rows, new[]
            {
                new OutputColumn("id", "Id"),
                new OutputColumn("asin", "ASIN"),
                new OutputColumn("title", "Title"),
                new OutputColumn("status", "Status"),
                new OutputColumn("completedAt", "Completed"),
            });
            return ExitCodes.Success;
        });
        return c;
    }

    private static Command CreateShow(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var idArg = new Argument<string>("jobId") { Description = "Job id to show." };
        var c = new Command("show", "Show a single history record by id.") { idArg };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var id = parse.GetValue(idArg)!;
            var store = new JsonlHistoryStore(HistoryPath());
            JobRecord? hit = null;
            await foreach (var rec in store.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (string.Equals(rec.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    hit = rec;
                    break;
                }
            }
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            if (hit is null)
            {
                CliEnvironment.Error.WriteLine($"oahu-cli: no history record with id '{id}'.");
                return ExitCodes.GenericFailure;
            }
            writer.WriteResource("history-record", ToDictionary(hit));
            return ExitCodes.Success;
        });
        return c;
    }

    public static string HistoryPath() => Path.Combine(CliPaths.SharedUserDataDir, "history.jsonl");

    private static Command CreateRetry(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var idArg = new Argument<string>("jobId") { Description = "Id of a past job to resubmit." };
        var c = new Command(
            "retry",
            "Resubmit a past job as a brand-new download. Reuses the recorded ASIN, title, and quality (if known); a fresh job id is assigned.")
        { idArg };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var id = parse.GetValue(idArg)!;
            var store = new JsonlHistoryStore(HistoryPath());
            JobRecord? hit = null;
            await foreach (var rec in store.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (string.Equals(rec.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    hit = rec;
                    break;
                }
            }
            if (hit is null)
            {
                CliEnvironment.Error.WriteLine($"oahu-cli: no history record with id '{id}'.");
                return ExitCodes.GenericFailure;
            }

            var request = new JobRequest
            {
                Asin = hit.Asin,
                Title = hit.Title,
                ProfileAlias = hit.ProfileAlias,
                Quality = hit.Quality ?? DownloadQuality.High,
            };

            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var jobService = CliServiceFactory.JobServiceFactory();
            return await DownloadCommand.RunAsync(jobService, new[] { request }, writer, ct).ConfigureAwait(false);
        });
        return c;
    }

    private static bool StatusMatches(JobPhase phase, string? wanted)
    {
        if (string.IsNullOrEmpty(wanted) || wanted.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return wanted.ToLowerInvariant() switch
        {
            "completed" => phase == JobPhase.Completed,
            "failed" => phase == JobPhase.Failed,
            "canceled" or "cancelled" => phase == JobPhase.Canceled,
            _ => false,
        };
    }
}
