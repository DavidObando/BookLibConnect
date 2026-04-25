using System;
using Oahu.Cli.Tui.Shell;
using Spectre.Console;

namespace Oahu.Cli.Tui;

/// <summary>
/// Public Phase 6 entry point. Owns the alt-screen lifecycle and the
/// <see cref="Shell.AppShell"/> instance. The CLI host calls
/// <see cref="Run"/> from the <c>oahu-cli</c> default action and the
/// <c>oahu-cli tui</c> subcommand.
/// </summary>
public static class TuiHost
{
    /// <summary>Exit code returned when the host refuses to enter (non-TTY).</summary>
    public const int NoTtyExitCode = 2;

    /// <summary>
    /// Run the AppShell against the real <see cref="Console"/>. This switches
    /// the terminal into the alt-screen buffer, sets <c>TreatControlCAsInput</c>
    /// so the progressive Ctrl+C state machine sees presses as keys, and
    /// guarantees a terminal restore even on unhandled exceptions.
    /// </summary>
    /// <param name="options">Shell options. May be null.</param>
    /// <param name="registerRestore">
    /// Optional hook to register the alt-screen leave action with an external
    /// exit-trap (the CLI uses <c>CliEnvironment.RegisterRestore</c>).
    /// </param>
    public static int Run(AppShellOptions? options = null, Action<Action>? registerRestore = null)
    {
        var console = AnsiConsole.Console;
        var shell = new AppShell(console, options);
        var prevTreatCtrlC = false;
        var altEntered = false;
        Action restore = () =>
        {
            if (altEntered)
            {
                AltScreen.Leave();
                altEntered = false;
            }
            try
            {
                Console.TreatControlCAsInput = prevTreatCtrlC;
            }
            catch
            {
                // some hosts don't support this setting; ignore.
            }
        };
        registerRestore?.Invoke(restore);

        try
        {
            try
            {
                prevTreatCtrlC = Console.TreatControlCAsInput;
                Console.TreatControlCAsInput = true;
            }
            catch
            {
                // ignore — we'll fall back to the cooperative SIGINT path.
            }

            AltScreen.Enter();
            altEntered = true;

            return shell.Run(new AppShell.ConsoleKeyReader());
        }
        finally
        {
            restore();
        }
    }
}
