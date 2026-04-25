using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;

namespace Oahu.Cli.App.Auth;

/// <summary>In-memory <see cref="IAuthService"/> for tests and for offline development.</summary>
public sealed class FakeAuthService : IAuthService
{
    private readonly object @lock = new();
    private readonly List<AuthSession> sessions = new();
    private string? activeAlias;

    public Task<IReadOnlyList<AuthSession>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (@lock)
        {
            return Task.FromResult<IReadOnlyList<AuthSession>>(sessions.ToArray());
        }
    }

    public Task<AuthSession?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (@lock)
        {
            return Task.FromResult(sessions.FirstOrDefault(s => s.ProfileAlias == activeAlias));
        }
    }

    public Task<AuthSession> LoginAsync(
        CliRegion region,
        IAuthCallbackBroker broker,
        bool preAmazonUsername = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        cancellationToken.ThrowIfCancellationRequested();

        // Fakes don't actually call broker; tests drive the broker explicitly when needed.
        var alias = $"{region.ToString().ToLowerInvariant()}-fake";
        var session = new AuthSession
        {
            ProfileAlias = alias,
            Region = region,
            AccountId = $"acct-{Guid.NewGuid():n}",
            AccountName = "Fake User",
            DeviceName = "fake-device",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };

        lock (@lock)
        {
            sessions.RemoveAll(s => s.ProfileAlias == alias);
            sessions.Add(session);
            activeAlias = alias;
        }
        return Task.FromResult(session);
    }

    public Task LogoutAsync(string profileAlias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
        cancellationToken.ThrowIfCancellationRequested();
        lock (@lock)
        {
            sessions.RemoveAll(s => s.ProfileAlias == profileAlias);
            if (activeAlias == profileAlias)
            {
                activeAlias = sessions.FirstOrDefault()?.ProfileAlias;
            }
        }
        return Task.CompletedTask;
    }

    public Task<AuthSession> RefreshAsync(string profileAlias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
        cancellationToken.ThrowIfCancellationRequested();
        lock (@lock)
        {
            var existing = sessions.FirstOrDefault(s => s.ProfileAlias == profileAlias)
                ?? throw new InvalidOperationException($"No session for profile '{profileAlias}'.");
            var refreshed = existing with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
            sessions.RemoveAll(s => s.ProfileAlias == profileAlias);
            sessions.Add(refreshed);
            return Task.FromResult(refreshed);
        }
    }
}
