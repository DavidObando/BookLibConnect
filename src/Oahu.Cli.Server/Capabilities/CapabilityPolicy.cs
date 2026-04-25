using System;

namespace Oahu.Cli.Server.Capabilities;

/// <summary>
/// Run-time gate: given a transport (stdio MCP vs. HTTP) and tool capability,
/// either allow the call or throw a uniform <see cref="UnauthorizedAccessException"/>
/// the tool host translates to a structured MCP/HTTP error.
///
/// v1 policy (per design §15.2 — simplified, scopes deferred):
/// <list type="bullet">
/// <item><b>Safe</b>: always allowed.</item>
/// <item><b>Mutating</b>/<b>Expensive</b>: allowed under HTTP (already gated by bearer token);
///   under stdio MCP requires <c>--unattended</c> OR an interactive prompt confirmation.
///   v1 ships <i>no</i> stdio prompt — auto-deny when <c>--unattended</c> is not set.</item>
/// <item><b>Destructive</b>: always requires the caller to pass <c>confirm: true</c>;
///   otherwise auto-deny. Once confirmed, falls through to the Mutating rule.</item>
/// </list>
/// </summary>
public sealed class CapabilityPolicy
{
    public CapabilityPolicy(ServerTransport transport, bool unattended)
    {
        Transport = transport;
        Unattended = unattended;
    }

    public ServerTransport Transport { get; }

    public bool Unattended { get; }

    public void Require(string toolName, CapabilityClass capability, bool confirmed = false)
    {
        if (capability == CapabilityClass.Safe)
        {
            return;
        }

        if (capability == CapabilityClass.Destructive && !confirmed)
        {
            throw new UnauthorizedAccessException(
                $"{toolName} is destructive; pass `confirm: true` in the tool arguments to authorise.");
        }

        if (Transport == ServerTransport.Http)
        {
            // HTTP requests are already authenticated by the bearer-token middleware.
            return;
        }

        // Stdio MCP path: require --unattended for everything non-safe.
        if (!Unattended)
        {
            throw new UnauthorizedAccessException(
                $"{toolName} requires unattended mode; restart the server with --unattended " +
                "(or call this tool over the loopback HTTP transport with a valid token).");
        }
    }
}

/// <summary>Which surface a tool invocation arrived through.</summary>
public enum ServerTransport
{
    /// <summary>JSON-RPC over stdio (Claude Desktop / Cursor / Copilot CLI).</summary>
    Stdio,

    /// <summary>Loopback HTTP REST + SSE.</summary>
    Http,
}
