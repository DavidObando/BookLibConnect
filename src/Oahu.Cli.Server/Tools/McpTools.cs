using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Oahu.Cli.Server.Capabilities;
using Oahu.Cli.Server.Hosting;

namespace Oahu.Cli.Server.Tools;

/// <summary>
/// MCP-stdio tool surface. Each method is a thin wrapper around <see cref="OahuTools"/>
/// that runs the call through <see cref="ToolDispatcher"/> for capability gating + audit.
///
/// Capability classes are owned by the corresponding method on <see cref="OahuTools"/>
/// (via <see cref="OahuCapabilityAttribute"/>) — duplication here is mechanical and
/// intentional: the wrapper exists only so the MCP SDK can discover it via attributes.
/// </summary>
[McpServerToolType]
public sealed class McpTools
{
    [McpServerTool(Name = "auth_status"), Description("Show every signed-in Audible profile and the active one.")]
    public Task<object> AuthStatus(OahuTools tools, ToolDispatcher d, CancellationToken ct) =>
        d.InvokeAsync("auth_status", CapabilityClass.Safe, null, () => tools.AuthStatusAsync(ct));

    [McpServerTool(Name = "library_list"), Description("List books in the local library cache. `filter` matches title/author/series substring; `limit` caps results.")]
    public Task<object> LibraryList(OahuTools tools, ToolDispatcher d, string? filter = null, int? limit = null, CancellationToken ct = default) =>
        d.InvokeAsync("library_list", CapabilityClass.Safe, Args(("filter", filter), ("limit", limit)), () => tools.LibraryListAsync(filter, limit, ct));

    [McpServerTool(Name = "library_show"), Description("Show full library detail for a single ASIN.")]
    public Task<object> LibraryShow(OahuTools tools, ToolDispatcher d, string asin, CancellationToken ct = default) =>
        d.InvokeAsync("library_show", CapabilityClass.Safe, Args(("asin", asin)), () => tools.LibraryShowAsync(asin, ct));

    [McpServerTool(Name = "library_sync"), Description("Pull the latest library snapshot from Audible. Long-running; counts as 'expensive'.")]
    public Task<object> LibrarySync(OahuTools tools, ToolDispatcher d, string? profile = null, CancellationToken ct = default) =>
        d.InvokeAsync("library_sync", CapabilityClass.Expensive, Args(("profile", profile)), () => tools.LibrarySyncAsync(profile, ct));

    [McpServerTool(Name = "queue_list"), Description("List pending download queue entries.")]
    public Task<object> QueueList(OahuTools tools, ToolDispatcher d, CancellationToken ct) =>
        d.InvokeAsync("queue_list", CapabilityClass.Safe, null, () => tools.QueueListAsync(ct));

    [McpServerTool(Name = "queue_add"), Description("Add one or more ASINs to the queue. `quality` is High|Normal|Low. `title` is honoured only when adding a single ASIN.")]
    public Task<object> QueueAdd(OahuTools tools, ToolDispatcher d, string[] asins, string? title = null, string? quality = null, string? profile = null, CancellationToken ct = default) =>
        d.InvokeAsync("queue_add", CapabilityClass.Mutating, Args(("asins", asins), ("title", title), ("quality", quality), ("profile", profile)), () => tools.QueueAddAsync(asins, title, quality, profile, ct));

    [McpServerTool(Name = "queue_remove"), Description("Remove one or more ASINs from the queue.")]
    public Task<object> QueueRemove(OahuTools tools, ToolDispatcher d, string[] asins, CancellationToken ct = default) =>
        d.InvokeAsync("queue_remove", CapabilityClass.Mutating, Args(("asins", asins)), () => tools.QueueRemoveAsync(asins, ct));

    [McpServerTool(Name = "queue_clear"), Description("Remove every entry from the queue. Destructive — pass `confirm: true` to authorise.")]
    public Task<object> QueueClear(OahuTools tools, ToolDispatcher d, bool confirm = false, CancellationToken ct = default) =>
        d.InvokeAsync("queue_clear", CapabilityClass.Destructive, Args(("confirm", confirm)), () => tools.QueueClearAsync(confirm, ct), confirmed: confirm);

