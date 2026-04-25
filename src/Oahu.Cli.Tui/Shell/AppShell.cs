using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oahu.Cli.Tui.Logging;
using Oahu.Cli.Tui.Screens;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Shell;

/// <summary>
/// The top-level TUI controller, per design §3.4 / §6 / §10 and the TUI-exploration §1.
///
/// Phase 6 deliverable: an empty-but-navigable shell.
///
///   • Header (app name, profile/region, activity verb, version)
///   • Tab strip (Home, Library, Queue, Jobs, History, Settings)
///   • Body (delegated to <see cref="ITabScreen.Render"/>)
///   • Pinned hint bar (global + per-screen hints)
///   • Logs overlay (toggled with <c>L</c>)
///   • Progressive Ctrl+C state machine
///   • Alt-screen entry / exit with full restoration on crash
///
/// Real screens for sign-in, library, jobs, etc. land in phases 7 and 8;
/// this class is a stable container that they plug into via
/// <see cref="ITabScreen"/>.
/// </summary>
public sealed class AppShell
{
    /// <summary>
    /// Source of key presses. The production path uses <see cref="ConsoleKeyReader"/>;
    /// tests inject deterministic key streams.
    /// </summary>
    public interface IKeyReader
    {
        /// <summary>Block until a key is available, then return it. Return null to signal EOF (exit).</summary>
        ConsoleKeyInfo? ReadKey();
    }

    private readonly IAnsiConsole console;
    private readonly AppShellOptions options;
    private readonly IReadOnlyList<ITabScreen> tabs;
    private readonly CtrlCState ctrlC;

    private int activeTab;
    private bool logsOpen;
    private string? toast;
    private DateTimeOffset? toastShownAt;

    public AppShell(IAnsiConsole console, AppShellOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        this.console = console;
        this.options = options ?? new AppShellOptions();
        tabs = this.options.Tabs ?? DefaultTabs.Create();
        if (tabs.Count == 0)
        {
            throw new ArgumentException("AppShell requires at least one tab.", nameof(options));
        }
        ctrlC = new CtrlCState();
    }

    public int ActiveTab => activeTab;

    public bool LogsOpen => logsOpen;

    public IReadOnlyList<ITabScreen> Tabs => tabs;

    /// <summary>
    /// Run the shell against an injected key reader. Returns the process exit code
    /// (always <c>0</c> for a clean quit; <c>130</c> for a Ctrl+C exit).
    /// </summary>
    public int Run(IKeyReader keyReader)
    {
        ArgumentNullException.ThrowIfNull(keyReader);
        Render();
        while (true)
        {
            var key = keyReader.ReadKey();
            if (key is null)
            {
                return 0;
            }
            var action = Dispatch(key.Value);
            switch (action)
            {
                case ShellAction.Continue:
                    Render();
                    break;
                case ShellAction.Exit:
                    return 0;
                case ShellAction.ExitSigInt:
                    return 130;
            }
        }
    }

    /// <summary>
    /// Dispatch a single key. Public for tests; production callers use
    /// <see cref="Run(IKeyReader)"/>.
    /// </summary>
    public ShellAction Dispatch(ConsoleKeyInfo key)
    {
        // Toast auto-clears on any key after the window has elapsed.
        if (!ctrlC.ToastActive)
        {
            toast = null;
            toastShownAt = null;
        }

        // Progressive Ctrl+C — handled before delegating to the active screen.
        var isCtrlC = key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0;
        if (isCtrlC)
        {
            switch (ctrlC.OnPress())
            {
                case CtrlCAction.CancelActiveJob:
                case CtrlCAction.CloseDialog:
                    // Phase 6 has no active jobs / dialogs; fall through to prompt.
                    toast = "Press Ctrl+C again to quit · Esc to stay";
                    toastShownAt = DateTimeOffset.UtcNow;
                    return ShellAction.Continue;
                case CtrlCAction.PromptToExit:
                    toast = "Press Ctrl+C again to quit · Esc to stay";
                    toastShownAt = DateTimeOffset.UtcNow;
                    return ShellAction.Continue;
                case CtrlCAction.Exit:
                    return ShellAction.ExitSigInt;
            }
        }

        // Logs overlay swallows its own input first.
        if (logsOpen)
        {
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                case ConsoleKey.L when (key.Modifiers & ConsoleModifiers.Control) == 0:
                    logsOpen = false;
                    return ShellAction.Continue;
            }
            return ShellAction.Continue;
        }

        // Esc clears toast / pending exit.
        if (key.Key == ConsoleKey.Escape)
        {
            if (toast is not null)
            {
                toast = null;
                toastShownAt = null;
                ctrlC.Reset();
                return ShellAction.Continue;
            }
        }

        // Number keys 1..9 jump to that tab.
        if (key.KeyChar >= '1' && key.KeyChar <= '9')
        {
            var idx = key.KeyChar - '1';
            if (idx < tabs.Count)
            {
                activeTab = idx;
                return ShellAction.Continue;
            }
        }

        switch (key.Key)
        {
            case ConsoleKey.Tab when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                activeTab = (activeTab - 1 + tabs.Count) % tabs.Count;
                return ShellAction.Continue;
            case ConsoleKey.Tab:
                activeTab = (activeTab + 1) % tabs.Count;
                return ShellAction.Continue;
            case ConsoleKey.L when (key.Modifiers & ConsoleModifiers.Control) != 0:
                // Ctrl+L: clear / redraw artifacts.
                AltScreen.Clear();
                return ShellAction.Continue;
            case ConsoleKey.L:
                if (options.LogBuffer is not null)
                {
                    logsOpen = true;
                }
                return ShellAction.Continue;
            case ConsoleKey.Q when key.Modifiers == 0:
                // Plain `q` is *not* a global quit (the active screen may use it).
                break;
        }

