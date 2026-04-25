using System;

namespace Oahu.Cli.Output;

/// <summary>
/// Carries the resolved output format plus enough state for handlers to write
/// pretty/plain/json without each command repeating the same logic.
/// </summary>
public sealed class OutputContext
{
    public OutputContext(OutputFormat format, bool quiet, bool useColor, bool useAscii)
    {
        Format = format;
        Quiet = quiet;
        UseColor = useColor;
        UseAscii = useAscii;
    }

    public OutputFormat Format { get; }

    public bool Quiet { get; }

    public bool UseColor { get; }

    public bool UseAscii { get; }

    /// <summary>
    /// Resolves the effective output format from explicit flags + TTY detection.
    /// Precedence (per design §9 / §4.2):
    ///   • --json wins if both flags are set (and we surface a parse error elsewhere).
    ///   • --plain forces Plain.
    ///   • Otherwise: Pretty if stdout is a TTY, else Plain.
    /// </summary>
    public static OutputFormat ResolveFormat(bool jsonFlag, bool plainFlag, bool stdoutIsRedirected)
    {
        if (jsonFlag)
        {
            return OutputFormat.Json;
        }
        if (plainFlag)
        {
            return OutputFormat.Plain;
        }
        return stdoutIsRedirected ? OutputFormat.Plain : OutputFormat.Pretty;
    }
}
