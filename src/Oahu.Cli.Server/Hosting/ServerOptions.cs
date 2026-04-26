using System.Net;

namespace Oahu.Cli.Server.Hosting;

/// <summary>Resolved configuration for a single <c>oahu-cli serve</c> invocation.</summary>
public sealed class ServerOptions
{
    /// <summary>Enable the JSON-RPC stdio MCP transport.</summary>
    public bool EnableStdio { get; init; }

    /// <summary>Enable the loopback HTTP REST + SSE transport.</summary>
    public bool EnableHttp { get; init; }

    /// <summary>Bind address for HTTP. Must be loopback (127.0.0.1 / ::1). Default: <c>127.0.0.1</c>.</summary>
    public string HttpHost { get; init; } = "127.0.0.1";

    /// <summary>HTTP port. <c>0</c> = ephemeral.</summary>
    public int HttpPort { get; init; }

    /// <summary>Allow Mutating/Expensive tools without an interactive prompt under stdio.</summary>
    public bool Unattended { get; init; }

    /// <summary>Optional override for the bearer-token file path (defaults to <c>&lt;ConfigDir&gt;/server.token</c>).</summary>
    public string? TokenPath { get; init; }

    /// <summary>Optional override for the lock file path (test hook).</summary>
    public string? LockPath { get; init; }

    /// <summary>Optional override for the audit log path (test hook).</summary>
    public string? AuditPath { get; init; }

    /// <summary>
    /// When set, the HTTP transport binds to a Unix-domain socket at this path
    /// instead of <see cref="HttpHost"/> / <see cref="HttpPort"/>. Mutually
    /// exclusive with the TCP options. Not supported on Windows.
    /// </summary>
    public string? UnixSocketPath { get; init; }

    /// <summary>
    /// When true and <see cref="UnixSocketPath"/> is set, the server verifies that
    /// each incoming HTTP connection's peer UID matches the server's UID and
    /// rejects mismatched connections with HTTP 403. Linux + macOS only.
    /// </summary>
    public bool StrictPeer { get; init; }

    /// <summary>Validates loopback constraint and returns the parsed <see cref="IPAddress"/>.</summary>
    public IPAddress ResolveBindAddress()
    {
        if (HttpHost is "localhost" or "127.0.0.1")
        {
            return IPAddress.Loopback;
        }
        if (HttpHost is "::1")
        {
            return IPAddress.IPv6Loopback;
        }
        if (IPAddress.TryParse(HttpHost, out var ip) && IPAddress.IsLoopback(ip))
        {
            return ip;
        }
        throw new System.InvalidOperationException(
            $"oahu-cli serve refuses to bind to non-loopback address '{HttpHost}'. " +
            "Only 127.0.0.1, ::1, and localhost are accepted in v1.");
    }
}
