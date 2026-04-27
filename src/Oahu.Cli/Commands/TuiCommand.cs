using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Errors;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui;
using Oahu.Cli.Tui.Auth;
using Oahu.Cli.Tui.Logging;
using Oahu.Cli.Tui.Screens;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Themes;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli tui</c> — explicit TUI entry. Equivalent to running
/// <c>oahu-cli</c> with no arguments.
/// </summary>
public static class TuiCommand
{
    /// <summary>Legacy alias for <see cref="ExitCodes.GenericFailure"/>; kept for source compatibility with tests.</summary>
    public const int NotImplementedExitCode = ExitCodes.GenericFailure;

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

    /// <summary>
    /// Overload that lets the subcommand share the root's <c>--theme</c> /
    /// <c>--no-color</c> resolution.
    /// </summary>
    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var cmd = new Command("tui", "Launch the interactive TUI (full-screen). Equivalent to running `oahu-cli` with no arguments.");
        cmd.SetAction(parse => Run(resolveGlobals(parse)));
        return cmd;
    }

    public static int Run() => Run(new GlobalOptions());

    public static int Run(GlobalOptions globals)
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

        // Apply the theme BEFORE building any screens so the first frame uses
        // the right palette. Precedence: --theme flag > NO_COLOR/--no-color → Mono
        // > persisted OahuConfig.Theme > Default. Unknown names silently fall
        // back to Default so a stale config can never wedge startup.
        ApplyStartupTheme(globals);

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

        // Sign-in flow is owned by HomeScreen: pressing 's' opens the region
        // picker modal via the navigator, then SignInFlow drives the broker
        // and library sync. No additional wiring needed here.
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

    /// <summary>
    /// Resolve the effective theme name and apply it via <see cref="Theme.Use(string)"/>.
    /// Public for testing — callers normally rely on <see cref="Run(GlobalOptions)"/>.
    /// </summary>
    public static string ResolveStartupThemeName(GlobalOptions globals, string? configuredTheme)
    {
        // 1. --theme flag wins.
        if (TryMatchTheme(globals.ThemeOverride, out var explicitName))
        {
            return explicitName;
        }

        // 2. Colour-disabled environments → Mono.
        if (globals.ForceNoColor || CliEnvironment.NoColorRequested)
        {
            return Themes.Mono.Name;
        }

        // 3. Persisted config value (silently ignored if unknown).
        if (TryMatchTheme(configuredTheme, out var configured))
        {
            return configured;
        }

        return Themes.Default.Name;
    }

    private static bool TryMatchTheme(string? candidate, out string resolved)
    {
        if (!string.IsNullOrEmpty(candidate))
        {
            foreach (var name in Theme.AvailableNames())
            {
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    resolved = name;
                    return true;
                }
            }
        }
        resolved = string.Empty;
        return false;
    }

    private static void ApplyStartupTheme(GlobalOptions globals)
    {
        string? configured = null;
        try
        {
            var cfg = CliServiceFactory.ConfigServiceFactory().LoadAsync().GetAwaiter().GetResult();
            configured = cfg.Theme;
        }
        catch
        {
            // Fresh installs / unreadable config → fall back to defaults.
        }

        var name = ResolveStartupThemeName(globals, configured);
        try
        {
            Theme.Use(name);
        }
        catch
        {
            Theme.Reset();
        }
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
