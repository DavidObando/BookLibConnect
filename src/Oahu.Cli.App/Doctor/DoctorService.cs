using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oahu.Cli.App.Paths;

namespace Oahu.Cli.App.Doctor;

/// <summary>
/// Default <see cref="IDoctorService"/> implementation.
///
/// Each check returns a <see cref="DoctorCheck"/>; failures degrade to a single
/// <see cref="DoctorSeverity"/> entry rather than throwing, so <c>oahu-cli doctor</c>
/// can still print a complete report even when one probe fails.
/// </summary>
public sealed class DoctorService : IDoctorService
{
    private static readonly Uri AudibleProbeUri = new("https://api.audible.com/", UriKind.Absolute);

    private readonly ILogger<DoctorService> logger;
    private readonly Func<HttpClient> httpClientFactory;

    public DoctorService(ILogger<DoctorService>? logger = null, Func<HttpClient>? httpClientFactory = null)
    {
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DoctorService>.Instance;
        this.httpClientFactory = httpClientFactory ?? DefaultHttpClient;
    }

    public async Task<DoctorReport> RunAsync(DoctorOptions? options = null, CancellationToken ct = default)
    {
        options ??= new DoctorOptions();
        var checks = new List<DoctorCheck>
        {
            CheckOutputDirectoryWritable(options.OutputDir ?? CliPaths.DefaultDownloadDir),
            CheckSharedUserDataDirReadable(),
            CheckLibraryCacheReachable(),
            CheckDiskFree(options.OutputDir ?? CliPaths.DefaultDownloadDir, options.MinFreeBytes),
            CheckCliConfigWritable(),
        };

        if (options.SkipNetwork)
        {
            checks.Add(new DoctorCheck(
                "audible-api",
                "Audible API reachable",
                DoctorSeverity.Ok,
                "skipped (--skip-network)"));
        }
        else
        {
            checks.Add(await CheckAudibleApiReachableAsync(ct).ConfigureAwait(false));
        }

        return new DoctorReport(checks);
    }

    internal static DoctorCheck CheckOutputDirectoryWritable(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".oahu-cli-doctor-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return new DoctorCheck("output-dir", "Output directory writable", DoctorSeverity.Ok, path);
        }
        catch (Exception ex)
        {
            return new DoctorCheck(
                "output-dir",
                "Output directory writable",
                DoctorSeverity.Error,
                $"{path}: {ex.Message}",
                "Pick a writable directory with `oahu-cli config set output-dir <path>` or pass `--output-dir`.");
        }
    }

    internal static DoctorCheck CheckSharedUserDataDirReadable()
    {
        var dir = CliPaths.SharedUserDataDir;
        try
        {
            // Existence is informational only — a fresh install will not have created it yet.
            // The check passes as long as we can stat the parent.
            var parent = Directory.GetParent(dir)?.FullName ?? dir;
            if (!Directory.Exists(parent))
            {
                return new DoctorCheck(
                    "user-data-dir",
                    "Shared user-data directory accessible",
                    DoctorSeverity.Warning,
                    $"parent does not exist: {parent}",
                    "The Oahu GUI creates this on first run; install or run the GUI once, or `oahu-cli auth login` will create it.");
            }

            return new DoctorCheck(
                "user-data-dir",
                "Shared user-data directory accessible",
                DoctorSeverity.Ok,
                Directory.Exists(dir) ? dir : $"will be created at: {dir}");
        }
        catch (Exception ex)
        {
            return new DoctorCheck(
                "user-data-dir",
                "Shared user-data directory accessible",
                DoctorSeverity.Error,
                ex.Message);
        }
    }

    internal static DoctorCheck CheckLibraryCacheReachable()
    {
        // The library cache lives under <SharedUserDataDir>/data/audiobooks.db (see Oahu.Data.BookDbContext).
        // We don't open EF here (that would require a profile); we just check the file's directory is reachable.
        var dataDir = Path.Combine(CliPaths.SharedUserDataDir, "data");
        try
        {
            if (!Directory.Exists(dataDir))
            {
                return new DoctorCheck(
                    "library-cache",
                    "Library cache directory reachable",
                    DoctorSeverity.Warning,
                    $"not yet created: {dataDir}",
                    "Created on first sign-in / library sync.");
            }

            var dbFile = Path.Combine(dataDir, "audiobooks.db");
            var status = File.Exists(dbFile) ? $"present: {dbFile}" : $"will be created at: {dbFile}";
            return new DoctorCheck("library-cache", "Library cache directory reachable", DoctorSeverity.Ok, status);
        }
        catch (Exception ex)
        {
            return new DoctorCheck("library-cache", "Library cache directory reachable", DoctorSeverity.Error, ex.Message);
        }
    }

    internal static DoctorCheck CheckCliConfigWritable()
    {
        try
        {
            CliPaths.EnsureDirectories();
            var probe = Path.Combine(CliPaths.ConfigDir, $".oahu-cli-doctor-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return new DoctorCheck("cli-config", "CLI config directory writable", DoctorSeverity.Ok, CliPaths.ConfigDir);
        }
        catch (Exception ex)
        {
            return new DoctorCheck("cli-config", "CLI config directory writable", DoctorSeverity.Error, $"{CliPaths.ConfigDir}: {ex.Message}");
        }
    }

    internal static DoctorCheck CheckDiskFree(string path, long minFreeBytes)
    {
        try
        {
            // DriveInfo expects an existing path; walk up until we find one.
            var probe = path;
            while (!string.IsNullOrEmpty(probe) && !Directory.Exists(probe))
            {
                probe = Path.GetDirectoryName(probe);
            }
            if (string.IsNullOrEmpty(probe))
            {
                probe = Path.GetPathRoot(Path.GetFullPath(path)) ?? "/";
            }

            var drive = new DriveInfo(probe);
            var free = drive.AvailableFreeSpace;
            var human = $"{free / (1024.0 * 1024 * 1024):0.00} GiB free on {drive.Name}";

            if (free < minFreeBytes)
            {
                return new DoctorCheck(
                    "disk-free",
                    "Sufficient free disk space",
                    DoctorSeverity.Warning,
                    human,
                    $"Audiobooks are large; aim for at least {minFreeBytes / (1024 * 1024 * 1024)} GiB free on the output volume.");
            }

            return new DoctorCheck("disk-free", "Sufficient free disk space", DoctorSeverity.Ok, human);
        }
        catch (Exception ex)
        {
            return new DoctorCheck("disk-free", "Sufficient free disk space", DoctorSeverity.Warning, ex.Message);
        }
    }

    internal async Task<DoctorCheck> CheckAudibleApiReachableAsync(CancellationToken ct)
    {
        try
        {
            using var http = httpClientFactory();
            using var req = new HttpRequestMessage(HttpMethod.Head, AudibleProbeUri);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            // Audible may answer with 4xx for an unauthenticated bare-root request — that's still "reachable".
            return new DoctorCheck(
                "audible-api",
                "Audible API reachable",
                DoctorSeverity.Ok,
                $"HTTP {(int)resp.StatusCode} from {AudibleProbeUri.Host}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DoctorCheck(
                "audible-api",
                "Audible API reachable",
                DoctorSeverity.Warning,
                "timed out after 5 s",
                "Check your network connection or proxy settings.");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Audible API probe failed");
            return new DoctorCheck(
                "audible-api",
                "Audible API reachable",
                DoctorSeverity.Warning,
                ex.Message,
                "Check your network connection or proxy settings.");
        }
    }

    private static HttpClient DefaultHttpClient()
    {
        var c = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("oahu-cli/1.0 (+https://github.com/DavidObando/Oahu)");
        return c;
    }
}
