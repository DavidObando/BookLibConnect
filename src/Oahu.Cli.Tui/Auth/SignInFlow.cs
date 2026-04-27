using System;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Shell;

namespace Oahu.Cli.Tui.Auth;

/// <summary>
/// Result of a completed sign-in flow.
/// </summary>
public sealed record SignInResult
{
    public required AuthSession Session { get; init; }

    public int LibraryCount { get; init; }
}

/// <summary>
/// Orchestrates sign-in: region picker → LoginAsync (which triggers
/// external-login and optional challenge modals via the broker) → sync library.
/// Runs the auth call on a background task; the broker posts modal requests
/// that the AppShell picks up in its input loop.
/// </summary>
public sealed class SignInFlow : IDisposable
{
    private readonly IAuthService authService;
    private readonly ILibraryService libraryService;
    private readonly TuiCallbackBroker broker;
    private readonly AppShellState state;

    private CancellationTokenSource? cts;
    private Task<SignInResult>? authTask;

    public SignInFlow(
        IAuthService authService,
        ILibraryService libraryService,
        TuiCallbackBroker broker,
        AppShellState state)
    {
        this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
        this.libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        this.broker = broker ?? throw new ArgumentNullException(nameof(broker));
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>True while the background auth task is running.</summary>
    public bool IsRunning => authTask is not null && !authTask.IsCompleted;

    /// <summary>Error message if auth failed.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>The broker that the AppShell polls for modal requests.</summary>
    public TuiCallbackBroker Broker => broker;

    /// <summary>Start the login process for the given region on a background thread.</summary>
    public void Start(CliRegion region, AuthCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        StartCore(region, credentials);
    }

    /// <summary>Browser-based start (legacy / fallback). Prefer the credentials overload —
    /// the TUI default flow asks the user for username + password and routes
    /// 2FA / CAPTCHA via <see cref="ChallengeModal"/>.</summary>
    public void StartBrowser(CliRegion region) => StartCore(region, credentials: null);

    /// <summary>
    /// Poll the auth task status. Returns a result when complete, null while
    /// still running. Captures exceptions into <see cref="ErrorMessage"/>.
    /// </summary>
    public SignInResult? Poll()
    {
        if (authTask is null)
        {
            return null;
        }

        if (!authTask.IsCompleted)
        {
            return null;
        }

        if (authTask.IsFaulted)
        {
            var ex = authTask.Exception?.InnerException ?? authTask.Exception;
            ErrorMessage = ex?.Message ?? "Sign-in failed.";
            state.ActivityVerb = "idle";
            authTask = null;
            return null;
        }

        if (authTask.IsCanceled)
        {
            ErrorMessage = "Sign-in cancelled.";
            state.ActivityVerb = "idle";
            authTask = null;
            return null;
        }

        var result = authTask.Result;
        authTask = null;
        return result;
    }

    /// <summary>Cancel the in-progress auth flow.</summary>
    public void Cancel()
    {
        cts?.Cancel();
        state.ActivityVerb = "idle";
    }

    /// <summary>Disposes the linked CancellationTokenSource. Safe to call multiple times.</summary>
    public void Dispose()
    {
        var local = cts;
        cts = null;
        try
        {
            local?.Cancel();
        }
        catch
        {
            // already disposed — safe to ignore
        }
        local?.Dispose();
    }

    private void StartCore(CliRegion region, AuthCredentials? credentials)
    {
        cts = new CancellationTokenSource();
        ErrorMessage = null;
        state.ActivityVerb = "signing in…";
        authTask = Task.Run(async () =>
        {
            var session = credentials is not null
                ? await authService.LoginWithCredentialsAsync(region, broker, credentials, preAmazonUsername: false, cts.Token).ConfigureAwait(false)
                : await authService.LoginAsync(region, broker, preAmazonUsername: false, cts.Token).ConfigureAwait(false);

            state.Profile = session.ProfileAlias;
            state.Region = session.Region.ToString().ToLowerInvariant();
            state.ActivityVerb = "syncing library…";

            var count = await libraryService.SyncAsync(session.ProfileAlias, cts.Token).ConfigureAwait(false);

            state.ActivityVerb = "idle";
            return new SignInResult { Session = session, LibraryCount = count };
        });
    }
}
