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
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly string serviceName;
    private readonly string securityBinary;

    public MacOsKeychainCredentialStore(string? serviceName = null, string? securityBinary = null)
    {
        this.serviceName = serviceName ?? DefaultService;
        this.securityBinary = securityBinary ?? ResolveSecurityBinary();
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

        // NOTE: `security add-generic-password -w <secret>` exposes the secret via argv (visible to
        // `ps` and crash dumps for any process owned by the current user). The macOS `security` tool
        // does not offer a stdin-fed equivalent for generic-password creation, so we accept this
        // tradeoff for the v1 shell-out approach. A future change can move to the native Keychain
        // Services API via P/Invoke to keep the secret entirely in this process's address space.
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

        // Bound the wait so a hung Keychain agent (e.g., headless / locked / GUI prompt
        // that nobody answers) cannot deadlock the CLI forever. The user-visible token
        // is "operation timed out", surfaced through the existing exception path.
        using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        using var proc = Process.Start(psi)
            ?? throw new CredentialStoreUnavailableException($"Could not start {securityBinary}.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(token);
        var stderrTask = proc.StandardError.ReadToEndAsync(token);
        try
        {
            await proc.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // best-effort
            }
            throw;
        }

        // Drain stdio after the process is gone; otherwise disposing the proc tears the
        // pipes down beneath the still-running ReadToEndAsync tasks, throwing
        // ObjectDisposedException from inside their continuations.
        string stdout;
        string stderr;
        try
        {
            stdout = await stdoutTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stdout = string.Empty;
        }
        try
        {
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stderr = string.Empty;
        }
        return (proc.ExitCode, stdout, stderr);
    }

    private static string ResolveSecurityBinary()
    {
        // Default location on every supported macOS release; fall back to PATH so users
        // with a non-standard install (e.g. command-line-tools-only) still resolve it.
        if (System.IO.File.Exists("/usr/bin/security"))
        {
            return "/usr/bin/security";
        }
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(System.IO.Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(dir))
                {
                    continue;
                }
                var candidate = System.IO.Path.Combine(dir, "security");
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return "/usr/bin/security";
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
