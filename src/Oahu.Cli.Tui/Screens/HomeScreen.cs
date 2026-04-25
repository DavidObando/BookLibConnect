using System;
using System.Collections.Generic;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Library;
using Oahu.Cli.Tui.Shell;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Screens;

/// <summary>
/// Home screen (tab 1). Shows greeting, active profile summary, and quick
/// actions. Per design TUI-exploration §2.
/// </summary>
public sealed class HomeScreen : ITabScreen
{
    private readonly AppShellState state;
    private readonly Func<IAuthService> authServiceFactory;
    private readonly Func<ILibraryService> libraryServiceFactory;

    private bool loaded;
    private int libraryCount;
    private string? accountName;

    public HomeScreen(AppShellState state, Func<IAuthService> authServiceFactory, Func<ILibraryService> libraryServiceFactory)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.authServiceFactory = authServiceFactory ?? throw new ArgumentNullException(nameof(authServiceFactory));
        this.libraryServiceFactory = libraryServiceFactory ?? throw new ArgumentNullException(nameof(libraryServiceFactory));
    }

    public string Title => "Home";

    public char NumberKey => '1';

    /// <summary>Event raised when the user picks the "sign in" action.</summary>
    public Action? OnSignInRequested { get; set; }

    public IEnumerable<KeyValuePair<string, string?>> Hints
    {
        get
        {
            if (!state.IsSignedIn)
            {
                yield return new("s", "sign in");
            }
            yield return new("r", "refresh");
        }
    }

    public IRenderable Render(int width, int height)
    {
        var lines = new List<IRenderable>();

        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();

        lines.Add(new Markup($"[{brand} bold]Aloha.[/]"));
        lines.Add(new Markup(string.Empty));

        if (state.IsSignedIn)
        {
            lines.Add(new Markup($"[{primary}]Signed in as [bold]{Markup.Escape(state.ProfileDisplay)}[/][/]"));
            if (!string.IsNullOrEmpty(accountName))
            {
                lines.Add(new Markup($"[{secondary}]{Markup.Escape(accountName)}[/]"));
            }
            lines.Add(new Markup(string.Empty));
            lines.Add(new Markup($"[{secondary}]Library: {libraryCount} title{(libraryCount == 1 ? "" : "s")}[/]"));
            lines.Add(new Markup(string.Empty));
            lines.Add(new Markup($"[{tertiary}]Quick actions:[/]"));
            lines.Add(new Markup($"  [{brand}]2[/] [{secondary}]Browse library[/]"));
            lines.Add(new Markup($"  [{brand}]3[/] [{secondary}]View queue[/]"));
            lines.Add(new Markup($"  [{brand}]6[/] [{secondary}]Settings[/]"));
        }
        else
        {
            lines.Add(new Markup($"[{secondary}]You're not signed in yet.[/]"));
            lines.Add(new Markup(string.Empty));
            lines.Add(new Markup($"[{secondary}]Press [{brand}]s[/] to sign in to Audible, or use a subcommand:[/]"));
            lines.Add(new Markup($"  [{tertiary}]oahu-cli auth login --region us[/]"));
        }

        return new Padder(new Rows(lines)).Padding(2, 1, 2, 1);
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.S when key.Modifiers == 0:
                if (!state.IsSignedIn)
                {
                    OnSignInRequested?.Invoke();
                    return true;
                }
                break;
            case ConsoleKey.R when key.Modifiers == 0:
                Refresh();
                return true;
        }
        return false;
    }

    /// <summary>Refresh the summary data from the services.</summary>
    public void Refresh()
    {
        try
        {
            var auth = authServiceFactory();
            var session = auth.GetActiveAsync().GetAwaiter().GetResult();
            if (session is not null)
            {
                accountName = session.AccountName;
            }

            var lib = libraryServiceFactory();
            var items = lib.ListAsync().GetAwaiter().GetResult();
            libraryCount = items.Count;
            loaded = true;
        }
        catch
        {
            // Swallow — the TUI must not crash.
        }
    }

    /// <summary>Lazy-load data on first render.</summary>
    internal void EnsureLoaded()
    {
        if (!loaded)
        {
            Refresh();
        }
    }
}
