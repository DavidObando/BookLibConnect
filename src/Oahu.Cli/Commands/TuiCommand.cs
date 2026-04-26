using System;
using System.CommandLine;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui;
using Oahu.Cli.Tui.Auth;
using Oahu.Cli.Tui.Logging;
using Oahu.Cli.Tui.Screens;
using Oahu.Cli.Tui.Shell;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli tui</c> — explicit TUI entry. Equivalent to running
/// <c>oahu-cli</c> with no arguments.
/// </summary>
public static class TuiCommand
{
    public const int NotImplementedExitCode = 1;

    /// <summary>
    /// Hook for tests / phase 7+ to swap the production launcher with a fake
    /// (e.g. one that captures the <see cref="AppShellOptions"/> instead of
    /// touching the real terminal).
    /// </summary>
    public static Func<AppShellOptions, int> Launcher { get; set; } =
        opts => TuiHost.Run(opts, CliEnvironment.RegisterRestore);

    /// <summary>
    /// In-process accessor to the Logs ring buffer. <see cref="Program"/> sets
    /// this when it builds the LoggerFactory; the TUI launcher reads it so the
    /// L-toggle overlay shows entries.
    /// </summary>
    public static LogRingBuffer? LogBuffer { get; set; }

    public static Command Create()
    {
        var cmd = new Command("tui", "Launch the interactive TUI (full-screen). Equivalent to running `oahu-cli` with no arguments.");
        cmd.SetAction(_ => Run());
        return cmd;
    }

    public static int Run()
    {
        if (!CliEnvironment.CanEnterTui)
        {
            CliEnvironment.Error.WriteLine("✗ TUI mode requires an interactive terminal.");
            CliEnvironment.Error.WriteLine();
            CliEnvironment.Error.WriteLine("  Run a subcommand instead, e.g.:");
            CliEnvironment.Error.WriteLine("    oahu-cli doctor --json");
            CliEnvironment.Error.WriteLine("  Or run `oahu-cli --help` for the full command set.");
            return TuiHost.NoTtyExitCode;
        }

        var state = new AppShellState();

        // Populate initial profile from existing sessions.
        try
        {
            var auth = CliServiceFactory.AuthServiceFactory();
            var session = auth.GetActiveAsync().GetAwaiter().GetResult();
            if (session is not null)
            {
                state.Profile = session.ProfileAlias;
                state.Region = session.Region.ToString().ToLowerInvariant();
            }
        }
        catch
        {
            // Swallow — fresh install has no profiles.
        }

        var tabs = DefaultTabs.CreateReal(
            state,
            CliServiceFactory.AuthServiceFactory,
            CliServiceFactory.LibraryServiceFactory,
            () => CliServiceFactory.ConfigServiceFactory(),
            CliServiceFactory.QueueServiceFactory,
            CliServiceFactory.JobServiceFactory);

        // Wire sign-in flow: Home "s" key → region picker → external login → sync
        if (tabs[0] is HomeScreen home)
        {
            home.OnSignInRequested = () => { /* handled in TuiHost via shell modal flow */ };
        }

        var opts = new AppShellOptions
        {
            UseAscii = string.Equals(Environment.GetEnvironmentVariable("OAHU_ASCII_ICONS"), "1", StringComparison.Ordinal),
            LogBuffer = LogBuffer,
            Version = ResolveVersion(),
            Tabs = tabs,
            State = state,
        };

        return Launcher(opts);
    }

    private static string ResolveVersion()
    {
        try
        {
            var asm = typeof(TuiCommand).Assembly;
            var info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false);
            if (info.Length > 0)
            {
                var v = ((System.Reflection.AssemblyInformationalVersionAttribute)info[0]).InformationalVersion;
                // Strip "+commit" suffix that GitVersioning appends.
                var plus = v.IndexOf('+');
                return plus >= 0 ? v[..plus] : v;
            }
            return asm.GetName().Version?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
