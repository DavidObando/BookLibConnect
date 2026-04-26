using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Doctor;
using Oahu.Cli.App.Errors;
using Oahu.Cli.App.Paths;
using Oahu.Cli.Server.Auth;
using Oahu.Cli.Server.Hosting;

namespace Oahu.Cli.Commands;

/// <summary>
/// <c>oahu-cli serve</c> — runs the MCP-stdio + loopback HTTP server (Phase 5).
///
/// <para>Subcommands:</para>
/// <list type="bullet">
/// <item><c>serve</c> (default): start the server with the configured transports.</item>
/// <item><c>serve token</c> &lt;<c>show</c>|<c>rotate</c>|<c>path</c>&gt;: manage the bearer token.</item>
/// </list>
///
/// <para>
/// Token rotation is offline-only: refuse if a server is currently holding the
/// data-dir lock. Documented in <c>docs/OAHU_CLI_SERVER.md</c>.
/// </para>
/// </summary>
public static class ServeCommand
{
    /// <summary>Legacy alias for <see cref="ExitCodes.Locked"/>; kept for source compatibility with tests.</summary>
    public const int LockedExitCode = ExitCodes.Locked;

    public static Command Create(Func<ParseResult, GlobalOptions> resolveGlobals)
    {
        var cmd = new Command("serve", "Run the MCP / loopback HTTP server (Phase 5).");

        var mcpOpt = new Option<bool>("--mcp") { Description = "Enable the JSON-RPC stdio MCP transport (default if neither --mcp nor --http is set)." };
        var httpOpt = new Option<bool>("--http") { Description = "Enable the loopback HTTP REST + SSE transport." };
        var bindOpt = new Option<string?>("--bind") { Description = "HTTP bind address. Must be loopback (127.0.0.1, ::1, localhost). Default: 127.0.0.1." };
        var portOpt = new Option<int?>("--port") { Description = "HTTP TCP port. Default: 8765. Use 0 for ephemeral." };
        var listenOpt = new Option<string?>("--listen")
        {
            Description = "Alternative HTTP transport: 'unix:<path>' to bind to a Unix-domain socket (Linux/macOS only; chmod 0600). Mutually exclusive with --bind/--port.",
        };
        var strictPeerOpt = new Option<bool>("--strict-peer")
        {
            Description = "When set with --listen unix:..., reject HTTP connections whose peer UID does not match the server's UID. No-op for TCP / Windows.",
        };
        var unattendedOpt = new Option<bool>("--unattended") { Description = "Allow Mutating/Expensive tools without interactive confirmation under stdio MCP." };

        cmd.Options.Add(mcpOpt);
        cmd.Options.Add(httpOpt);
        cmd.Options.Add(bindOpt);
        cmd.Options.Add(portOpt);
        cmd.Options.Add(listenOpt);
        cmd.Options.Add(strictPeerOpt);
        cmd.Options.Add(unattendedOpt);

        cmd.SetAction(async (parse, ct) =>
        {
            var enableStdio = parse.GetValue(mcpOpt);
            var enableHttp = parse.GetValue(httpOpt);
            var listenRaw = parse.GetValue(listenOpt);
            string? unixSocketPath = null;
            if (!string.IsNullOrEmpty(listenRaw))
            {
                if (!listenRaw.StartsWith("unix:", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"oahu-cli: --listen '{listenRaw}' is not valid. Only 'unix:<path>' is supported.");
                    return ExitCodes.UsageError;
                }
                unixSocketPath = listenRaw.Substring("unix:".Length);
                if (string.IsNullOrWhiteSpace(unixSocketPath))
                {
                    Console.Error.WriteLine("oahu-cli: --listen unix:<path> requires a non-empty path.");
                    return ExitCodes.UsageError;
                }
                if (parse.GetValue(bindOpt) is not null || parse.GetValue(portOpt) is not null)
                {
                    Console.Error.WriteLine("oahu-cli: --listen is mutually exclusive with --bind/--port.");
                    return ExitCodes.UsageError;
                }
                // Implicitly enable HTTP when --listen is given.
                enableHttp = true;
            }

            if (!enableStdio && !enableHttp)
            {
                enableStdio = true;
            }
            var options = new ServerOptions
            {
                EnableStdio = enableStdio,
                EnableHttp = enableHttp,
                HttpHost = parse.GetValue(bindOpt) ?? "127.0.0.1",
                HttpPort = parse.GetValue(portOpt) ?? 8765,
                UnixSocketPath = unixSocketPath,
                StrictPeer = parse.GetValue(strictPeerOpt),
                Unattended = parse.GetValue(unattendedOpt),
            };
            var factories = new ServerHost.ServiceFactories
            {
                Auth = CliServiceFactory.AuthServiceFactory,
                Library = CliServiceFactory.LibraryServiceFactory,
                Queue = CliServiceFactory.QueueServiceFactory,
                Job = CliServiceFactory.JobServiceFactory,
                Config = () => new JsonConfigService(CliPaths.ConfigFile),
                Doctor = () => new DoctorService(),
            };
            return await ServerHost.RunAsync(options, factories, ct).ConfigureAwait(false);
        });

        cmd.Subcommands.Add(CreateTokenCommand());
        return cmd;
    }

    private static Command CreateTokenCommand()
    {
        var token = new Command("token", "Manage the loopback HTTP bearer token.");

        var showCmd = new Command("show", "Print the current bearer token to stdout.");
        showCmd.SetAction(_ =>
        {
            var ts = new TokenStore();
            Console.Out.WriteLine(ts.ReadOrCreate());
            return ExitCodes.Success;
        });

        var pathCmd = new Command("path", "Print the path to the bearer-token file.");
        pathCmd.SetAction(_ =>
        {
            Console.Out.WriteLine(new TokenStore().Path);
            return ExitCodes.Success;
        });

        var rotateCmd = new Command("rotate", "Mint a new bearer token, replacing the existing one. Refuses while a server is running.");
        rotateCmd.SetAction(_ =>
        {
            using var dataLock = new UserDataLock();
            try
            {
                dataLock.Acquire();
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Stop the running server before rotating its token.");
                return ExitCodes.Locked;
            }
            var fresh = new TokenStore().Rotate();
            Console.Out.WriteLine(fresh);
            return ExitCodes.Success;
        });

        token.Subcommands.Add(showCmd);
        token.Subcommands.Add(pathCmd);
        token.Subcommands.Add(rotateCmd);
        return token;
    }
}
