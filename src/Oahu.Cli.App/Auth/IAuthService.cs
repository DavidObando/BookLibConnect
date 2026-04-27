using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Auth;

/// <summary>
/// Credentials supplied by the user for in-process (programmatic) sign-in.
/// </summary>
public sealed record AuthCredentials(string Username, string Password);

/// <summary>
/// Audible authentication boundary. Phase 3 ships this interface plus an in-memory
/// <see cref="FakeAuthService"/>. The Core-backed implementation that wraps
/// <c>Oahu.Core.AudibleLogin</c> + <c>Authorize</c> + the <c>IAuthCallbackBroker</c>
/// lands in Phase 4 alongside the <c>auth login/status/logout</c> commands, when
/// the exact field set the commands need is settled.
/// </summary>
public interface IAuthService
{
    /// <summary>List every signed-in profile. Empty when no one is signed in yet.</summary>
    Task<IReadOnlyList<AuthSession>> ListSessionsAsync(CancellationToken cancellationToken = default);

    Task<AuthSession?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Browser-based sign-in: builds an Audible OAuth URL, asks the broker for
    /// the redirect URL the user pasted back, and registers the device.
    /// </summary>
    Task<AuthSession> LoginAsync(
        CliRegion region,
        IAuthCallbackBroker broker,
        bool preAmazonUsername = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Programmatic (in-process) sign-in with <paramref name="credentials"/>.
    /// CAPTCHA, MFA, CVF, and approval challenges are routed through the broker.
    /// Mirrors the GUI's "direct login" path (see Avalonia ProfileWizardViewModel).
    /// </summary>
    Task<AuthSession> LoginWithCredentialsAsync(
        CliRegion region,
        IAuthCallbackBroker broker,
        AuthCredentials credentials,
        bool preAmazonUsername = false,
        CancellationToken cancellationToken = default)
        => throw new System.NotSupportedException(
            $"{GetType().Name} does not support credentials-based sign-in.");

    Task LogoutAsync(string profileAlias, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the access token for <paramref name="profileAlias"/>; returns the updated session.</summary>
    Task<AuthSession> RefreshAsync(string profileAlias, CancellationToken cancellationToken = default);
}
