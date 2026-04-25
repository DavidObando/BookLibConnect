using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Doctor;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Queue;
using Oahu.Cli.Server.Auth;
using Oahu.Cli.Server.Hosting;
using Xunit;

namespace Oahu.Cli.Tests.Server;

/// <summary>
/// Boots the real ASP.NET Core HTTP server on an ephemeral loopback port, hits it
/// with HttpClient, and asserts wire-level shape + bearer-token enforcement.
/// </summary>
public sealed class HttpServerIntegrationTests
{
    private static (ServerOptions options, ServerHost.ServiceFactories factories, string token, string tokenPath) Build()
    {
        var tokenPath = Path.Combine(Path.GetTempPath(), $"oahu-http-token-{Guid.NewGuid():n}");
        var auditPath = Path.Combine(Path.GetTempPath(), $"oahu-http-audit-{Guid.NewGuid():n}.jsonl");
        var token = new TokenStore(tokenPath).ReadOrCreate();

        var lib = new FakeLibraryService(new[]
        {
            new LibraryItem { Asin = "B1", Title = "Foundation" },
            new LibraryItem { Asin = "B2", Title = "Dune" },
        });
        var queue = new InMemoryQueueService();
        var auth = new FakeAuthService();
        var jobs = new JobScheduler(new FakeJobExecutor());
        var configPath = Path.Combine(Path.GetTempPath(), $"oahu-http-cfg-{Guid.NewGuid():n}.json");
        var cfg = new JsonConfigService(configPath);
        var doctor = new DoctorService();

        var options = new ServerOptions
        {
            EnableHttp = true,
            HttpHost = "127.0.0.1",
            HttpPort = 0,
            TokenPath = tokenPath,
            AuditPath = auditPath,
        };
        var factories = new ServerHost.ServiceFactories
        {
            Auth = () => auth,
            Library = () => lib,
            Queue = () => queue,
            Job = () => jobs,
            Config = () => cfg,
            Doctor = () => doctor,
        };
        return (options, factories, token, tokenPath);
    }

    private static string ResolveBaseUrl(WebApplication app)
    {
        // Kestrel exposes the actual ephemeral port via IServerAddressesFeature
        // once StartAsync has bound the socket. Fall back to app.Urls otherwise.
        var server = app.Services.GetRequiredService<IServer>();
        var addrs = server.Features.Get<IServerAddressesFeature>();
        var url = addrs?.Addresses.FirstOrDefault() ?? app.Urls.FirstOrDefault();
        Assert.False(string.IsNullOrEmpty(url), "Server did not report a bound URL.");
        return url!;
    }

    [Fact]
    public async Task Missing_Token_Returns_401()
    {
        var (options, factories, _, tokenPath) = Build();
        var app = ServerHost.BuildHttpApp(options, factories);
        await app.StartAsync();
        try
        {
            using var c = new HttpClient { BaseAddress = new Uri(ResolveBaseUrl(app)) };
            var resp = await c.GetAsync("/v1/library");
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task With_Token_Library_Endpoint_Returns_Items()
    {
        var (options, factories, token, tokenPath) = Build();
        var app = ServerHost.BuildHttpApp(options, factories);
        await app.StartAsync();
        try
        {
            using var c = new HttpClient { BaseAddress = new Uri(ResolveBaseUrl(app)) };
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await c.GetAsync("/v1/library");
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            Assert.Contains("Foundation", json);
            Assert.Contains("Dune", json);
            Assert.Contains("\"total\":2", json);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public async Task Queue_Add_Then_List_Then_Clear_Requires_Confirm()
    {
        var (options, factories, token, tokenPath) = Build();
        var app = ServerHost.BuildHttpApp(options, factories);
        await app.StartAsync();
        try
        {
            using var c = new HttpClient { BaseAddress = new Uri(ResolveBaseUrl(app)) };
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var addBody = new StringContent("{\"asins\":[\"B1\",\"B2\"]}", System.Text.Encoding.UTF8, "application/json");
            var addResp = await c.PostAsync("/v1/queue", addBody);
            addResp.EnsureSuccessStatusCode();

            var listJson = await c.GetStringAsync("/v1/queue");
            Assert.Contains("\"total\":2", listJson);

            // Clear without confirm: should fail (Destructive denied).
            var clearNoConfirm = await c.DeleteAsync("/v1/queue");
            Assert.False(clearNoConfirm.IsSuccessStatusCode);

            // Clear with confirm.
            var clearOk = await c.DeleteAsync("/v1/queue?confirm=true");
            clearOk.EnsureSuccessStatusCode();

            var afterJson = await c.GetStringAsync("/v1/queue");
            Assert.Contains("\"total\":0", afterJson);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            File.Delete(tokenPath);
        }
    }

    [Fact]
    public void Non_Loopback_Bind_Refused()
    {
        var (options, factories, _, tokenPath) = Build();
        try
        {
            var bad = new ServerOptions
            {
                EnableHttp = true,
                HttpHost = "0.0.0.0",
                HttpPort = 0,
                TokenPath = options.TokenPath,
                AuditPath = options.AuditPath,
            };
            Assert.Throws<InvalidOperationException>(() => ServerHost.BuildHttpApp(bad, factories));
        }
        finally
        {
            File.Delete(tokenPath);
        }
    }
}
