using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Threading.Tasks;
using Oahu.Cli.App.Errors;
using Oahu.Cli.App.Library;
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
public static class QueueCommand
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
            var svc = CliServiceFactory.QueueServiceFactory();
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
            return ExitCodes.Success;
        });
        return c;
    }

    private static Command CreateAdd(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var asinArg = new Argument<string[]>("asin") { Arity = ArgumentArity.ZeroOrMore, Description = "One or more ASINs (use '-' to read one ASIN per line from stdin). Omit to search by --title." };
        var titleOpt = new Option<string?>("--title")
        {
            Description = "When ASINs are supplied: tag for the new entry. When no ASINs are supplied: case-insensitive substring search against the library; a single match is added.",
        };
        var c = new Command("add", "Add one or more ASINs to the queue, or resolve a single title via the library cache.") { asinArg, titleOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var asins = parse.GetValue(asinArg) ?? Array.Empty<string>();
            var title = parse.GetValue(titleOpt);
            var svc = CliServiceFactory.QueueServiceFactory();
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));

            // Title-based add: no positionals, --title supplied → search the library.
            if (asins.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(title))
                {
                    CliEnvironment.Error.WriteLine("oahu-cli: queue add requires an ASIN or --title.");
                    return ExitCodes.UsageError;
                }
                if (title.Length < 4)
                {
                    CliEnvironment.Error.WriteLine("oahu-cli: --title must be at least 4 characters when used as a search term.");
                    return ExitCodes.UsageError;
                }

                var library = CliServiceFactory.LibraryServiceFactory();
                var items = await library.ListAsync(new LibraryFilter { Search = title, AvailableOnly = true }, ct).ConfigureAwait(false);
                var matches = new List<LibraryItem>();
                foreach (var i in items)
                {
                    if ((i.Title?.IndexOf(title, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                        || (i.Subtitle?.IndexOf(title, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                    {
                        matches.Add(i);
                    }
                }
                if (matches.Count == 0)
                {
                    CliEnvironment.Error.WriteLine($"oahu-cli: no library item matches title '{title}'. Try `oahu-cli library sync` first.");
                    return ExitCodes.UsageError;
                }
                if (matches.Count > 1)
                {
                    CliEnvironment.Error.WriteLine($"oahu-cli: '{title}' matched {matches.Count} items. Disambiguate with --asin:");
                    foreach (var m in matches.Take(10))
                    {
                        CliEnvironment.Error.WriteLine($"  {m.Asin}  {m.Title}");
                    }
                    if (matches.Count > 10)
                    {
                        CliEnvironment.Error.WriteLine($"  … and {matches.Count - 10} more.");
                    }
                    return ExitCodes.UsageError;
                }
                var only = matches[0];
                var ok = await svc.AddAsync(new QueueEntry { Asin = only.Asin, Title = only.Title }, ct).ConfigureAwait(false);
                writer.WriteResource("queue-add-result", new Dictionary<string, object?>
                {
                    ["added"] = ok ? 1 : 0,
                    ["skipped"] = ok ? 0 : 1,
                    ["resolvedAsin"] = only.Asin,
                    ["resolvedTitle"] = only.Title,
                });
                return ExitCodes.Success;
            }

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

            writer.WriteResource("queue-add-result", new Dictionary<string, object?>
            {
                ["added"] = added,
                ["skipped"] = skipped,
            });
            return ExitCodes.Success;
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
            var svc = CliServiceFactory.QueueServiceFactory();
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));

            if (globals.DryRun)
            {
                var items = await svc.ListAsync(ct).ConfigureAwait(false);
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in items)
                {
                    present.Add(e.Asin);
                }
                int wouldRemove = 0, wouldMiss = 0;
                foreach (var a in asins)
                {
                    if (present.Contains(a))
                    {
                        wouldRemove++;
                    }
                    else
                    {
                        wouldMiss++;
                    }
                }
                writer.WriteResource("queue-remove-plan", new Dictionary<string, object?>
                {
                    ["wouldRemove"] = wouldRemove,
                    ["wouldMiss"] = wouldMiss,
                });
                return ExitCodes.Success;
            }

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
            writer.WriteResource("queue-remove-result", new Dictionary<string, object?>
            {
                ["removed"] = removed,
                ["missing"] = missing,
            });
            return missing > 0 && removed == 0 ? ExitCodes.GenericFailure : ExitCodes.Success;
        });
        return c;
    }

    private static Command CreateClear(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var c = new Command("clear", "Remove every item from the queue.");
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var force = globals.Force;
            var svc = CliServiceFactory.QueueServiceFactory();
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));

            if (globals.DryRun)
            {
                var items = await svc.ListAsync(ct).ConfigureAwait(false);
                writer.WriteResource("queue-clear-plan", new Dictionary<string, object?>
                {
                    ["wouldRemove"] = items.Count,
                });
                return ExitCodes.Success;
            }

            if (!force && CliEnvironment.IsStdinTty && CliEnvironment.IsStdoutTty)
            {
                CliEnvironment.Out.Write("Clear the entire queue? [y/N] ");
                var line = Console.ReadLine();
                if (!string.Equals(line?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    CliEnvironment.Error.WriteLine("Aborted.");
                    return ExitCodes.Success;
                }
            }
            await svc.ClearAsync(ct).ConfigureAwait(false);
            writer.WriteSuccess("Queue cleared.");
            return ExitCodes.Success;
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
