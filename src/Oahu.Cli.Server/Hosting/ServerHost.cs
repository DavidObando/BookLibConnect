using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Doctor;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Queue;
using Oahu.Cli.Server.Audit;
using Oahu.Cli.Server.Auth;
using Oahu.Cli.Server.Capabilities;
using Oahu.Cli.Server.Tools;

namespace Oahu.Cli.Server.Hosting;

/// <summary>
/// Entry-point for <c>oahu-cli serve</c>. Hosts the MCP-stdio server, the loopback
/// HTTP REST server, or both, depending on <see cref="ServerOptions"/>.
///
/// <para>
/// Service factories default to whatever <c>Oahu.Cli.Commands.CliServiceFactory</c>
/// returns; tests inject fakes via the same seam (see <see cref="ServiceFactories"/>).
/// </para>
///
/// <para>
/// CRITICAL: under stdio MCP, stdout is owned by the JSON-RPC channel. All ASP.NET
/// Core / hosting / our own diagnostics must go to <c>stderr</c> or be silenced.
/// We force this when <see cref="ServerOptions.EnableStdio"/> is true.
/// </para>
/// </summary>
public static class ServerHost
{
    /// <summary>
    /// Override-able service factories. Production code lets these resolve via the
    /// <c>CliServiceFactory</c> static accessors (configured by <c>oahu-cli</c>'s
    /// <c>Program.Main</c>); tests can swap them out wholesale.
    /// </summary>
    public sealed class ServiceFactories
    {
        public Func<IAuthService> Auth { get; init; } = () => throw new InvalidOperationException("AuthFactory not configured");

        public Func<ILibraryService> Library { get; init; } = () => throw new InvalidOperationException("LibraryFactory not configured");

        public Func<IQueueService> Queue { get; init; } = () => throw new InvalidOperationException("QueueFactory not configured");

        public Func<IJobService> Job { get; init; } = () => throw new InvalidOperationException("JobFactory not configured");

        public Func<IConfigService> Config { get; init; } = () => throw new InvalidOperationException("ConfigFactory not configured");

        public Func<IDoctorService> Doctor { get; init; } = () => throw new InvalidOperationException("DoctorFactory not configured");
    }

    /// <summary>
    /// Build a <see cref="WebApplication"/> ready to be started. Public so tests can
    /// construct it without going through <see cref="RunAsync"/>.
    /// </summary>
    public static WebApplication BuildHttpApp(ServerOptions options, ServiceFactories factories)
    {
        var bindAddr = options.ResolveBindAddress(); // throws on non-loopback.
        var builder = WebApplication.CreateSlimBuilder();

        // Quiet ASP.NET Core (avoid corrupting stdout when sharing with stdio MCP).
        if (options.EnableStdio)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new StderrLoggerProvider());
        }
        else
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole();
        }

        RegisterShared(builder.Services, options, factories, ServerTransport.Http);

        var app = builder.Build();
        app.Urls.Clear();
        app.Urls.Add($"http://{(bindAddr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[::1]" : bindAddr.ToString())}:{options.HttpPort}");
        HttpEndpoints.Map(app);
        return app;
    }

    /// <summary>
    /// Build a configured stdio MCP host. Public so tests can introspect tool registration
    /// without spinning up I/O.
    /// </summary>
    public static IHost BuildStdioHost(ServerOptions options, ServiceFactories factories)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new StderrLoggerProvider());

        RegisterShared(builder.Services, options, factories, ServerTransport.Stdio);
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(new[] { typeof(McpTools) });

        return builder.Build();
    }

    /// <summary>Run the configured server. Returns when the process is asked to stop.</summary>
    public static async Task<int> RunAsync(ServerOptions options, ServiceFactories factories, CancellationToken cancellationToken = default)
    {
        if (!options.EnableStdio && !options.EnableHttp)
        {
            await Console.Error.WriteLineAsync("oahu-cli serve: must enable at least one of --mcp or --http.").ConfigureAwait(false);
            return 2;
        }

        // Acquire the cooperative file lock first — fail fast on contention.
        using var dataLock = new UserDataLock(options.LockPath);
        try
        {
            dataLock.Acquire();
        }
        catch (InvalidOperationException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 4;
        }

        // Ensure the token file exists if HTTP is enabled (so we fail fast on permission issues too).
        if (options.EnableHttp)
        {
            try
            {
                _ = new TokenStore(options.TokenPath).ReadOrCreate();
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"oahu-cli serve: token init failed: {ex.Message}").ConfigureAwait(false);
                return 4;
            }
        }

        Task? stdioTask = null;
        WebApplication? httpApp = null;

        try
        {
            if (options.EnableStdio)
            {
                var stdioHost = BuildStdioHost(options, factories);
                stdioTask = stdioHost.RunAsync(cancellationToken);
            }

            if (options.EnableHttp)
            {
                httpApp = BuildHttpApp(options, factories);
                await httpApp.StartAsync(cancellationToken).ConfigureAwait(false);
                var addresses = string.Join(", ", httpApp.Urls);
                await Console.Error.WriteLineAsync($"oahu-cli serve: HTTP listening on {addresses}").ConfigureAwait(false);
            }

            if (stdioTask is not null)
            {
                await stdioTask.ConfigureAwait(false);
            }
            else if (httpApp is not null)
            {
                await httpApp.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            if (httpApp is not null)
            {
                await httpApp.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Shared DI registration for both stdio MCP and HTTP REST.</summary>
    public static void RegisterShared(IServiceCollection services, ServerOptions options, ServiceFactories factories, ServerTransport transport)
    {
        services.AddSingleton(_ => factories.Auth());
        services.AddSingleton(_ => factories.Library());
        services.AddSingleton(_ => factories.Queue());
        services.AddSingleton(_ => factories.Job());
        services.AddSingleton(_ => factories.Config());
        services.AddSingleton(_ => factories.Doctor());

        services.AddSingleton<OahuTools>();
        services.AddSingleton(new TokenStore(options.TokenPath));
        services.AddSingleton(new AuditLog(options.AuditPath));
        services.AddSingleton(new CapabilityPolicy(transport, options.Unattended));
        services.AddSingleton<ToolDispatcher>();
    }

    private sealed class StderrLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName);

        public void Dispose()
        {
        }

        private sealed class StderrLogger : ILogger
        {
            private readonly string category;

            public StderrLogger(string category)
            {
                this.category = category;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }
                var msg = formatter(state, exception);
                Console.Error.WriteLine($"[{logLevel}] {category}: {msg}");
                if (exception is not null)
                {
                    Console.Error.WriteLine(exception);
                }
            }
        }
    }
}
