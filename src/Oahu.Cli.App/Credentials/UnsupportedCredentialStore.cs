using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Credentials;

/// <summary>
/// Sentinel store returned when no native keyring is available. Every method throws
/// <see cref="CredentialStoreUnavailableException"/>, signalling the caller (typically
/// <c>oahu-cli auth login</c>) to print a clear diagnostic with remediation steps
/// rather than fall back to insecure file storage.
/// </summary>
public sealed class UnsupportedCredentialStore : ICredentialStore
{
    private readonly string reason;

    public UnsupportedCredentialStore(string reason)
    {
        this.reason = reason;
    }

    public string Provider => "unsupported";

    public Task<string?> GetAsync(string account, CancellationToken cancellationToken = default) => throw Make();

    public Task SetAsync(string account, string secret, CancellationToken cancellationToken = default) => throw Make();

    public Task<bool> DeleteAsync(string account, CancellationToken cancellationToken = default) => throw Make();

    public Task<IReadOnlyList<string>> ListAccountsAsync(CancellationToken cancellationToken = default) => throw Make();

    private CredentialStoreUnavailableException Make() => new(reason);
}

public sealed class CredentialStoreUnavailableException : Exception
{
    public CredentialStoreUnavailableException(string reason)
        : base($"No supported credential store is available on this system: {reason}")
    {
    }
}
