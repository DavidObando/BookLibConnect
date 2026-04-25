namespace Oahu.Cli.Output;

/// <summary>
/// Selects how command-mode handlers serialise their results.
/// Per design §9: Pretty (TTY), Plain (non-TTY or --plain), Json (--json).
/// </summary>
public enum OutputFormat
{
    Pretty,
    Plain,
    Json,
}
