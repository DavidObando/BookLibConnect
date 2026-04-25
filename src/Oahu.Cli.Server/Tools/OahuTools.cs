using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Doctor;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Queue;
using Oahu.Cli.Server.Capabilities;

namespace Oahu.Cli.Server.Tools;

/// <summary>
/// All tool implementations the <c>oahu-cli serve</c> surface exposes, mapped 1:1
/// against the table in <c>docs/OAHU_CLI_DESIGN.md</c> §15.1.
///
/// <para>
/// These methods <b>do not</b> contain auth or audit logic — that's owned by
/// <see cref="Hosting.ToolDispatcher"/>. Capability classes are owned by the
/// <see cref="OahuCapabilityAttribute"/> on each method (single source of truth
/// for both stdio MCP tool registration and the REST routes).
/// </para>
///
/// <para>
/// All methods return plain CLR types serialized via <c>System.Text.Json</c>; both
/// transports get the same JSON shape.
/// </para>
/// </summary>
public sealed class OahuTools
{
    private readonly IAuthService authService;
    private readonly ILibraryService libraryService;
    private readonly IQueueService queueService;
    private readonly IJobService jobService;
    private readonly IConfigService configService;
    private readonly IDoctorService doctorService;

    public OahuTools(
        IAuthService authService,
        ILibraryService libraryService,
        IQueueService queueService,
        IJobService jobService,
        IConfigService configService,
        IDoctorService doctorService)
    {
        this.authService = authService;
        this.libraryService = libraryService;
        this.queueService = queueService;
        this.jobService = jobService;
        this.configService = configService;
        this.doctorService = doctorService;
    }

    // ---- AUTH ------------------------------------------------------------
    [OahuCapability(CapabilityClass.Safe)]
    public async Task<object> AuthStatusAsync(CancellationToken ct = default)
    {
        var sessions = await authService.ListSessionsAsync(ct).ConfigureAwait(false);
        var active = await authService.GetActiveAsync(ct).ConfigureAwait(false);
        return new
        {
            active = active is null ? null : ToAuth(active),
            sessions = sessions.Select(ToAuth).ToArray(),
        };
    }

    // ---- LIBRARY ---------------------------------------------------------
    [OahuCapability(CapabilityClass.Safe)]
    public async Task<object> LibraryListAsync(string? filter = null, int? limit = null, CancellationToken ct = default)
    {
        var f = string.IsNullOrWhiteSpace(filter) ? null : new LibraryFilter { Search = filter };
        var items = await libraryService.ListAsync(f, ct).ConfigureAwait(false);
        IEnumerable<LibraryItem> seq = items;
        if (limit is { } n && n >= 0)
        {
            seq = seq.Take(n);
        }
        return new { items = seq.Select(ToLib).ToArray(), total = items.Count };
    }

