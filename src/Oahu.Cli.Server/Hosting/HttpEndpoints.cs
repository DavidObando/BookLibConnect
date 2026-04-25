using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Oahu.Cli.Server.Auth;
using Oahu.Cli.Server.Capabilities;
using Oahu.Cli.Server.Tools;

namespace Oahu.Cli.Server.Hosting;

/// <summary>Maps the OAhu tool surface as REST + SSE endpoints on a loopback HTTP listener.</summary>
public static class HttpEndpoints
{
    public static void Map(WebApplication app)
    {
        // Bearer-token middleware: every request must present a matching Authorization header.
        app.Use(async (ctx, next) =>
        {
            var token = ctx.RequestServices.GetRequiredService<TokenStore>().ReadOrCreate();
            var auth = ctx.Request.Headers["Authorization"].ToString();
            const string prefix = "Bearer ";
            if (!auth.StartsWith(prefix, StringComparison.Ordinal) ||
                !TokenStore.Equal(auth.Substring(prefix.Length), token))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.WWWAuthenticate = "Bearer realm=\"oahu-cli\"";
                await ctx.Response.WriteAsync("{\"error\":\"unauthorized\"}").ConfigureAwait(false);
                return;
            }
            await next(ctx).ConfigureAwait(false);
        });

        var v1 = app.MapGroup("/v1");

        v1.MapGet("/auth/status", (OahuTools t, ToolDispatcher d, CancellationToken ct) =>
            d.InvokeAsync("auth_status", CapabilityClass.Safe, null, () => t.AuthStatusAsync(ct), principal: "http"));

        v1.MapGet("/library", (OahuTools t, ToolDispatcher d, string? filter, int? limit, CancellationToken ct) =>
            d.InvokeAsync("library_list", CapabilityClass.Safe, Args(("filter", filter), ("limit", limit)), () => t.LibraryListAsync(filter, limit, ct), principal: "http"));

        v1.MapGet("/library/{asin}", (OahuTools t, ToolDispatcher d, string asin, CancellationToken ct) =>
            d.InvokeAsync("library_show", CapabilityClass.Safe, Args(("asin", asin)), () => t.LibraryShowAsync(asin, ct), principal: "http"));

        v1.MapPost("/library/sync", (OahuTools t, ToolDispatcher d, string? profile, CancellationToken ct) =>
            d.InvokeAsync("library_sync", CapabilityClass.Expensive, Args(("profile", profile)), () => t.LibrarySyncAsync(profile, ct), principal: "http"));

        v1.MapGet("/queue", (OahuTools t, ToolDispatcher d, CancellationToken ct) =>
            d.InvokeAsync("queue_list", CapabilityClass.Safe, null, () => t.QueueListAsync(ct), principal: "http"));

        v1.MapPost("/queue", async (OahuTools t, ToolDispatcher d, QueueAddBody body, CancellationToken ct) =>
            await d.InvokeAsync("queue_add", CapabilityClass.Mutating,
                Args(("asins", body.Asins), ("title", body.Title), ("quality", body.Quality), ("profile", body.Profile)),
                () => t.QueueAddAsync(body.Asins ?? Array.Empty<string>(), body.Title, body.Quality, body.Profile, ct),
                principal: "http").ConfigureAwait(false));

        v1.MapDelete("/queue/{asin}", (OahuTools t, ToolDispatcher d, string asin, CancellationToken ct) =>
            d.InvokeAsync("queue_remove", CapabilityClass.Mutating, Args(("asins", new[] { asin })), () => t.QueueRemoveAsync(new[] { asin }, ct), principal: "http"));

        v1.MapDelete("/queue", (OahuTools t, ToolDispatcher d, bool? confirm, CancellationToken ct) =>
            d.InvokeAsync("queue_clear", CapabilityClass.Destructive, Args(("confirm", confirm == true)), () => t.QueueClearAsync(confirm == true, ct), confirmed: confirm == true, principal: "http"));

        v1.MapPost("/jobs", async (OahuTools t, ToolDispatcher d, DownloadBody body, CancellationToken ct) =>
            await d.InvokeAsync("download", CapabilityClass.Expensive,
                Args(("asins", body.Asins), ("quality", body.Quality), ("profile", body.Profile), ("exportToAax", body.ExportToAax), ("outputDir", body.OutputDir)),
                () => t.DownloadAsync(body.Asins ?? Array.Empty<string>(), body.Quality, body.Profile, body.ExportToAax ?? false, body.OutputDir, ct),
                principal: "http").ConfigureAwait(false));

