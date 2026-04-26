using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oahu.Cli.App.Paths;

namespace Oahu.Cli.App.Models;

/// <summary>
/// User-tunable settings for the CLI. Loaded from / saved to <c>config.json</c> via
/// <c>IConfigService</c>. New fields must default to sensible values so the file
/// stays forward-compatible without a migration.
/// </summary>
public sealed record OahuConfig
{
    public string DownloadDirectory { get; init; } = CliPaths.DefaultDownloadDir;

    public DownloadQuality DefaultQuality { get; init; } = DownloadQuality.High;

    public int MaxParallelJobs { get; init; } = 1;

    public bool KeepEncryptedFiles { get; init; }

    public bool MultiPartDownload { get; init; }

    public bool ExportToAax { get; init; }

    public string ExportDirectory { get; init; } = "";

    public string? DefaultProfileAlias { get; init; }

    /// <summary>
    /// Name of the active TUI theme (case-insensitive). Null/empty means "use the built-in
    /// default". Validated against <c>Oahu.Cli.Tui.Themes.Theme.AvailableNames()</c> when set
    /// via <c>oahu-cli config set theme</c>; the TUI silently falls back to the default if
    /// the persisted value is unknown so a stale config never wedges startup.
    /// </summary>
    public string? Theme { get; init; }

    /// <summary>
    /// When true, the credentials store falls back to a passphrase-protected file when no native keyring is available.
    /// Off by default — the design's stance is "fail closed" rather than silently store secrets in a file.
    /// </summary>
    public bool AllowEncryptedFileCredentials { get; init; }

    /// <summary>
    /// Captures any JSON properties that were present on disk but are not declared above.
    /// Preserved across round-trips so a newer CLI can write fields that an older CLI will not erase.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; init; }

    /// <summary>Default config used the first time the CLI runs (no file on disk yet).</summary>
    public static OahuConfig Default => new();
}
