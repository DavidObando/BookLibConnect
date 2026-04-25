using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Credentials;

/// <summary>
/// macOS Keychain-backed store driven by <c>/usr/bin/security</c>. We shell out to
/// avoid a P/Invoke dependency; the surface used (<c>security add-generic-password
/// / find-generic-password / delete-generic-password</c>) is stable and present on
/// every supported macOS version.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacOsKeychainCredentialStore : ICredentialStore
{
    private const string DefaultService = "oahu-cli";
    private readonly string serviceName;
    private readonly string securityBinary;

    public MacOsKeychainCredentialStore(string? serviceName = null, string? securityBinary = null)
    {
        this.serviceName = serviceName ?? DefaultService;
        this.securityBinary = securityBinary ?? "/usr/bin/security";
    }

    public string Provider => "keychain";

    public async Task<string?> GetAsync(string account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        var (code, stdout, _) = await RunAsync(
            new[] { "find-generic-password", "-s", serviceName, "-a", account, "-w" },
            cancellationToken).ConfigureAwait(false);
        if (code == 44)
        {
            return null;
        }
        if (code != 0)
        {
            throw new CredentialStoreOperationException("find-generic-password", code);
        }
        return stdout.TrimEnd('\n', '\r');
    }

    public async Task SetAsync(string account, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentNullException.ThrowIfNull(secret);
        var (code, _, _) = await RunAsync(
            new[] { "add-generic-password", "-U", "-s", serviceName, "-a", account, "-w", secret },
            cancellationToken).ConfigureAwait(false);
        if (code != 0)
        {
            throw new CredentialStoreOperationException("add-generic-password", code);
        }
    }

    public async Task<bool> DeleteAsync(string account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        var (code, _, _) = await RunAsync(
            new[] { "delete-generic-password", "-s", serviceName, "-a", account },
            cancellationToken).ConfigureAwait(false);
        if (code == 44)
        {
            return false;
        }
        if (code != 0)
        {
            throw new CredentialStoreOperationException("delete-generic-password", code);
        }
        return true;
    }

    public Task<IReadOnlyList<string>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        // /usr/bin/security has no clean "list accounts under a service" verb; defer to Phase 4.
        // Returning an empty list is fine for now since all callers only use Get/Set/Delete keyed by alias.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private async Task<(int Code, string StdOut, string StdErr)> RunAsync(string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(securityBinary)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var proc = Process.Start(psi)
            ?? throw new CredentialStoreUnavailableException($"Could not start {securityBinary}.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }
}

public sealed class CredentialStoreOperationException : Exception
{
    public CredentialStoreOperationException(string operation, int exitCode)
        : base($"Credential store operation '{operation}' failed with exit code {exitCode}.")
    {
        Operation = operation;
        ExitCode = exitCode;
    }

    public string Operation { get; }

    public int ExitCode { get; }
}
