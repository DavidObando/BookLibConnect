using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Output;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli library list | sync | show</c>.
///
/// 4b.1 ships the command surface against <see cref="ILibraryService"/>. The default
/// resolver returns a <see cref="FakeLibraryService"/> seeded only by tests, so
/// on a clean machine <c>library list</c> prints an empty table until 4b.2
/// substitutes the Core-backed service that reads the GUI-shared library cache.
/// </summary>
internal static class LibraryCommand
{
    public const string ListSchemaResource = "library-list";
    public const string ShowSchemaResource = "library-show";

    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var cmd = new Command("library", "Browse the cached Audible library.");
        cmd.Subcommands.Add(CreateList(resolveGlobals));
        cmd.Subcommands.Add(CreateSync(resolveGlobals));
        cmd.Subcommands.Add(CreateShow(resolveGlobals));
        return cmd;
    }

    public static IReadOnlyDictionary<string, object?> ToDictionary(LibraryItem item) => new Dictionary<string, object?>
    {
        ["asin"] = item.Asin,
        ["title"] = item.Title,
        ["subtitle"] = item.Subtitle,
        ["authors"] = item.Authors,
        ["narrators"] = item.Narrators,
        ["series"] = item.Series,
        ["seriesPosition"] = item.SeriesPosition,
        ["runtimeMinutes"] = item.Runtime is { } rt ? (int?)Math.Round(rt.TotalMinutes) : null,
        ["purchaseDate"] = item.PurchaseDate,
        ["isAvailable"] = item.IsAvailable,
        ["hasMultiplePartFiles"] = item.HasMultiplePartFiles,
    };

    private static Command CreateList(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var filterOpt = new Option<string?>("--filter") { Description = "Substring match against title." };
        var authorOpt = new Option<string?>("--author") { Description = "Substring match against any author." };
        var seriesOpt = new Option<string?>("--series") { Description = "Exact match against the series name." };
        var unreadOpt = new Option<bool>("--unread")
        {
            Description = "Restrict to unread titles. (Not yet implemented — always errors in 4b.1.)",
        };
        var limitOpt = new Option<int?>("--limit") { Description = "Cap the number of results." };
        var allOpt = new Option<bool>("--all")
        {
            Description = "Include unavailable titles (defaults to available-only).",
        };

        var c = new Command("list", "List the cached library.") { filterOpt, authorOpt, seriesOpt, unreadOpt, limitOpt, allOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));

            if (parse.GetValue(unreadOpt))
            {
                CliEnvironment.Error.WriteLine("--unread is not implemented yet (oahu-cli phase 4b.2).");
                return 1;
            }

            var filter = new LibraryFilter
            {
                Search = parse.GetValue(filterOpt),
                Author = parse.GetValue(authorOpt),
                Series = parse.GetValue(seriesOpt),
                AvailableOnly = !parse.GetValue(allOpt),
            };

            var svc = CliServiceFactory.LibraryServiceFactory();
            IEnumerable<LibraryItem> items = await svc.ListAsync(filter, ct).ConfigureAwait(false);
            var limit = parse.GetValue(limitOpt);
            if (limit is { } n && n > 0)
            {
                items = items.Take(n);
            }

            var rows = items.Select(i => ToDictionary(i)).ToList();
            writer.WriteCollection(ListSchemaResource, rows, new[]
            {
                new OutputColumn("asin", "ASIN"),
                new OutputColumn("title", "Title"),
                new OutputColumn("authors", "Authors"),
                new OutputColumn("series", "Series"),
                new OutputColumn("runtimeMinutes", "Runtime (min)"),
                new OutputColumn("isAvailable", "Available"),
            });

            return 0;
        });
        return c;
    }

    private static Command CreateSync(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var profileOpt = new Option<string?>("--profile")
        {
            Description = "Profile alias whose library to sync (defaults to the active profile).",
        };
        var fullOpt = new Option<bool>("--full") { Description = "Force a full re-sync rather than an incremental refresh." };
        var c = new Command("sync", "Pull the latest library snapshot from Audible.") { profileOpt, fullOpt };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var alias = parse.GetValue(profileOpt);
            if (string.IsNullOrWhiteSpace(alias))
            {
                var auth = CliServiceFactory.AuthServiceFactory();
                var active = await auth.GetActiveAsync(ct).ConfigureAwait(false);
                if (active is null)
                {
                    CliEnvironment.Error.WriteLine("No active profile. Sign in with `oahu-cli auth login` first, or pass --profile.");
                    return 3;
                }
                alias = active.ProfileAlias;
            }

            try
            {
                var svc = CliServiceFactory.LibraryServiceFactory();
                var count = await svc.SyncAsync(alias!, ct).ConfigureAwait(false);
                writer.WriteResource("library-sync-result", new Dictionary<string, object?>
                {
                    ["profileAlias"] = alias,
                    ["itemCount"] = count,
                    ["full"] = parse.GetValue(fullOpt),
                });
                return 0;
            }
            catch (Exception ex)
            {
                CliEnvironment.Error.WriteLine($"Sync failed: {ex.Message}");
                return 4;
            }
        });
        return c;
    }

    private static Command CreateShow(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var asinArg = new Argument<string>("asin") { Description = "Book ASIN." };
        var c = new Command("show", "Show a single book's details.") { asinArg };
        c.SetAction(async (parse, ct) =>
        {
            var globals = resolveGlobals(parse);
            var writer = OutputWriterFactory.Create(ConfigCommand.BuildContext(globals));
            var asin = parse.GetValue(asinArg);
            var svc = CliServiceFactory.LibraryServiceFactory();
            var item = await svc.GetAsync(asin!, ct).ConfigureAwait(false);
            if (item is null)
            {
                CliEnvironment.Error.WriteLine($"No library entry with ASIN '{asin}'. Run `oahu-cli library sync` if you haven't recently.");
                return 1;
            }
            writer.WriteResource(ShowSchemaResource, ToDictionary(item));
            return 0;
        });
        return c;
    }
}