        v1.MapGet("/jobs", (OahuTools t, ToolDispatcher d, CancellationToken ct) =>
            d.InvokeAsync("jobs_status", CapabilityClass.Safe, null, () => t.JobsStatusAsync(null, ct), principal: "http"));

        v1.MapGet("/jobs/{jobId}", (OahuTools t, ToolDispatcher d, string jobId, CancellationToken ct) =>
            d.InvokeAsync("jobs_status", CapabilityClass.Safe, Args(("jobId", jobId)), () => t.JobsStatusAsync(jobId, ct), principal: "http"));

        v1.MapDelete("/jobs/{jobId}", (OahuTools t, ToolDispatcher d, string jobId, CancellationToken ct) =>
            d.InvokeAsync("jobs_cancel", CapabilityClass.Mutating, Args(("jobId", jobId)), () => t.JobsCancelAsync(jobId, ct), principal: "http"));

        // SSE: stream JobUpdates as text/event-stream.
        v1.MapGet("/jobs/stream", async (HttpContext ctx, Oahu.Cli.App.Jobs.IJobService jobs, CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            // Send a snapshot of currently-active jobs first so reconnecting clients
            // do not miss the early Queued/Licensing updates that already fired.
            foreach (var snap in jobs.ListActive())
            {
                await WriteSseAsync(ctx, "snapshot", new
                {
                    jobId = snap.JobId,
                    asin = snap.Asin,
                    title = snap.Title,
                    phase = snap.Phase.ToString(),
                    progress = snap.Progress,
                    message = snap.Message,
                }, ct).ConfigureAwait(false);
            }

            await foreach (var u in jobs.ObserveAll(ct).ConfigureAwait(false))
            {
                await WriteSseAsync(ctx, "update", new
                {
                    jobId = u.JobId,
                    phase = u.Phase.ToString(),
                    progress = u.Progress,
                    message = u.Message,
                    timestamp = u.Timestamp,
                }, ct).ConfigureAwait(false);
            }
        });

        v1.MapGet("/history", (OahuTools t, ToolDispatcher d, int? limit, CancellationToken ct) =>
            d.InvokeAsync("history_list", CapabilityClass.Safe, Args(("limit", limit)), () => t.HistoryListAsync(limit, ct), principal: "http"));

        v1.MapGet("/history/{jobId}", (OahuTools t, ToolDispatcher d, string jobId, CancellationToken ct) =>
            d.InvokeAsync("history_show", CapabilityClass.Safe, Args(("jobId", jobId)), () => t.HistoryShowAsync(jobId, ct), principal: "http"));

        v1.MapGet("/doctor", (OahuTools t, ToolDispatcher d, CancellationToken ct) =>
            d.InvokeAsync("doctor", CapabilityClass.Safe, null, () => t.DoctorAsync(ct), principal: "http"));

        v1.MapGet("/config", (OahuTools t, ToolDispatcher d, string? key, CancellationToken ct) =>
            d.InvokeAsync("config_get", CapabilityClass.Safe, Args(("key", key)), () => t.ConfigGetAsync(key, ct), principal: "http"));

        v1.MapPut("/config/{key}", (OahuTools t, ToolDispatcher d, string key, ConfigSetBody body, CancellationToken ct) =>
            d.InvokeAsync("config_set", CapabilityClass.Mutating, Args(("key", key), ("value", body.Value)), () => t.ConfigSetAsync(key, body.Value ?? string.Empty, ct), principal: "http"));
    }

    private static async Task WriteSseAsync(HttpContext ctx, string evt, object data, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        await ctx.Response.WriteAsync($"event: {evt}\n", ct).ConfigureAwait(false);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var d = new Dictionary<string, object?>(pairs.Length, StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }
        return d;
    }

    public sealed class QueueAddBody
    {
        public string[]? Asins { get; set; }

        public string? Title { get; set; }

        public string? Quality { get; set; }

        public string? Profile { get; set; }
    }

    public sealed class DownloadBody
    {
        public string[]? Asins { get; set; }

        public string? Quality { get; set; }

        public string? Profile { get; set; }

        public bool? ExportToAax { get; set; }

        public string? OutputDir { get; set; }
    }

    public sealed class ConfigSetBody
    {
        public string? Value { get; set; }
    }
}
