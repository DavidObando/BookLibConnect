using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Credentials;

/// <summary>
/// Linux Secret-Service-backed store driven by <c>secret-tool</c> (libsecret).
/// Requires a running secret-service daemon (gnome-keyring, KWallet's secret-service
/// bridge, KeePassXC, …) and a DBus session; missing tools / daemons surface as
/// <see cref="CredentialStoreUnavailableException"/> via the factory.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretToolCredentialStore : ICredentialStore
{
    private const string SchemaService = "service";
    private const string SchemaAccount = "account";
    private const string DefaultService = "oahu-cli";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly string serviceName;
    private readonly string secretTool;

    public LinuxSecretToolCredentialStore(string? serviceName = null, string? secretToolPath = null)
    {
        this.serviceName = serviceName ?? DefaultService;
        this.secretTool = secretToolPath ?? "secret-tool";
    }

    public string Provider => "secret-tool";

    public async Task<string?> GetAsync(string account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        var (code, stdout, _) = await RunAsync(
            new[] { "lookup", SchemaService, serviceName, SchemaAccount, account },
            stdin: null,
            cancellationToken).ConfigureAwait(false);
        if (code != 0)
        {
            return null; // secret-tool returns non-zero when the entry is missing.
        }
        return stdout.TrimEnd('\n', '\r');
    }

    public async Task SetAsync(string account, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentNullException.ThrowIfNull(secret);
        var (code, _, stderr) = await RunAsync(
            new[] { "store", "--label", $"{serviceName} ({account})", SchemaService, serviceName, SchemaAccount, account },
            stdin: secret,
            cancellationToken).ConfigureAwait(false);
        if (code != 0)
        {
            throw new CredentialStoreOperationException($"store: {stderr.Trim()}", code);
        }
    }

    public async Task<bool> DeleteAsync(string account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        var (code, _, _) = await RunAsync(
            new[] { "clear", SchemaService, serviceName, SchemaAccount, account },
            stdin: null,
            cancellationToken).ConfigureAwait(false);
        return code == 0;
    }

    public Task<IReadOnlyList<string>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        // secret-tool has no "search by attribute" with an iterable result; defer to Phase 4.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private async Task<(int Code, string StdOut, string StdErr)> RunAsync(
        string[] args,
        string? stdin,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(secretTool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        // Bound the wait so a hung secret-service daemon (e.g., DBus stuck, KWallet
        // prompt that nobody answers) cannot deadlock the CLI forever.
        using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        Process proc;
        try
        {
            proc = Process.Start(psi)
                ?? throw new CredentialStoreUnavailableException($"Could not start {secretTool}.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new CredentialStoreUnavailableException($"{secretTool} is not installed: {ex.Message}");
        }
        using (proc)
        {
            if (stdin is not null)
            {
                try
                {
                    await proc.StandardInput.WriteAsync(stdin.AsMemory(), token).ConfigureAwait(false);
                }
                finally
                {
                    // Close stdin even if WriteAsync threw; otherwise secret-tool blocks
                    // forever waiting for EOF and the WaitForExit below times out.
                    try
                    {
                        proc.StandardInput.Close();
                    }
                    catch
                    {
                        // best-effort
                    }
                }
            }
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
    }
}
