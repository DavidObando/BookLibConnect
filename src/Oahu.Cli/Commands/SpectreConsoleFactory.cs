using Spectre.Console;

namespace Oahu.Cli.Commands;

/// <summary>
/// Single source of truth for constructing a Spectre <see cref="IAnsiConsole"/>
/// that obeys both the user's <c>--no-color</c> / <c>NO_COLOR</c> preference and
/// the auto-degrade-on-non-TTY rule from §9 of the design doc.
/// </summary>
public static class SpectreConsoleFactory
{
    public static IAnsiConsole Create(GlobalOptions globals)
    {
        var disableAnsi = globals.ForceNoColor
            || CliEnvironment.ColorDisabled
            || !CliEnvironment.IsStdoutTty;

        var settings = new AnsiConsoleSettings
        {
            Ansi = disableAnsi ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = disableAnsi ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
            Interactive = CliEnvironment.IsStdoutTty ? InteractionSupport.Yes : InteractionSupport.No,
            Out = new AnsiConsoleOutput(CliEnvironment.Out),
        };

        return AnsiConsole.Create(settings);
    }
}
