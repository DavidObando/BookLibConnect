using System;
using System.Collections.Generic;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Queue;
using Oahu.Cli.Tui.Shell;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Screens;

/// <summary>The six default tabs.</summary>
public static class DefaultTabs
{
    /// <summary>Create placeholder tabs (Phase 6 fallback).</summary>
    public static IReadOnlyList<ITabScreen> Create() => new ITabScreen[]
    {
        new PlaceholderScreen
        {
            Title = "Home",
            NumberKey = '1',
            Heading = "Aloha.",
            Body = "Quick actions, recent events, and a status summary live here.",
        },
        new PlaceholderScreen
        {
            Title = "Library",
            NumberKey = '2',
            Heading = "Library",
            Body = "Searchable table of your Audible library with multi-select.",
        },
        new PlaceholderScreen
        {
            Title = "Queue",
            NumberKey = '3',
            Heading = "Queue",
            Body = "Ordered list of jobs waiting to run; reorder, remove, start.",
        },
        new PlaceholderScreen
        {
            Title = "Jobs",
            NumberKey = '4',
            Heading = "Jobs",
            Body = "Live in-flight jobs, with download / decrypt / mux / export progress.",
        },
        new PlaceholderScreen
        {
            Title = "History",
            NumberKey = '5',
            Heading = "History",
            Body = "Completed and failed jobs; re-run, view error, open file.",
        },
        new PlaceholderScreen
        {
            Title = "Settings",
            NumberKey = '6',
            Heading = "Settings",
            Body = "Edit configuration, switch theme, manage profiles.",
        },
    };

    /// <summary>
    /// Create real tabs with services wired in. Phase 8: Queue, Jobs, History
    /// are now real screens backed by <see cref="IQueueService"/> and
    /// <see cref="IJobService"/>.
    /// </summary>
    public static IReadOnlyList<ITabScreen> CreateReal(
        AppShellState state,
        Func<IAuthService> authServiceFactory,
        Func<ILibraryService> libraryServiceFactory,
        Func<IConfigService> configServiceFactory,
        Func<IQueueService> queueServiceFactory,
        Func<IJobService> jobServiceFactory)
    {
        return new ITabScreen[]
        {
            new HomeScreen(state, authServiceFactory, libraryServiceFactory),
            new LibraryScreen(state, libraryServiceFactory, queueServiceFactory),
            new QueueScreen(queueServiceFactory, jobServiceFactory),
            new JobsScreen(jobServiceFactory),
            new HistoryScreen(jobServiceFactory),
            new SettingsScreen(configServiceFactory),
        };
    }
}

/// <summary>
/// Placeholder screens for tabs not yet implemented.
/// </summary>
internal sealed class PlaceholderScreen : ITabScreen
{
    public required string Title { get; init; }

    public required char NumberKey { get; init; }

    public required string Heading { get; init; }

    public required string Body { get; init; }

    public IEnumerable<KeyValuePair<string, string?>> Hints { get; init; } =
        Array.Empty<KeyValuePair<string, string?>>();

    public IRenderable Render(int width, int height)
    {
        var rows = new List<IRenderable>
        {
            new Markup($"[{Tokens.Tokens.TextPrimary.Value.ToMarkup()} bold]{Markup.Escape(Heading)}[/]"),
            new Markup(string.Empty),
            new Markup($"[{Tokens.Tokens.TextSecondary.Value.ToMarkup()}]{Markup.Escape(Body)}[/]"),
            new Markup(string.Empty),
            new Markup($"[{Tokens.Tokens.TextTertiary.Value.ToMarkup()}](real content lands in a later phase)[/]"),
        };
        return new Padder(new Rows(rows)).Padding(2, 1, 2, 1);
    }

    public bool HandleKey(ConsoleKeyInfo key) => false;
}
