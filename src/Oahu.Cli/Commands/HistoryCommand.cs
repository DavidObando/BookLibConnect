using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Paths;
using Oahu.Cli.Output;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli history list|show</c>. <c>retry</c> is wired in phase 4c when the
/// real job executor lands.
/// </summary>
internal static class HistoryCommand
{
    public const string SchemaResource = "history";

    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var cmd = new Command("history", "Inspect completed job history.");
        cmd.Subcommands.Add(CreateList(resolveGlobals));
        cmd.Subcommands.Add(CreateShow(resolveGlobals));
        return cmd;
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
                    return 2;
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
            return 0;
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
                return 1;
            }
            writer.WriteResource("history-record", ToDictionary(hit));
            return 0;
        });
        return c;
    }

    public static string HistoryPath() => Path.Combine(CliPaths.SharedUserDataDir, "history.jsonl");

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
