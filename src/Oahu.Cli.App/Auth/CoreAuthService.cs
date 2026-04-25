using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Core;
using Oahu.Cli.App.Models;
using Oahu.CommonTypes;
using Oahu.Core;

namespace Oahu.Cli.App.Auth;

/// <summary>
/// Core-backed <see cref="IAuthService"/>. Wraps the singleton
/// <see cref="AudibleClient"/> exposed by <see cref="CoreEnvironment"/>:
/// profiles map to <see cref="AuthSession"/>, aliases come from the books DB
/// (<see cref="AudibleClient.GetAccountAliases"/>), and sign-in routes through
/// <see cref="AudibleClient.ConfigBuildNewLoginUri"/> +
/// <see cref="AudibleClient.ConfigParseExternalLoginResponseAsync"/>.
/// </summary>
public sealed class CoreAuthService : IAuthService
{
    private readonly AudibleClient client;

    public CoreAuthService()
        : this(CoreEnvironment.Client)
    {
    }

    internal CoreAuthService(AudibleClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<AuthSession>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profiles = await client.GetProfilesAsync().ConfigureAwait(false);
        if (profiles is null)
        {
            return Array.Empty<AuthSession>();
        }

        var aliases = client.GetAccountAliases()?.ToDictionary(a => a.AccountId, a => a.Alias, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        return profiles
            .Select(p => ToSession(p, aliases))
            .ToArray();
    }

    public async Task<AuthSession?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Try to load the GUI's "active" profile so ProfileKey reflects the
        // user's actual selection (otherwise we just pick the first).
        await CoreEnvironment.EnsureProfileLoadedAsync().ConfigureAwait(false);

        var sessions = await ListSessionsAsync(cancellationToken).ConfigureAwait(false);

        var current = client.ProfileKey;
        if (current is not null)
        {
            var match = sessions.FirstOrDefault(s =>
                s.Region == ToCliRegion(current.Region) &&
                string.Equals(s.AccountId, current.AccountId, StringComparison.Ordinal));
            if (match is not null)
            {
                return match;
            }
        }
        return sessions.Count > 0 ? sessions[0] : null;
    }

    public async Task<AuthSession> LoginAsync(
        CliRegion region,
        IAuthCallbackBroker broker,
        bool preAmazonUsername = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);
        cancellationToken.ThrowIfCancellationRequested();

        var coreRegion = ToCoreRegion(region);
        var loginUri = client.ConfigBuildNewLoginUri(coreRegion, preAmazonUsername);

        var responseUri = await broker.CompleteExternalLoginAsync(
            new ExternalLoginChallenge(loginUri),
            cancellationToken).ConfigureAwait(false);

        var callbacks = CallbackBridge.ToCoreCallbacks(broker, cancellationToken);

        // ConfigParseExternalLoginResponseAsync runs on whatever thread we're on —
        // it does no UI marshaling internally, but it can call the synchronous
        // callbacks. Since CompleteExternalLoginAsync already returned the URL,
        // those callbacks should not re-fire here; if they do, the bridge above
        // will block on the broker which is safe on a thread-pool thread.
        var result = await Task.Run(
            () => client.ConfigParseExternalLoginResponseAsync(responseUri, callbacks),
            cancellationToken).ConfigureAwait(false);

        if (result is null || result.NewProfileKey is null)
        {
            throw new InvalidOperationException(
                $"Sign-in failed: {result?.Result.ToString() ?? "no response"}.");
        }

        // EAuthorizeResult.DeregistrationFailed is currently emitted whenever a
        // previous profile existed even when sign-in succeeded (see comments in
        // AudibleClient.ConfigParseExternalLoginResponseAsync). Treat it as
        // success-with-warning; the caller is welcome to surface
        // result.PrevDeviceName.
        if (result.Result != EAuthorizeResult.Succ &&
            result.Result != EAuthorizeResult.DeregistrationFailed)
        {
            throw new InvalidOperationException($"Sign-in failed: {result.Result}.");
        }

        var newKey = result.NewProfileKey;
        var aliases = client.GetAccountAliases()?.ToDictionary(a => a.AccountId, a => a.Alias, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        return ToSession(newKey, aliases);
    }

