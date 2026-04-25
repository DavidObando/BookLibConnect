using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Paths;
using Oahu.Cli.App.Queue;
using Oahu.Cli.Output;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli queue list|add|remove|clear</c>.
///
/// 4a only accepts ASIN for <c>add</c>; phase 4b extends <c>add</c> to accept titles
/// (resolved through the library cache). The on-disk file is shared with the GUI
/// per design §7.
/// </summary>
internal static class QueueCommand
{
    public const string SchemaResource = "queue";

    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var cmd = new Command("queue", "Inspect and modify the shared download queue.");
        cmd.Subcommands.Add(CreateList(resolveGlobals));
        cmd.Subcommands.Add(CreateAdd(resolveGlobals));
        cmd.Subcommands.Add(CreateRemove(resolveGlobals));
        cmd.Subcommands.Add(CreateClear(resolveGlobals));
        return cmd;
    }

    public static IReadOnlyDictionary<string, object?> ToDictionary(QueueEntry entry) => new Dictionary<string, object?>
    {
        ["asin"] = entry.Asin,
        ["title"] = entry.Title,
        ["quality"] = entry.Quality.ToString(),
        ["addedAt"] = entry.AddedAt,
        ["profileAlias"] = entry.ProfileAlias,
    };

    private static Command CreateList(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var c = new Command("list", "List queued items.");
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var svc = new JsonFileQueueService(QueuePath());
            var items = await svc.ListAsync(ct).ConfigureAwait(false);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var rows = new List<IReadOnlyDictionary<string, object?>>(items.Count);
            foreach (var e in items)
            {
                rows.Add(ToDictionary(e));
            }
            writer.WriteCollection(SchemaResource, rows, new[]
            {
                new OutputColumn("asin", "ASIN"),
                new OutputColumn("title", "Title"),
                new OutputColumn("quality", "Quality"),
                new OutputColumn("addedAt", "Added"),
            });
            return 0;
        });
        return c;
    }

    private static Command CreateAdd(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var asinArg = new Argument<string[]>("asin") { Arity = ArgumentArity.OneOrMore, Description = "One or more ASINs (use '-' to read one ASIN per line from stdin)." };
        var titleOpt = new Option<string?>("--title")
        {
            Description = "Optional title (used when the library cache hasn't been synced yet). Only meaningful when adding a single ASIN.",
        };
        var c = new Command("add", "Add one or more ASINs to the queue.") { asinArg, titleOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var asins = parse.GetValue(asinArg) ?? Array.Empty<string>();
            var title = parse.GetValue(titleOpt);
            var svc = new JsonFileQueueService(QueuePath());
            var added = 0;
            var skipped = 0;

            foreach (var input in ExpandStdin(asins))
            {
                var trimmed = input.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }
                var entry = new QueueEntry
                {
                    Asin = trimmed,
                    Title = title ?? trimmed,
                };
                if (await svc.AddAsync(entry, ct).ConfigureAwait(false))
                {
                    added++;
                }
                else
                {
                    skipped++;
                }
            }

            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            writer.WriteResource("queue-add-result", new Dictionary<string, object?>
            {
                ["added"] = added,
                ["skipped"] = skipped,
            });
            return 0;
        });
        return c;
    }

    private static Command CreateRemove(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var asinArg = new Argument<string[]>("asin") { Arity = ArgumentArity.OneOrMore, Description = "One or more ASINs to remove." };
        var c = new Command("remove", "Remove items from the queue.") { asinArg };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var asins = parse.GetValue(asinArg) ?? Array.Empty<string>();
            var svc = new JsonFileQueueService(QueuePath());
            var removed = 0;
            var missing = 0;
            foreach (var asin in asins)
            {
                if (await svc.RemoveAsync(asin, ct).ConfigureAwait(false))
                {
                    removed++;
                }
                else
                {
                    missing++;
                }
            }
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            writer.WriteResource("queue-remove-result", new Dictionary<string, object?>
            {
                ["removed"] = removed,
                ["missing"] = missing,
            });
            return missing > 0 && removed == 0 ? 1 : 0;
        });
        return c;
    }

    private static Command CreateClear(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var forceOpt = new Option<bool>("--force", "-f") { Description = "Skip the confirmation prompt." };
        var c = new Command("clear", "Remove every item from the queue.") { forceOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var force = parse.GetValue(forceOpt);
            if (!force && CliEnvironment.IsStdinTty && CliEnvironment.IsStdoutTty)
            {
                CliEnvironment.Out.Write("Clear the entire queue? [y/N] ");
                var line = Console.ReadLine();
                if (!string.Equals(line?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    CliEnvironment.Error.WriteLine("Aborted.");
                    return 0;
                }
            }
            var svc = new JsonFileQueueService(QueuePath());
            await svc.ClearAsync(ct).ConfigureAwait(false);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            writer.WriteSuccess("Queue cleared.");
            return 0;
        });
        return c;
    }

    public static string QueuePath() => Path.Combine(CliPaths.SharedUserDataDir, "queue.json");

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