    [McpServerTool(Name = "download"), Description("Submit a download for one or more ASINs. Returns immediately with the assigned jobIds; poll `jobs_status` to track progress.")]
    public Task<object> Download(OahuTools tools, ToolDispatcher d, string[] asins, string? quality = null, string? profile = null, bool exportToAax = false, string? outputDir = null, CancellationToken ct = default) =>
        d.InvokeAsync("download", CapabilityClass.Expensive, Args(("asins", asins), ("quality", quality), ("profile", profile), ("exportToAax", exportToAax), ("outputDir", outputDir)), () => tools.DownloadAsync(asins, quality, profile, exportToAax, outputDir, ct));

    [McpServerTool(Name = "jobs_status"), Description("Latest-known status of one job (`jobId`) or every active job (omit `jobId`).")]
    public Task<object> JobsStatus(OahuTools tools, ToolDispatcher d, string? jobId = null, CancellationToken ct = default) =>
        d.InvokeAsync("jobs_status", CapabilityClass.Safe, Args(("jobId", jobId)), () => tools.JobsStatusAsync(jobId, ct));

    [McpServerTool(Name = "jobs_cancel"), Description("Cooperatively cancel a running or queued job. Returns `{canceled: true}` if the job was found.")]
    public Task<object> JobsCancel(OahuTools tools, ToolDispatcher d, string jobId, CancellationToken ct = default) =>
        d.InvokeAsync("jobs_cancel", CapabilityClass.Mutating, Args(("jobId", jobId)), () => tools.JobsCancelAsync(jobId, ct));

    [McpServerTool(Name = "history_list"), Description("List terminal job records (success/failure/cancel). `limit` returns the most-recent N.")]
    public Task<object> HistoryList(OahuTools tools, ToolDispatcher d, int? limit = null, CancellationToken ct = default) =>
        d.InvokeAsync("history_list", CapabilityClass.Safe, Args(("limit", limit)), () => tools.HistoryListAsync(limit, ct));

    [McpServerTool(Name = "history_show"), Description("Show one history record by jobId.")]
    public Task<object> HistoryShow(OahuTools tools, ToolDispatcher d, string jobId, CancellationToken ct = default) =>
        d.InvokeAsync("history_show", CapabilityClass.Safe, Args(("jobId", jobId)), () => tools.HistoryShowAsync(jobId, ct));

    [McpServerTool(Name = "doctor"), Description("Run environment self-checks (write perms, profile store readable, library cache reachable, Audible API reachable, disk free).")]
    public Task<object> Doctor(OahuTools tools, ToolDispatcher d, CancellationToken ct) =>
        d.InvokeAsync("doctor", CapabilityClass.Safe, null, () => tools.DoctorAsync(ct));

    [McpServerTool(Name = "config_get"), Description("Get one config key (or every key when `key` is omitted).")]
    public Task<object> ConfigGet(OahuTools tools, ToolDispatcher d, string? key = null, CancellationToken ct = default) =>
        d.InvokeAsync("config_get", CapabilityClass.Safe, Args(("key", key)), () => tools.ConfigGetAsync(key, ct));

    [McpServerTool(Name = "config_set"), Description("Set one config key. Allowed keys: DownloadDirectory, DefaultQuality, MaxParallelJobs, KeepEncryptedFiles.")]
    public Task<object> ConfigSet(OahuTools tools, ToolDispatcher d, string key, string value, CancellationToken ct = default) =>
        d.InvokeAsync("config_set", CapabilityClass.Mutating, Args(("key", key), ("value", value)), () => tools.ConfigSetAsync(key, value, ct));

    private static System.Collections.Generic.IReadOnlyDictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var d = new System.Collections.Generic.Dictionary<string, object?>(pairs.Length, System.StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }
        return d;
    }
}
