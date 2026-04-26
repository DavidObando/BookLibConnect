using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Oahu.Cli.Server.Audit;
using Oahu.Cli.Server.Capabilities;

namespace Oahu.Cli.Server.Hosting;

/// <summary>
/// Wraps tool invocations with the capability gate + audit logging. Used by both
/// the MCP tool host and the REST endpoints so the policy is enforced exactly once
/// regardless of transport.
/// </summary>
public sealed class ToolDispatcher
{
    private readonly CapabilityPolicy policy;
    private readonly AuditLog audit;

    public ToolDispatcher(CapabilityPolicy policy, AuditLog audit)
    {
        this.policy = policy;
        this.audit = audit;
    }

    public CapabilityPolicy Policy => policy;

    public async Task<T> InvokeAsync<T>(
        string toolName,
        CapabilityClass capability,
        IReadOnlyDictionary<string, object?>? args,
        Func<Task<T>> body,
        bool confirmed = false,
        string principal = "stdio")
    {
        var transport = policy.Transport == ServerTransport.Http ? "http" : "stdio";
        var sw = Stopwatch.StartNew();
        try
        {
            policy.Require(toolName, capability, confirmed);
        }
        catch (UnauthorizedAccessException)
        {
            SafeAudit(transport, principal, toolName, args, "denied", sw.ElapsedMilliseconds);
            throw;
        }

        try
        {
            var result = await body().ConfigureAwait(false);
            SafeAudit(transport, principal, toolName, args, "ok", sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception)
        {
            SafeAudit(transport, principal, toolName, args, "error", sw.ElapsedMilliseconds);
            throw;
        }
    }

    private void SafeAudit(string transport, string principal, string toolName, IReadOnlyDictionary<string, object?>? args, string outcome, long latencyMs)
    {
        try
        {
            audit.Write(transport, principal, toolName, args, outcome, latencyMs);
        }
        catch
        {
            // The original tool result/exception must surface — never let an audit
            // failure mask the actual outcome the caller cares about.
        }
    }
}
