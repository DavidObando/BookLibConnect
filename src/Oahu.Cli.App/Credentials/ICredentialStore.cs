using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Credentials;

/// <summary>
/// Stores opaque secrets keyed by an account alias. Implementations bind to the
/// platform's native keyring (DPAPI / Keychain / libsecret); when no native keyring
/// is available the factory returns <see cref="UnsupportedCredentialStore"/> so the
/// CLI can fail closed with a clear error rather than silently store secrets in
/// a file.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Identifier used by the CLI when surfacing diagnostics ("dpapi", "keychain", "secret-tool", "unsupported").</summary>
    string Provider { get; }

    Task<string?> GetAsync(string account, CancellationToken cancellationToken = default);

    Task SetAsync(string account, string secret, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string account, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListAccountsAsync(CancellationToken cancellationToken = default);
}
