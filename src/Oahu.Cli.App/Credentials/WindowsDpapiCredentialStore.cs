using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Credentials;

/// <summary>
/// Windows DPAPI-backed credential store. Secrets are encrypted with
/// <see cref="DataProtectionScope.CurrentUser"/> so they're scoped to the signed-in
/// Windows account; the encrypted blob lives next to <c>config.json</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiCredentialStore : ICredentialStore
{
    private readonly string filePath;
    private readonly object writeLock = new();

    public WindowsDpapiCredentialStore(string filePath)
    {
        this.filePath = filePath;
    }

    public string Provider => "dpapi";

    public Task<string?> GetAsync(string account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        cancellationToken.ThrowIfCancellationRequested();
        var map = LoadLocked();
        return Task.FromResult(map.TryGetValue(account, out var v) ? v : null);
    }

    public Task SetAsync(string account, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        lock (writeLock)
        {
            var map = LoadLocked();
            map[account] = secret;
            PersistLocked(map);
        }
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        cancellationToken.ThrowIfCancellationRequested();
        lock (writeLock)
        {
            var map = LoadLocked();
            if (!map.Remove(account))
            {
                return Task.FromResult(false);
            }
            PersistLocked(map);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<string>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(LoadLocked().Keys.ToArray());
    }

    private Dictionary<string, string> LoadLocked()
    {
        if (!File.Exists(filePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var encrypted = File.ReadAllBytes(filePath);
        if (encrypted.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var json = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(json));
        return map is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
    }

    private void PersistLocked(Dictionary<string, string> map)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(map);
        var encrypted = ProtectedData.Protect(json, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        var tmp = filePath + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(encrypted);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, filePath, overwrite: true);
    }
}
