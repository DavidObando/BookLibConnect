namespace Oahu.Cli.Commands;

/// <summary>
/// Globally-applicable flags resolved from the root command and made available
/// to every subcommand.
/// </summary>
public sealed class GlobalOptions
{
    public bool Quiet { get; init; }

    public bool Verbose { get; init; }

    public bool Force { get; init; }

    public bool DryRun { get; init; }

    public bool ForceNoColor { get; init; }

    public bool UseAscii { get; init; }

    public string? ConfigDirOverride { get; init; }

    public string? LogDirOverride { get; init; }

    public string? LogLevelOverride { get; init; }

    public bool Json { get; init; }

    public bool Plain { get; init; }

    /// <summary>
    /// Optional theme name from the <c>--theme</c> root flag. When set it overrides
    /// the persisted <c>OahuConfig.Theme</c> for the lifetime of the process.
    /// </summary>
    public string? ThemeOverride { get; init; }
}
