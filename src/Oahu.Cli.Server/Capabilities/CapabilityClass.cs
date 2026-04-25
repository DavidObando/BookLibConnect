namespace Oahu.Cli.Server.Capabilities;

/// <summary>
/// Per-tool capability class — drives whether a tool can run unattended,
/// whether HTTP must require an unrestricted bearer token, and whether
/// destructive confirmations are required.
///
/// See <c>docs/OAHU_CLI_DESIGN.md</c> §15.2.
/// </summary>
public enum CapabilityClass
{
    /// <summary>Read-only, side-effect-free. Always allowed.</summary>
    Safe,

    /// <summary>Mutates server state but is reversible. Auto-allowed under HTTP-with-token; under stdio MCP requires <c>--unattended</c>.</summary>
    Mutating,

    /// <summary>Long-running and resource-heavy (e.g. <c>library_sync</c>, <c>download</c>). Same auth as Mutating; future versions may add per-class rate limits.</summary>
    Expensive,

    /// <summary>Irreversible (delete history, log out). Always requires <c>confirm: true</c> in the tool args.</summary>
    Destructive,
}

/// <summary>Marker attribute on tool methods so capability is owned by code, not by description text.</summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
public sealed class OahuCapabilityAttribute : System.Attribute
{
    public OahuCapabilityAttribute(CapabilityClass capability)
    {
        Capability = capability;
    }

    public CapabilityClass Capability { get; }
}
