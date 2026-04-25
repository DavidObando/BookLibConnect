using System.CommandLine;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli tui</c> — placeholder for explicit TUI entry. Phase 6 wires this to
/// <c>Oahu.Cli.Tui.AppShell.RunAsync</c>; for now it returns a clear "not yet
/// implemented" message and exit code 1, so users / CI can detect the gap.
/// </summary>
public static class TuiCommand
{
    public const int NotImplementedExitCode = 1;

    public static Command Create()
    {
        var cmd = new Command("tui", "Launch the interactive TUI (full-screen). Equivalent to running `oahu-cli` with no arguments.");

        cmd.SetAction(_ => RunPlaceholder());

        return cmd;
    }

    public static int RunPlaceholder()
    {
        if (!CliEnvironment.CanEnterTui)
        {
            CliEnvironment.Error.WriteLine("✗ TUI mode requires an interactive terminal.");
            CliEnvironment.Error.WriteLine();
            CliEnvironment.Error.WriteLine("  Run a subcommand instead, e.g.:");
            CliEnvironment.Error.WriteLine("    oahu-cli doctor --json");
            CliEnvironment.Error.WriteLine("  Or run `oahu-cli --help` for the full command set.");
            return 2;
        }

        CliEnvironment.Error.WriteLine("oahu-cli: TUI mode is not yet implemented (Phase 6).");
        CliEnvironment.Error.WriteLine("Use `oahu-cli --help` to see available commands.");
        return NotImplementedExitCode;
    }
}
