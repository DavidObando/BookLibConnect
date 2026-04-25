namespace Oahu.Cli.Commands;

/// <summary>
/// Globally-applicable flags resolved from the root command and made available
/// to every subcommand.
/// </summary>
internal sealed class GlobalOptions
{
    public bool Quiet { get; init; }

    public bool Verbose { get; init; }

    public bool ForceNoColor { get; init; }

    public bool UseAscii { get; init; }

    public string? ConfigDirOverride { get; init; }

    public string? LogDirOverride { get; init; }

    public string? LogLevelOverride { get; init; }

    public bool Json { get; init; }

    public bool Plain { get; init; }
}
