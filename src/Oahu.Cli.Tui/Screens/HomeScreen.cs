using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Auth;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Screens;

/// <summary>
/// Home screen (tab 1). Shows greeting, active profile summary, and quick
/// actions. Per design TUI-exploration §2.
/// </summary>
public sealed class HomeScreen : ITabScreen
{
    private const string SignInActivityMessage = "Signing in to Audible";

    private readonly AppShellState state;
    private readonly Func<IAuthService> authServiceFactory;
    private readonly Func<ILibraryService> libraryServiceFactory;
    private readonly PulseSpinner signInSpinner = new();

    private bool loaded;
    private bool loading;
    private Task? loadTask;
    private int libraryCount;
    private string? accountName;
    private int spinnerTick;

    private IAppShellNavigator? navigator;
    private RegionPickerModal? pendingRegionModal;
    private CredentialsModal? pendingCredentialsModal;
    private CliRegion pendingRegion;
    private SignInFlow? signInFlow;
    private TuiCallbackBroker? signInBroker;

    public HomeScreen(AppShellState state, Func<IAuthService> authServiceFactory, Func<ILibraryService> libraryServiceFactory)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.authServiceFactory = authServiceFactory ?? throw new ArgumentNullException(nameof(authServiceFactory));
        this.libraryServiceFactory = libraryServiceFactory ?? throw new ArgumentNullException(nameof(libraryServiceFactory));
    }

    public string Title => "Home";

    public char NumberKey => '1';

    public bool IsLoading
    {
        get
        {
            // Reconcile with the background load task so AppShell sees the
            // current truth when it samples NeedsTimedRefresh right after a
            // Render call. Without this, a load that finishes between the
            // spinner being drawn and AppShell reading NeedsTimedRefresh would
            // leave a frozen spinner on screen until the next keypress.
            var t = loadTask;
            if (loading && t is not null && t.IsCompleted)
            {
                loading = false;
                loadTask = null;
            }
            return loading;
        }
    }

    /// <summary>
    /// True while a sign-in is in progress (the shell keeps polling the input
    /// loop so background completion is observed without a key press).
    /// </summary>
    public bool NeedsTimedRefresh => IsLoading || signInFlow is not null || pendingRegionModal is not null || pendingCredentialsModal is not null;

    /// <summary>Event raised when the user picks the "sign in" action.</summary>
    public Action? OnSignInRequested { get; set; }

    public IEnumerable<KeyValuePair<string, string?>> Hints
    {
        get
        {
            if (!state.IsSignedIn)
            {
                yield return new("s", "sign in");
            }
            yield return new("r", "refresh");
        }
    }

    public void OnActivated(IAppShellNavigator navigator)
    {
        this.navigator = navigator;
    }

    public IRenderable Render(int width, int height)
    {
        EnsureLoaded();
        DriveSignInFlow();

        // Check if background load completed
        if (loading && loadTask is not null && loadTask.IsCompleted)
        {
            loading = false;
            loadTask = null;
        }

        var lines = new List<IRenderable>();

        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();

        lines.Add(new Markup($"[{brand} bold]Aloha.[/]"));
        lines.Add(new Markup(string.Empty));

        if (state.IsSignedIn)
        {
            lines.Add(new Markup($"[{primary}]Signed in as [bold]{Markup.Escape(state.ProfileDisplay)}[/][/]"));
            if (!string.IsNullOrEmpty(accountName))
            {
                lines.Add(new Markup($"[{secondary}]{Markup.Escape(accountName)}[/]"));
            }
            lines.Add(new Markup(string.Empty));
            if (loading)
            {
                spinnerTick++;
                var spinChars = new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };
                var ch = spinChars[spinnerTick % spinChars.Length];
                lines.Add(new Markup($"[{brand}]{ch}[/] [{secondary}]Loading…[/]"));
            }
            else
            {
                lines.Add(new Markup($"[{secondary}]Library: {libraryCount} title{(libraryCount == 1 ? "" : "s")}[/]"));
            }
            lines.Add(new Markup(string.Empty));
            lines.Add(new Markup($"[{tertiary}]Quick actions:[/]"));
            lines.Add(new Markup($"  [{brand}]2[/] [{secondary}]Browse library[/]"));
            lines.Add(new Markup($"  [{brand}]3[/] [{secondary}]View queue[/]"));
            lines.Add(new Markup($"  [{brand}]6[/] [{secondary}]Settings[/]"));
        }
        else if (signInFlow is not null)
        {
            // Sign-in in progress (between credentials submit and any 2FA modal,
            // or while waiting on Audible to finish registration). Surface a
            // PulseSpinner + the active verb so the home screen doesn't look
            // dead while the background task is working.
            lines.Add(new Markup($"[{secondary}]{Markup.Escape(SignInActivityMessage)}…[/]"));
            lines.Add(new Markup(string.Empty));
            var verb = string.IsNullOrWhiteSpace(state.ActivityVerb) || string.Equals(state.ActivityVerb, "idle", StringComparison.Ordinal)
                ? "working"
                : state.ActivityVerb;
            lines.Add(new Markup($"{signInSpinner.RenderMarkup()} [{primary}]{Markup.Escape(verb)}[/] [{tertiary}]· Esc to cancel[/]"));
        }
        else
        {
            lines.Add(new Markup($"[{secondary}]You're not signed in yet.[/]"));
            lines.Add(new Markup(string.Empty));
            lines.Add(new Markup($"[{secondary}]Press [{brand}]s[/] to sign in to Audible, or use a subcommand:[/]"));
            lines.Add(new Markup($"  [{tertiary}]oahu-cli auth login --region us[/]"));
        }

        return new Padder(new Rows(lines)).Padding(2, 1, 2, 1);
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape when signInFlow is not null:
                signInFlow.Cancel();
                return true;
            case ConsoleKey.S when key.Modifiers == 0:
                if (!state.IsSignedIn)
                {
                    BeginSignIn();
                    return true;
                }
                break;
            case ConsoleKey.R when key.Modifiers == 0:
                Refresh();
                return true;
        }
        return false;
    }

    /// <summary>Refresh the summary data from the services (synchronous, for tests).</summary>
    public void Refresh()
    {
        try
        {
            var auth = authServiceFactory();
            var session = auth.GetActiveAsync().GetAwaiter().GetResult();
            if (session is not null)
            {
                accountName = session.AccountName;
            }

            var lib = libraryServiceFactory();
            var items = lib.ListAsync().GetAwaiter().GetResult();
            libraryCount = items.Count;
            loaded = true;
            loading = false;
        }
        catch
        {
            loaded = true;
            loading = false;
            // Swallow — the TUI must not crash.
        }
    }

    /// <summary>Lazy-load data on first render (non-blocking).</summary>
    internal void EnsureLoaded()
    {
        if (!loaded && !loading)
        {
            loading = true;
            loadTask = Task.Run(() =>
            {
                try
                {
                    var auth = authServiceFactory();
                    var session = auth.GetActiveAsync().GetAwaiter().GetResult();
                    if (session is not null)
                    {
                        accountName = session.AccountName;
                    }

                    var lib = libraryServiceFactory();
                    var items = lib.ListAsync().GetAwaiter().GetResult();
                    libraryCount = items.Count;
                    loaded = true;
                }
                catch
                {
                    loaded = true;
                    // Swallow — the TUI must not crash.
                }
                finally
                {
                    loading = false;
                }
            });
        }
    }

    private void BeginSignIn()
    {
        // Fire the back-compat callback (tests rely on this). Do this first so
        // even environments without a navigator still observe the request.
        OnSignInRequested?.Invoke();

        // Need a navigator (production path) to drive modals; tests typically
        // don't supply one and just observe the callback.
        if (navigator is null
            || pendingRegionModal is not null
            || pendingCredentialsModal is not null
            || signInFlow is not null)
        {
            return;
        }

        pendingRegionModal = new RegionPickerModal();
        navigator.ShowModal(pendingRegionModal);
    }

    private void DriveSignInFlow()
    {
        if (navigator is null)
        {
            return;
        }

        // Step 1: region picker → either advance to credentials or bail out.
        if (pendingRegionModal is not null)
        {
            // External dismissal (e.g. Ctrl+C cleared the modal without
            // setting IsComplete): treat as cancel so the screen state matches
            // what the user sees, and the next `s` press can start over.
            if (!pendingRegionModal.IsComplete && !ReferenceEquals(navigator.ActiveModal, pendingRegionModal))
            {
                pendingRegionModal = null;
                return;
            }

            if (!pendingRegionModal.IsComplete)
            {
                return;
            }

            var modal = pendingRegionModal;
            pendingRegionModal = null;

            if (modal.WasCancelled || string.IsNullOrEmpty(modal.Result))
            {
                return;
            }

            if (!TryParseRegion(modal.Result, out var region))
            {
                navigator.ShowToast($"Unknown region '{modal.Result}'.");
                return;
            }

            pendingRegion = region;
            pendingCredentialsModal = new CredentialsModal(region.ToString().ToLowerInvariant());
            navigator.ShowModal(pendingCredentialsModal);
            return;
        }

        // Step 2: credentials modal → start the SignInFlow.
        if (pendingCredentialsModal is not null)
        {
            if (!pendingCredentialsModal.IsComplete && !ReferenceEquals(navigator.ActiveModal, pendingCredentialsModal))
            {
                pendingCredentialsModal = null;
                return;
            }

            if (!pendingCredentialsModal.IsComplete)
            {
                return;
            }

            var modal = pendingCredentialsModal;
            pendingCredentialsModal = null;

            if (modal.WasCancelled || modal.Result is null)
            {
                return;
            }

            try
            {
                signInBroker = new TuiCallbackBroker();
                navigator.SetBroker(signInBroker);
                signInFlow = new SignInFlow(
                    authServiceFactory(),
                    libraryServiceFactory(),
                    signInBroker,
                    state);
                signInFlow.Start(pendingRegion, modal.Result);
            }
            catch (Exception ex)
            {
                navigator.ShowToast($"Sign-in failed to start: {ex.Message}");
                TeardownSignInFlow();
            }

            return;
        }

        // Step 3: SignInFlow running — poll for completion / error.
        if (signInFlow is not null)
        {
            var result = signInFlow.Poll();
            if (result is not null)
            {
                // Success: state.Profile / Region are already populated by the
                // background task. Refresh the home summary and tear down.
                accountName = result.Session.AccountName;
                libraryCount = result.LibraryCount;
                loaded = true;
                loading = false;
                navigator.ShowToast($"Signed in as {result.Session.ProfileAlias} · {result.LibraryCount} title{(result.LibraryCount == 1 ? string.Empty : "s")}");
                TeardownSignInFlow();
                return;
            }

            if (!signInFlow.IsRunning)
            {
                // Failed or cancelled.
                var msg = signInFlow.ErrorMessage ?? "Sign-in did not complete.";
                navigator.ShowToast(msg);
                TeardownSignInFlow();
            }
        }
    }

    private void TeardownSignInFlow()
    {
        try
        {
            signInFlow?.Dispose();
        }
        catch
        {
            // ignore — we're tearing down.
        }
        signInFlow = null;
        signInBroker = null;
        navigator?.SetBroker(null);
    }

    private static bool TryParseRegion(string code, out CliRegion region)
    {
        return Enum.TryParse(code, ignoreCase: true, out region);
    }
}
