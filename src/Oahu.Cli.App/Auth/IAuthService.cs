using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Auth;

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

    Task<AuthSession> LoginAsync(
        CliRegion region,
        IAuthCallbackBroker broker,
        bool preAmazonUsername = false,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(string profileAlias, CancellationToken cancellationToken = default);

    /// <summary>Refreshes the access token for <paramref name="profileAlias"/>; returns the updated session.</summary>
    Task<AuthSession> RefreshAsync(string profileAlias, CancellationToken cancellationToken = default);
}