    public async Task LogoutAsync(string profileAlias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
        cancellationToken.ThrowIfCancellationRequested();

        var key = await ResolveKeyByAliasAsync(profileAlias).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No profile with alias '{profileAlias}'.");

        var result = await client.RemoveProfileAsync(key).ConfigureAwait(false);
        if (result < EAuthorizeResult.Succ)
        {
            throw new InvalidOperationException($"Failed to remove profile '{profileAlias}': {result}.");
        }
    }

    public async Task<AuthSession> RefreshAsync(string profileAlias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileAlias);
        cancellationToken.ThrowIfCancellationRequested();

        var key = await ResolveKeyByAliasAsync(profileAlias).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No profile with alias '{profileAlias}'.");

        // ChangeProfileAsync triggers a token refresh when the active profile
        // actually changes. Forcing aliasChanged=true makes it re-issue without
        // changing the alias when the profile was already active.
        await client.ChangeProfileAsync(key, aliasChanged: true).ConfigureAwait(false);

        var sessions = await ListSessionsAsync(cancellationToken).ConfigureAwait(false);
        return sessions.FirstOrDefault(s => string.Equals(s.ProfileAlias, profileAlias, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Profile '{profileAlias}' disappeared after refresh.");
    }

    internal static CliRegion ToCliRegion(ERegion region) => region switch
    {
        ERegion.Us => CliRegion.Us,
        ERegion.Uk => CliRegion.Uk,
        ERegion.De => CliRegion.De,
        ERegion.Fr => CliRegion.Fr,
        ERegion.It => CliRegion.It,
        ERegion.Es => CliRegion.Es,
        ERegion.Jp => CliRegion.Jp,
        ERegion.Au => CliRegion.Au,
        ERegion.Ca => CliRegion.Ca,
        ERegion.In => CliRegion.In,
        ERegion.Br => CliRegion.Br,
        _ => throw new ArgumentOutOfRangeException(nameof(region), region, null),
    };

    internal static ERegion ToCoreRegion(CliRegion region) => region switch
    {
        CliRegion.Us => ERegion.Us,
        CliRegion.Uk => ERegion.Uk,
        CliRegion.De => ERegion.De,
        CliRegion.Fr => ERegion.Fr,
        CliRegion.It => ERegion.It,
        CliRegion.Es => ERegion.Es,
        CliRegion.Jp => ERegion.Jp,
        CliRegion.Au => ERegion.Au,
        CliRegion.Ca => ERegion.Ca,
        CliRegion.In => ERegion.In,
        CliRegion.Br => ERegion.Br,
        _ => throw new ArgumentOutOfRangeException(nameof(region), region, null),
    };

    private async Task<IProfileKey?> ResolveKeyByAliasAsync(string profileAlias)
    {
        var aliases = client.GetAccountAliases()?.ToDictionary(a => a.AccountId, a => a.Alias, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var profiles = await client.GetProfilesAsync().ConfigureAwait(false);
        if (profiles is null)
        {
            return null;
        }

        return profiles.FirstOrDefault(p =>
            aliases.TryGetValue(p.AccountId, out var alias) &&
            string.Equals(alias, profileAlias, StringComparison.Ordinal));
    }

    private static AuthSession ToSession(IProfileKeyEx key, IReadOnlyDictionary<string, string> aliases)
    {
        var alias = aliases.TryGetValue(key.AccountId, out var a) && !string.IsNullOrWhiteSpace(a)
            ? a
            : key.AccountName ?? key.AccountId;
        return new AuthSession
        {
            ProfileAlias = alias,
            Region = ToCliRegion(key.Region),
            AccountId = key.AccountId,
            AccountName = key.AccountName,
            DeviceName = key.DeviceName,
            ExpiresAt = null,
        };
    }

    private static AuthSession ToSession(IProfileKey key, IReadOnlyDictionary<string, string> aliases)
    {
        if (key is IProfileKeyEx ex)
        {
            return ToSession(ex, aliases);
        }

        var alias = aliases.TryGetValue(key.AccountId, out var a) && !string.IsNullOrWhiteSpace(a)
            ? a
            : key.AccountId;
        return new AuthSession
        {
            ProfileAlias = alias,
            Region = ToCliRegion(key.Region),
            AccountId = key.AccountId,
            ExpiresAt = null,
        };
    }
}