        // Delegate to the active screen.
        if (tabs[activeTab].HandleKey(key))
        {
            return ShellAction.Continue;
        }

        return ShellAction.Continue;
    }

    private void Render()
    {
        var screen = tabs[activeTab];
        var width = console.Profile.Width;
        var height = Math.Max(10, console.Profile.Height);
        // Leave room for chrome (header + tabs + spacers + hint bar = ~6 rows).
        var bodyHeight = Math.Max(5, height - 6);

        AltScreen.Clear();

        // Header.
        var headerText = BuildHeader(width);
        console.Write(new Markup(headerText));
        console.WriteLine();
        console.Write(new Rule { Style = new Style(Tokens.Tokens.BorderNeutral) });

        // Tabs.
        new TabStrip
        {
            Titles = tabs.Select(t => t.Title).ToArray(),
            ActiveIndex = activeTab,
            UseAscii = options.UseAscii,
        }.Write(console);
        console.Write(new Rule { Style = new Style(Tokens.Tokens.BorderNeutral) });

        // Body (or logs overlay).
        if (logsOpen && options.LogBuffer is { } buf)
        {
            console.Write(RenderLogsOverlay(buf, width, bodyHeight));
        }
        else
        {
            console.Write(screen.Render(width, bodyHeight));
        }

        // Hint bar — global + per-screen + toast.
        console.WriteLine();
        console.Write(new Rule { Style = new Style(Tokens.Tokens.BorderNeutral) });

        if (toast is not null)
        {
            var c = Tokens.Tokens.StatusWarning.Value.ToMarkup();
            console.Write(new Markup($"[{c}] ! {Markup.Escape(toast)}[/]"));
            console.WriteLine();
        }
        else
        {
            BuildHintBar(screen).Write(console);
        }
    }

    private string BuildHeader(int width)
    {
        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();

        var profile = string.IsNullOrEmpty(options.Profile)
            ? "(not signed in)"
            : !string.IsNullOrEmpty(options.Region)
                ? $"{options.Profile}@{options.Region}"
                : options.Profile;

        var verb = options.ActivityVerb?.Invoke();
        if (string.IsNullOrEmpty(verb))
        {
            verb = "idle";
        }
        var version = string.IsNullOrEmpty(options.Version) ? string.Empty : $"v{options.Version}";

        return string.Concat(
            $"[{brand} bold]oahu[/]",
            $"  [{tertiary}]·[/]  ",
            $"[{secondary}]{Markup.Escape(profile)}[/]",
            $"  [{tertiary}]·[/]  ",
            $"[{primary}]{Markup.Escape(verb)}[/]",
            $"  [{tertiary}]·[/]  ",
            $"[{tertiary}]{Markup.Escape(version)}[/]");
    }

    private HintBar BuildHintBar(ITabScreen screen)
    {
        var bar = new HintBar { UseAscii = options.UseAscii }
            .Add("1-6", "tabs")
            .Add("Tab", "next")
            .Add("?", "help")
            .Add("L", options.LogBuffer is not null ? "logs" : null)
            .Add("Ctrl+C", "quit");
        bar.AddRange(screen.Hints);
        return bar;
    }

    private IRenderable RenderLogsOverlay(LogRingBuffer buf, int width, int height)
    {
        var snapshot = buf.Snapshot();
        var lines = new List<IRenderable>(Math.Min(snapshot.Count, height) + 2);
        lines.Add(new Markup($"[{Tokens.Tokens.TextPrimary.Value.ToMarkup()} bold]Logs[/]  [{Tokens.Tokens.TextTertiary.Value.ToMarkup()}]({snapshot.Count}/{buf.Capacity})[/]"));
        lines.Add(new Markup(string.Empty));

        var startIndex = Math.Max(0, snapshot.Count - (height - 4));
        for (var i = startIndex; i < snapshot.Count; i++)
        {
            var entry = snapshot[i];
            var color = entry.Level switch
            {
                Microsoft.Extensions.Logging.LogLevel.Warning => Tokens.Tokens.StatusWarning,
                Microsoft.Extensions.Logging.LogLevel.Error => Tokens.Tokens.StatusError,
                Microsoft.Extensions.Logging.LogLevel.Critical => Tokens.Tokens.StatusError,
                Microsoft.Extensions.Logging.LogLevel.Debug => Tokens.Tokens.TextTertiary,
                Microsoft.Extensions.Logging.LogLevel.Trace => Tokens.Tokens.TextTertiary,
                _ => Tokens.Tokens.TextSecondary,
            };
            lines.Add(new Markup($"[{color.Value.ToMarkup()}]{Markup.Escape(entry.FormatLine())}[/]"));
        }

        return new Padder(new Rows(lines)).Padding(2, 1, 2, 1);
    }

    /// <summary>Console-backed key reader used by the production path.</summary>
    public sealed class ConsoleKeyReader : IKeyReader
    {
        public ConsoleKeyInfo? ReadKey()
        {
            try
            {
                return Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                // Stdin redirected mid-flight — treat as EOF.
                return null;
            }
        }
    }
}

/// <summary>Possible outcomes of a single key dispatch.</summary>
public enum ShellAction
{
    /// <summary>Continue the input loop, redraw on next tick.</summary>
    Continue,

    /// <summary>Clean exit, return code 0.</summary>
    Exit,

    /// <summary>Ctrl+C exit, return code 130.</summary>
    ExitSigInt,
}
