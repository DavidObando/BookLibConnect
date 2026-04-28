using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Oahu.Cli;

/// <summary>
/// Process-level setup for the CLI: encoding, color policy, TTY detection,
/// and the exit-trap that restores the terminal on crash / Ctrl+C / process exit.
///
/// Mirrors the design-doc <c>setupProcess()</c> contract (§5.1 + Phase 1).
/// </summary>
public static class CliEnvironment
{
    private static int restoreInstalled;
    private static Action? restoreAction;
    private static int sigintCount;
    private static System.Threading.Timer? graceTimer;

    private static void ForceExit130()
    {
        try
        {
            graceTimer?.Dispose();
        }
        catch
        {
            // best-effort
        }
        try
        {
            RunRestore();
        }
        catch
        {
            // best-effort
        }
        Environment.Exit(130);
    }

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

        // 2. Windows VT — enable ANSI escape sequence processing on the stdout/stderr
        //    console handles so raw sequences (alt-screen, cursor movement, SGR colours)
        //    render correctly on conhost and older Windows Terminal builds.
        EnableWindowsVirtualTerminal();

        // 3. Colour policy. NO_COLOR present (any value) wins unless FORCE_COLOR is also set.
        // https://no-color.org/  — "any non-empty value" but in practice presence is enough.
        NoColorRequested = Environment.GetEnvironmentVariable("NO_COLOR") is not null;
        ForceColorRequested = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FORCE_COLOR"));

        // 4. TTY detection — used by output writers to auto-degrade and by the TUI gate.
        IsStdoutTty = !Console.IsOutputRedirected;
        IsStderrTty = !Console.IsErrorRedirected;
        IsStdinTty = !Console.IsInputRedirected;

        // 5. Exit-trap: ensure RestoreOnExit fires for Ctrl+C, ProcessExit, AND unhandled exceptions.
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
        var local = System.Threading.Interlocked.Exchange(ref restoreAction, null);
        try
        {
            local?.Invoke();
        }
        catch
        {
            // Last-resort handler — never throw out of the exit path.
        }
    }

    private static void EnableWindowsVirtualTerminal()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            const int STD_OUTPUT_HANDLE = -11;
            const int STD_ERROR_HANDLE = -12;
            const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
            const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
            const uint VT_FLAGS = ENABLE_PROCESSED_OUTPUT | ENABLE_VIRTUAL_TERMINAL_PROCESSING;

            EnableVtForHandle(STD_OUTPUT_HANDLE, VT_FLAGS);
            EnableVtForHandle(STD_ERROR_HANDLE, VT_FLAGS);
        }
        catch
        {
            // Best effort — very old Windows builds or redirected handles may fail.
        }

        static void EnableVtForHandle(int handleId, uint vtFlag)
        {
            var handle = GetStdHandle(handleId);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return;
            }

            if (GetConsoleMode(handle, out var mode))
            {
                SetConsoleMode(handle, mode | vtFlag);
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private static void InstallExitTrap()
    {
        if (System.Threading.Interlocked.CompareExchange(ref restoreInstalled, 1, 0) != 0)
        {
            return;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            // Progressive Ctrl+C state machine (design §10):
            //   1st press → cooperative cancel + 5s grace timer.
            //   2nd press OR timer expiry → force-exit with code 130.
            // System.CommandLine has already set e.Cancel = true on its own
            // handler before we run, so the runtime won't terminate the
            // process for the first press; we only need to force-exit on the
            // second press / timer.
            var n = System.Threading.Interlocked.Increment(ref sigintCount);
            if (n == 1)
            {
                try
                {
                    Console.Error.WriteLine("oahu-cli: cancelling… press Ctrl+C again to force-quit (5s grace).");
                }
                catch
                {
                    // stderr might be closed; nothing useful we can do.
                }

                graceTimer = new System.Threading.Timer(
                    _ => ForceExit130(),
                    null,
                    TimeSpan.FromSeconds(5),
                    System.Threading.Timeout.InfiniteTimeSpan);
                RunRestore();
            }
            else
            {
                ForceExit130();
            }
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
