using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Doctor;

/// <summary>
/// Runs environment self-checks (write perms, profile store readable, library cache
/// reachable, Audible API reachable, disk free).
///
/// Per design (§15 risks), there is intentionally <b>no FFmpeg check</b>: decryption
/// and muxing are fully in-process via <c>Oahu.Decrypt</c> (AAXClean-derived).
/// </summary>
public interface IDoctorService
{
    Task<DoctorReport> RunAsync(DoctorOptions? options = null, CancellationToken ct = default);
}

/// <summary>Tunables for <see cref="IDoctorService.RunAsync"/>.</summary>
public sealed class DoctorOptions
{
    /// <summary>Override the directory checked for write permissions (defaults to <see cref="Paths.CliPaths.DefaultDownloadDir"/>).</summary>
    public string? OutputDir { get; init; }

    /// <summary>Skip the network check (offline environments / CI).</summary>
    public bool SkipNetwork { get; init; }

    /// <summary>Minimum free disk space considered acceptable (default 1 GiB).</summary>
    public long MinFreeBytes { get; init; } = 1L * 1024 * 1024 * 1024;
}
