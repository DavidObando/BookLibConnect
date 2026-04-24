using System;
using System.IO;
using System.Text;

namespace Oahu.Cli;

/// <summary>
/// Process-level setup for the CLI: encoding, color policy, TTY detection,
/// and the exit-trap that restores the terminal on crash / Ctrl+C / process exit.
///
/// Mirrors the design-doc <c>setupProcess()</c> contract (§5.1 + Phase 1).
/// </summary>
internal static class CliEnvironment
{
    private static int restoreInstalled;
    private static Action? restoreAction;

    /// <summary>True when the user explicitly disabled colour via <c>NO_COLOR</c>.</summary>
    public static bool NoColorRequested { get; private set; }

    /// <summary>True when the user explicitly forced colour via <c>FORCE_COLOR</c>.</summary>
    public static bool ForceColorRequested { get; private set; }

    /// <summary>True when stdout is attached to an interactive terminal.</summary>
    public static bool IsStdoutTty { get; private set; }

    /// <summary>True when stderr is attached to an interactive terminal.</summary>
    public static bool IsStderrTty { get; private set; }

    /// <summary>True when stdin is attached to an interactive terminal.</summary>
    public static bool IsStdinTty { get; private set; }

    /// <summary>True when the resolved colour policy is "no colour".</summary>
    public static bool ColorDisabled => NoColorRequested && !ForceColorRequested;

    /// <summary>True when TUI mode is allowed in this process.</summary>
    public static bool CanEnterTui
    {
        get
        {
            if (!IsStdoutTty || !IsStdinTty)
            {
                return false;
            }

            var term = Environment.GetEnvironmentVariable("TERM");
            if (string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(Environment.GetEnvironmentVariable("OAHU_NO_TUI"), "1", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Configure the process. Call once, before any console I/O.
    /// </summary>
    public static void Initialise()
    {
        // 1. Console encoding — UTF-8 in & out so unicode glyphs (status icons, box drawing) render.
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Some hosts (redirected handles, certain CI runners) refuse — that's fine, fall through.
        }

        // 2. Colour policy. NO_COLOR present (any value) wins unless FORCE_COLOR is also set.
        // https://no-color.org/  — "any non-empty value" but in practice presence is enough.
        NoColorRequested = Environment.GetEnvironmentVariable("NO_COLOR") is not null;
        ForceColorRequested = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FORCE_COLOR"));

        // 3. TTY detection — used by output writers to auto-degrade and by the TUI gate.
        IsStdoutTty = !Console.IsOutputRedirected;
        IsStderrTty = !Console.IsErrorRedirected;
        IsStdinTty = !Console.IsInputRedirected;

        // 4. Exit-trap: ensure RestoreOnExit fires for Ctrl+C, ProcessExit, AND unhandled exceptions.
        InstallExitTrap();
    }

    /// <summary>
    /// Register a callback that *must* run on shutdown to restore the terminal
    /// (alt-screen exit, cursor re-enable, raw-mode reset, etc).
    ///
    /// Idempotent — last writer wins. Phase 1 ships only the framework; Phase 6 fills it in.
    /// </summary>
    public static void RegisterRestore(Action restore) => restoreAction = restore;

    /// <summary>
    /// Run the restore callback (if any). Safe to call multiple times.
    /// </summary>
    public static void RunRestore()
    {
        var local = restoreAction;
        restoreAction = null;
        try
        {
            local?.Invoke();
        }
        catch
        {
            // Last-resort handler — never throw out of the exit path.
        }
    }

    private static void InstallExitTrap()
    {
        if (System.Threading.Interlocked.CompareExchange(ref restoreInstalled, 1, 0) != 0)
        {
            return;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            // Phase 1: cooperative — let System.CommandLine's cancellation see Ctrl+C.
            // Phase 6 (TUI) replaces this with the progressive Ctrl+C state machine.
            RunRestore();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => RunRestore();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            RunRestore();
            try
            {
                var ex = e.ExceptionObject as Exception;
                Console.Error.WriteLine();
                Console.Error.WriteLine($"oahu-cli: unhandled exception: {ex?.GetType().FullName}: {ex?.Message}");
                Console.Error.WriteLine("(See `oahu-cli doctor` and the daily log under the logs directory for details.)");
            }
            catch
            {
                // ignore secondary failures.
            }
        };
    }

    /// <summary>Stream pair used by the CLI; tests can swap these to capture output.</summary>
    public static TextWriter Out { get; set; } = Console.Out;

    /// <summary>Stream pair used by the CLI; tests can swap these to capture output.</summary>
    public static TextWriter Error { get; set; } = Console.Error;
}