    [OahuCapability(CapabilityClass.Safe)]
    public async Task<object> LibraryShowAsync(string asin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(asin))
        {
            throw new ArgumentException("asin is required.", nameof(asin));
        }
        var item = await libraryService.GetAsync(asin, ct).ConfigureAwait(false);
        if (item is null)
        {
            throw new KeyNotFoundException($"No library item with ASIN '{asin}'.");
        }
        return ToLib(item);
    }

    [OahuCapability(CapabilityClass.Expensive)]
    public async Task<object> LibrarySyncAsync(string? profile = null, CancellationToken ct = default)
    {
        var alias = profile ?? string.Empty;
        var added = await libraryService.SyncAsync(alias, ct).ConfigureAwait(false);
        return new { newItems = added };
    }

    // ---- QUEUE -----------------------------------------------------------
    [OahuCapability(CapabilityClass.Safe)]
    public async Task<object> QueueListAsync(CancellationToken ct = default)
    {
        var items = await queueService.ListAsync(ct).ConfigureAwait(false);
        return new
        {
            items = items.Select(ToQueue).ToArray(),
            total = items.Count,
        };
    }

    [OahuCapability(CapabilityClass.Mutating)]
    public async Task<object> QueueAddAsync(string[] asins, string? title = null, string? quality = null, string? profile = null, CancellationToken ct = default)
    {
        if (asins is null || asins.Length == 0)
        {
            throw new ArgumentException("at least one asin is required.", nameof(asins));
        }
        var q = ParseQuality(quality);
        var added = new List<string>();
        var skipped = new List<string>();
        foreach (var raw in asins)
        {
            var asin = raw?.Trim();
            if (string.IsNullOrEmpty(asin))
            {
                continue;
            }
            var entry = new QueueEntry
            {
                Asin = asin,
                Title = (asins.Length == 1 ? title : null) ?? asin,
                Quality = q,
                ProfileAlias = profile,
            };
            if (await queueService.AddAsync(entry, ct).ConfigureAwait(false))
            {
                added.Add(asin);
            }
            else
            {
                skipped.Add(asin);
            }
        }
        return new { added = added.ToArray(), skipped = skipped.ToArray() };
    }

    [OahuCapability(CapabilityClass.Mutating)]
    public async Task<object> QueueRemoveAsync(string[] asins, CancellationToken ct = default)
    {
        if (asins is null || asins.Length == 0)
        {
            throw new ArgumentException("at least one asin is required.", nameof(asins));
        }
        var removed = new List<string>();
        var missed = new List<string>();
        foreach (var asin in asins)
        {
            if (await queueService.RemoveAsync(asin, ct).ConfigureAwait(false))
            {
                removed.Add(asin);
            }
            else
            {
                missed.Add(asin);
            }
        }
        return new { removed = removed.ToArray(), missing = missed.ToArray() };
    }

    [OahuCapability(CapabilityClass.Destructive)]
    public async Task<object> QueueClearAsync(bool confirm, CancellationToken ct = default)
    {
        var items = await queueService.ListAsync(ct).ConfigureAwait(false);
        var n = items.Count;
        await queueService.ClearAsync(ct).ConfigureAwait(false);
        return new { removed = n };
    }

    // ---- JOBS / DOWNLOAD -------------------------------------------------
    [OahuCapability(CapabilityClass.Expensive)]
    public async Task<object> DownloadAsync(string[] asins, string? quality = null, string? profile = null, bool exportToAax = false, string? outputDir = null, CancellationToken ct = default)
    {
        if (asins is null || asins.Length == 0)
        {
            throw new ArgumentException("at least one asin is required.", nameof(asins));
        }
        var q = ParseQuality(quality);
        var accepted = new List<object>();
        foreach (var raw in asins)
        {
            var asin = raw?.Trim();
            if (string.IsNullOrEmpty(asin))
            {
                continue;
            }
            // Best-effort title lookup so the job record / observers see something useful.
            string title = asin;
            try
            {
                var item = await libraryService.GetAsync(asin, ct).ConfigureAwait(false);
                if (item is not null)
                {
                    title = item.Title;
                }
            }
            catch
            {
                // ignore — fall back to ASIN.
            }
            var req = new JobRequest
            {
                Asin = asin,
                Title = title,
                Quality = q,
                ProfileAlias = profile,
                ExportToAax = exportToAax,
                OutputDir = outputDir,
            };
            await jobService.SubmitAsync(req, ct).ConfigureAwait(false);
            accepted.Add(new { jobId = req.Id, asin, title });
        }
        return new { accepted = accepted.ToArray() };
    }

    [OahuCapability(CapabilityClass.Safe)]
    public Task<object> JobsStatusAsync(string? jobId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            var snap = jobService.GetSnapshot(jobId);
            return Task.FromResult<object>(new { job = snap is null ? null : ToSnapshot(snap) });
        }
        var snaps = jobService.ListActive();
        return Task.FromResult<object>(new
        {
            jobs = snaps.Select(ToSnapshot).ToArray(),
            total = snaps.Count,
        });
    }

    [OahuCapability(CapabilityClass.Mutating)]
    public Task<object> JobsCancelAsync(string jobId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("jobId is required.", nameof(jobId));
        }
        var ok = jobService.Cancel(jobId);
        return Task.FromResult<object>(new { canceled = ok });
    }

    // ---- HISTORY ---------------------------------------------------------
    [OahuCapability(CapabilityClass.Safe)]
    public async Task<object> HistoryListAsync(int? limit = null, CancellationToken ct = default)
    {
        var rows = new List<JobRecord>();
        await foreach (var r in jobService.ReadHistoryAsync(ct).ConfigureAwait(false))
        {
            rows.Add(r);
        }
        IEnumerable<JobRecord> seq = rows;
        if (limit is { } n && n >= 0)
        {
            // history.jsonl is append-order; "limit" returns the most-recent N.
            seq = seq.Skip(Math.Max(0, rows.Count - n));
        }
        return new { items = seq.Select(ToHistory).ToArray(), total = rows.Count };
    }

    [OahuCapability(CapabilityClass.Safe)]
    public async Task<object> HistoryShowAsync(string jobId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("jobId is required.", nameof(jobId));
        }
        await foreach (var r in jobService.ReadHistoryAsync(ct).ConfigureAwait(false))
        {
            if (string.Equals(r.Id, jobId, StringComparison.Ordinal))
            {
                return ToHistory(r);
            }
        }
        throw new KeyNotFoundException($"No history record with jobId '{jobId}'.");
    }

    // history_delete deferred to post-1.0 (would need rewrite-then-rename of jsonl).

    // ---- DOCTOR ----------------------------------------------------------
    [OahuCapability(CapabilityClass.Safe)]
    public async Task<object> DoctorAsync(CancellationToken ct = default)
    {
        var report = await doctorService.RunAsync(null, ct).ConfigureAwait(false);
        return new
        {
            hasErrors = report.HasErrors,
            hasWarnings = report.HasWarnings,
            checks = report.Checks.Select(c => new
            {
                id = c.Id,
                title = c.Title,
                severity = c.Severity.ToString(),
                message = c.Message,
                hint = c.Hint,
            }).ToArray(),
        };
    }

    // ---- CONFIG ----------------------------------------------------------
    [OahuCapability(CapabilityClass.Safe)]
    public async Task<object> ConfigGetAsync(string? key = null, CancellationToken ct = default)
    {
        var cfg = await configService.LoadAsync(ct).ConfigureAwait(false);
        var dict = ConfigToDict(cfg);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new { config = dict, path = configService.Path };
        }
        if (!dict.TryGetValue(key, out var v))
        {
            throw new KeyNotFoundException($"Unknown config key '{key}'.");
        }
        return new { key, value = v };
    }

    [OahuCapability(CapabilityClass.Mutating)]
    public async Task<object> ConfigSetAsync(string key, string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("key is required.", nameof(key));
        }
        var cfg = await configService.LoadAsync(ct).ConfigureAwait(false);
        var updated = ApplyConfig(cfg, key, value);
        await configService.SaveAsync(updated, ct).ConfigureAwait(false);
        return new { key, value = ConfigToDict(updated)[key] };
    }

    // ---- helpers ---------------------------------------------------------
    private static object ToAuth(AuthSession s) => new
    {
        profileAlias = s.ProfileAlias,
        region = s.Region.ToString(),
        accountId = s.AccountId,
        accountName = s.AccountName,
        deviceName = s.DeviceName,
        expiresAt = s.ExpiresAt,
        isExpired = s.IsExpired,
    };

    private static object ToLib(LibraryItem i) => new
    {
        asin = i.Asin,
        title = i.Title,
        subtitle = i.Subtitle,
        authors = i.Authors,
        narrators = i.Narrators,
        series = i.Series,
        seriesPosition = i.SeriesPosition,
        runtimeMinutes = i.Runtime?.TotalMinutes,
        purchaseDate = i.PurchaseDate,
        isAvailable = i.IsAvailable,
    };

    private static object ToQueue(QueueEntry e) => new
    {
        asin = e.Asin,
        title = e.Title,
        quality = e.Quality.ToString(),
        addedAt = e.AddedAt,
        profileAlias = e.ProfileAlias,
    };

    private static object ToSnapshot(JobSnapshot s) => new
    {
        jobId = s.JobId,
        asin = s.Asin,
        title = s.Title,
        phase = s.Phase.ToString(),
        progress = s.Progress,
        message = s.Message,
        startedAt = s.StartedAt,
        updatedAt = s.UpdatedAt,
        quality = s.Quality?.ToString(),
        profileAlias = s.ProfileAlias,
    };

    private static object ToHistory(JobRecord r) => new
    {
        id = r.Id,
        asin = r.Asin,
        title = r.Title,
        terminalPhase = r.TerminalPhase.ToString(),
        startedAt = r.StartedAt,
        completedAt = r.CompletedAt,
        errorMessage = r.ErrorMessage,
        profileAlias = r.ProfileAlias,
        quality = r.Quality?.ToString(),
    };

    private static DownloadQuality ParseQuality(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DownloadQuality.High;
        }
        if (Enum.TryParse<DownloadQuality>(raw, ignoreCase: true, out var q))
        {
            return q;
        }
        throw new ArgumentException($"Invalid quality '{raw}'. Expected: High, Normal, Low.");
    }

    private static IDictionary<string, object?> ConfigToDict(OahuConfig cfg) => new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["DownloadDirectory"] = cfg.DownloadDirectory,
        ["DefaultQuality"] = cfg.DefaultQuality.ToString(),
        ["MaxParallelJobs"] = cfg.MaxParallelJobs,
        ["KeepEncryptedFiles"] = cfg.KeepEncryptedFiles,
    };

    private static OahuConfig ApplyConfig(OahuConfig cfg, string key, string value) => key switch
    {
        "DownloadDirectory" => cfg with { DownloadDirectory = value },
        "DefaultQuality" => cfg with { DefaultQuality = ParseQuality(value) },
        "MaxParallelJobs" => cfg with { MaxParallelJobs = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
        "KeepEncryptedFiles" => cfg with { KeepEncryptedFiles = bool.Parse(value) },
        _ => throw new KeyNotFoundException($"Unknown config key '{key}'."),
    };
}
