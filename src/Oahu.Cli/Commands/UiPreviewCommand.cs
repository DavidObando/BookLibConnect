using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using Oahu.Cli.Tui.Hooks;
using Oahu.Cli.Tui.Themes;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Commands;

/// <summary>
/// Hidden helper command (<c>oahu-cli ui-preview</c>) that renders every Phase 2 widget
/// under the requested theme. Used by snapshot tests and by humans visually auditing
/// theme additions. Per design §13, this stays available post-1.0 (gated behind a
/// hidden / experimental flag) — we wire it as a regular but undocumented subcommand
/// here so System.CommandLine can still surface it via tab completion in dev shells.
/// </summary>
internal static class UiPreviewCommand
{
    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var themeOpt = new Option<string>("--theme")
        {
            Description = "Theme to render: " + string.Join(", ", Theme.AvailableNames()),
        };
        themeOpt.AcceptOnlyFromAmong(Theme.AvailableNames().ToArray());
        themeOpt.DefaultValueFactory = _ => "Default";

        var allThemesOpt = new Option<bool>("--all-themes")
        {
            Description = "Render the preview once per built-in theme.",
        };

        var cmd = new Command("ui-preview", "Render every TUI widget under the current theme (developer / snapshot tool).")
        {
            themeOpt,
            allThemesOpt,
        };
        cmd.Hidden = true;

        cmd.SetAction(parse =>
        {
            var globals = resolveGlobals(parse);
            var allThemes = parse.GetValue(allThemesOpt);
            var themeName = parse.GetValue(themeOpt) ?? "Default";

            var console = SpectreConsoleFactory.Create(globals);
            var themesToRender = allThemes
                ? Theme.Available.ToList()
                : new List<Theme> { ResolveTheme(themeName) };

            foreach (var theme in themesToRender)
            {
                Theme.Use(theme.Name);
                RenderPreview(console, theme.Name, globals.UseAscii);
                console.WriteLine();
            }

            // Reset to Default so the next command isn't affected by the preview.
            Theme.Reset();
            return 0;
        });

        return cmd;
    }

    private static Theme ResolveTheme(string name)
    {
        foreach (var t in Theme.Available)
        {
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }
        return Themes.Default;
    }

    private static void RenderPreview(IAnsiConsole console, string themeName, bool useAscii)
    {
        var rule = new Rule($"[bold]{Markup.Escape($"Theme: {themeName}")}[/]") { Justification = Justify.Left };
        console.Write(rule);
        console.WriteLine();

        // Hooks summary
        var size = new TerminalSize(console);
        var bp = Breakpoint.For(size);
        console.MarkupLine($"[grey]Width:[/] {size.Width}  [grey]Height:[/] {size.Height}  [grey]Breakpoint:[/] {bp}  [grey]SSH:[/] {SshDetector.IsSshSession()}  [grey]ScreenReader:[/] {ScreenReaderProbe.IsActive()}");
        console.WriteLine();

        // Icons
        console.MarkupLine("[bold]Icons[/]");
        var icons = new[]
        {
            Tui.Icons.Icons.Success, Tui.Icons.Icons.Error, Tui.Icons.Icons.Warning,
            Tui.Icons.Icons.Info, Tui.Icons.Icons.Disabled, Tui.Icons.Icons.Prompt,
            Tui.Icons.Icons.Filled, Tui.Icons.Icons.Working, Tui.Icons.Icons.Empty,
            Tui.Icons.Icons.ArrowRight, Tui.Icons.Icons.ArrowUp, Tui.Icons.Icons.ArrowDown,
        };
        foreach (var ic in icons)
        {
            console.Markup($"  [{ic.Color.Value.ToMarkup()}]{Markup.Escape(ic.Render(useAscii))}[/] {ic.ScreenReaderLabel}   ");
        }
        console.WriteLine();
        console.WriteLine();

        // StatusLine
        console.MarkupLine("[bold]StatusLine[/]");
        new StatusLine
        {
            Verb = "Authenticating",
            Hint = "Esc to cancel",
            Metric = "1.2 KB",
            UseAscii = useAscii,
        }.Write(console);
        console.WriteLine();

        // TimelineItems
        console.MarkupLine("[bold]TimelineItem (each row begins with the same 4-cell prefix — never reflows)[/]");
        new TimelineItem { Title = "Output dir writable", Description = "~/Music/Oahu/Downloads", State = TimelineState.Success, UseAscii = useAscii }.Write(console);
        new TimelineItem { Title = "Audible reachable", State = TimelineState.Loading, UseAscii = useAscii }.Write(console);
        new TimelineItem { Title = "Activation bytes cache", Description = "empty — will fetch on first decrypt", State = TimelineState.Warning, UseAscii = useAscii }.Write(console);
        new TimelineItem { Title = "Library cache", Description = "287 books, last sync 2 h ago", State = TimelineState.Info, UseAscii = useAscii, Detail = "→ run 'library sync' to refresh" }.Write(console);
        new TimelineItem { Title = "Decrypt failed", Description = "The Way of Kings", State = TimelineState.Error, UseAscii = useAscii }.Write(console);
        console.WriteLine();

        // SelectList
        console.MarkupLine("[bold]SelectList[/]");
        new SelectList<string>
        {
            Items = new[] { "Project Hail Mary", "The Three-Body Problem", "Artemis", "The Way of Kings" },
            Format = s => s,
            CursorIndex = 1,
            SelectedIndices = new HashSet<int> { 0, 2 },
            UseAscii = useAscii,
        }.Write(console);
        console.WriteLine();

        // StyledTable
        console.MarkupLine("[bold]StyledTable[/]");
        var table = StyledTable.Create(useAscii)
            .AddBoldColumn("Title")
            .AddBoldColumn("Author")
            .AddBoldColumn("Length", noWrap: true);
        table.AddRow("Project Hail Mary", "Andy Weir", "16h 10m");
        table.AddRow("The Three-Body Problem", "Liu Cixin", "13h 26m");
        console.Write(table);
        console.WriteLine();

        // HintBar
        console.MarkupLine("[bold]HintBar[/]");
        var hints = new HintBar { UseAscii = useAscii }
            .Add("1-6", "tabs")
            .Add("Tab", "next")
            .Add("/", "search")
            .Add(":", "palette")
            .Add("?", "help")
            .Add("Ctrl+C", "quit");
        hints.Write(console);
        console.WriteLine();

        // Dialog (composes everything above)
        console.MarkupLine("[bold]Dialog[/]");
        var dialogBody = new Markup($"[{Tui.Tokens.Tokens.TextPrimary.Value.ToMarkup()}]The Audible API returned 401 Unauthorized.[/]\n\n[{Tui.Tokens.Tokens.TextSecondary.Value.ToMarkup()}]Your session may have expired. Sign in again and retry.[/]");
        var dialogFooter = new HintBar { UseAscii = useAscii }
            .Add("Tab", "buttons")
            .Add("Enter", "activate")
            .Add("L", "logs")
            .Add("Esc", "dismiss");
        new Dialog
        {
            Title = "Couldn't sync library",
            Body = dialogBody,
            Footer = dialogFooter,
            UseAscii = useAscii,
        }.Write(console);
        console.WriteLine();
    }
}
