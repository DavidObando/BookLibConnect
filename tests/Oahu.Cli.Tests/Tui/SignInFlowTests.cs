using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Auth;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Themes;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class SignInFlowTests : IDisposable
{
    public SignInFlowTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    [Fact]
    public void RegionPicker_Returns_Selected_Region()
    {
        var modal = new RegionPickerModal();
        Assert.False(modal.IsComplete);

        // Move down to UK
        modal.HandleKey(MakeKey(ConsoleKey.DownArrow));
        modal.HandleKey(MakeKey(ConsoleKey.Enter));

        Assert.True(modal.IsComplete);
        Assert.False(modal.WasCancelled);
        Assert.Equal("uk", modal.Result);
    }

    [Fact]
    public void RegionPicker_Escape_Cancels()
    {
        var modal = new RegionPickerModal();
        modal.HandleKey(MakeKey(ConsoleKey.Escape));
        Assert.True(modal.IsComplete);
        Assert.True(modal.WasCancelled);
    }

    [Fact]
    public void RegionPicker_First_Item_Is_US()
    {
        var modal = new RegionPickerModal();
        modal.HandleKey(MakeKey(ConsoleKey.Enter));
        Assert.Equal("us", modal.Result);
    }

    [Fact]
    public void ExternalLogin_Accepts_Valid_Url()
    {
        var modal = new ExternalLoginModal(new Uri("https://audible.com/login?code=abc"));
        Assert.False(modal.IsComplete);

        // Type a redirect URL
        foreach (var c in "https://localhost/callback?code=x")
        {
            modal.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
        modal.HandleKey(MakeKey(ConsoleKey.Enter));

        Assert.True(modal.IsComplete);
        Assert.False(modal.WasCancelled);
        Assert.Equal("https://localhost/callback?code=x", modal.Result?.ToString());
    }

    [Fact]
    public void ExternalLogin_Rejects_Invalid_Url()
    {
        var modal = new ExternalLoginModal(new Uri("https://audible.com/login"));
        foreach (var c in "not-a-url")
        {
            modal.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
        modal.HandleKey(MakeKey(ConsoleKey.Enter));
        Assert.False(modal.IsComplete); // Stays open on invalid input
    }

    [Fact]
    public void ExternalLogin_Escape_Cancels()
    {
        var modal = new ExternalLoginModal(new Uri("https://audible.com/login"));
        modal.HandleKey(MakeKey(ConsoleKey.Escape));
        Assert.True(modal.IsComplete);
        Assert.True(modal.WasCancelled);
    }

    [Fact]
    public void ChallengeModal_Accepts_Text()
    {
        var modal = new ChallengeModal
        {
            Title = "MFA",
            Instructions = "Enter code:",
        };
        foreach (var c in "123456")
        {
            modal.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
        modal.HandleKey(MakeKey(ConsoleKey.Enter));
        Assert.True(modal.IsComplete);
        Assert.Equal("123456", modal.Result);
    }

    [Fact]
    public void ChallengeModal_Approval_Requires_Only_Enter()
    {
        var modal = new ChallengeModal
        {
            Title = "Approval",
            Instructions = "Approve on device.",
            ApprovalOnly = true,
        };
        modal.HandleKey(MakeKey(ConsoleKey.Enter));
        Assert.True(modal.IsComplete);
        Assert.Equal(string.Empty, modal.Result);
    }

    [Fact]
    public void CredentialsModal_Submits_Username_And_Password()
    {
        var modal = new CredentialsModal("us");
        foreach (var c in "alice@example.com")
        {
            modal.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
        modal.HandleKey(MakeKey(ConsoleKey.Tab));
        foreach (var c in "hunter2")
        {
            modal.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
        modal.HandleKey(MakeKey(ConsoleKey.Enter));

        Assert.True(modal.IsComplete);
        Assert.False(modal.WasCancelled);
        Assert.NotNull(modal.Result);
        Assert.Equal("alice@example.com", modal.Result!.Username);
        Assert.Equal("hunter2", modal.Result.Password);
    }

    [Fact]
    public void CredentialsModal_Requires_Email_And_Password()
    {
        var modal = new CredentialsModal();
        // Empty submission stays open.
        modal.HandleKey(MakeKey(ConsoleKey.Enter));
        Assert.False(modal.IsComplete);

        // Email only — still blocked.
        foreach (var c in "alice@example.com")
        {
            modal.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
        modal.HandleKey(MakeKey(ConsoleKey.Enter));
        Assert.False(modal.IsComplete);
    }

    [Fact]
    public void CredentialsModal_Escape_Cancels()
    {
        var modal = new CredentialsModal();
        modal.HandleKey(MakeKey(ConsoleKey.Escape));
        Assert.True(modal.IsComplete);
        Assert.True(modal.WasCancelled);
        Assert.Null(modal.Result);
    }

    [Fact]
    public async Task TuiCallbackBroker_MFA_Posts_And_Completes()
    {
        var broker = new TuiCallbackBroker();
        var mfaTask = broker.SolveMfaAsync(new MfaChallenge(), CancellationToken.None);

        Assert.True(broker.HasPending);
        Assert.True(broker.TryDequeue(out var request));
        Assert.NotNull(request);
        Assert.IsType<MfaChallenge>(request!.Challenge);

        request.Completion.TrySetResult("123456");
        var result = await mfaTask;
        Assert.Equal("123456", result);
    }

    [Fact]
    public async Task TuiCallbackBroker_ExternalLogin_Posts_And_Completes()
    {
        var broker = new TuiCallbackBroker();
        var uri = new Uri("https://audible.com/login");
        var loginTask = broker.CompleteExternalLoginAsync(new ExternalLoginChallenge(uri), CancellationToken.None);

        Assert.True(broker.TryDequeue(out var request));
        Assert.NotNull(request);

        request!.Completion.TrySetResult("https://localhost/callback?code=abc");
        var result = await loginTask;
        Assert.Equal("https://localhost/callback?code=abc", result.ToString());
    }

    [Fact]
    public void PulseSpinner_Cycles_Frames_With_Constant_Width()
    {
        var spinner = new Oahu.Cli.Tui.Widgets.PulseSpinner();
        var glyphs = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 12; i++)
        {
            glyphs.Add(spinner.Glyph);
            spinner.Tick();
        }

        // All glyphs are exactly one character (single-width design contract).
        Assert.All(glyphs, g => Assert.Equal(1, g.Length));

        // The set of frames seen should be more than one (the spinner cycles).
        Assert.True(new System.Collections.Generic.HashSet<string>(glyphs).Count > 1);
    }

    [Fact]
    public void PulseSpinner_UseAscii_Renders_Static_Asterisk()
    {
        var spinner = new Oahu.Cli.Tui.Widgets.PulseSpinner { UseAscii = true };
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal("*", spinner.Glyph);
            spinner.Tick();
        }
    }

    [Fact]
    public void SignInFlow_Start_Sets_State()
    {
        var state = new AppShellState();
        var broker = new TuiCallbackBroker();
        var flow = new SignInFlow(new FakeAuthService(), new FakeLibraryService(), broker, state);

        Assert.False(flow.IsRunning);
        flow.Start(CliRegion.Us, new AuthCredentials("alice@example.com", "secret"));
        Assert.True(flow.IsRunning);
        Assert.Equal("signing in…", state.ActivityVerb);
    }

    [Fact]
    public void AppShell_Modal_Receives_Keys()
    {
        var shell = new AppShell(new Spectre.Console.Testing.TestConsole { Profile = { Width = 80, Height = 30 } });
        var modal = new RegionPickerModal();
        shell.ShowModal(modal);
        Assert.NotNull(shell.ActiveModal);

        // Keys go to modal
        shell.Dispatch(MakeKey(ConsoleKey.DownArrow));
        shell.Dispatch(MakeKey(ConsoleKey.Enter));
        Assert.True(modal.IsComplete);
        Assert.Equal("uk", modal.Result);

        // After completion the shell auto-dismisses so the owning screen
        // gets render ticks again.
        Assert.Null(shell.ActiveModal);
    }

    [Fact]
    public void AppShell_Modal_Esc_Cancels_With_Completion_Flag()
    {
        // Repro for the bug where the shell intercepted Esc before the modal
        // could mark itself cancelled. The modal must observe Esc so its owner
        // knows the user explicitly cancelled (vs. external dismissal).
        var shell = new AppShell(new Spectre.Console.Testing.TestConsole { Profile = { Width = 80, Height = 30 } });
        var modal = new RegionPickerModal();
        shell.ShowModal(modal);

        shell.Dispatch(MakeKey(ConsoleKey.Escape));

        Assert.True(modal.IsComplete);
        Assert.True(modal.WasCancelled);
        Assert.Null(shell.ActiveModal);
    }

    [Fact]
    public void AppShell_CtrlC_Dismisses_Modal()
    {
        var shell = new AppShell(new Spectre.Console.Testing.TestConsole { Profile = { Width = 80, Height = 30 } });
        shell.ShowModal(new RegionPickerModal());
        Assert.NotNull(shell.ActiveModal);

        var action = shell.Dispatch(new ConsoleKeyInfo((char)3, ConsoleKey.C, shift: false, alt: false, control: true));
        Assert.Equal(ShellAction.Continue, action);
        Assert.Null(shell.ActiveModal);
    }

    [Fact]
    public void AppShell_Mutable_State_Reflects_In_Header()
    {
        var state = new AppShellState { Profile = "bob", Region = "uk" };
        var console = new Spectre.Console.Testing.TestConsole { Profile = { Width = 80, Height = 30 } };
        console.EmitAnsiSequences = false;
        var shell = new AppShell(console, new AppShellOptions { State = state });

        // Run with EOF to trigger render
        var reader = new ScriptedReader();
        shell.Run(reader);

        var output = console.Output;
        Assert.Contains("bob@uk", output);
    }

    private static ConsoleKeyInfo MakeKey(ConsoleKey key, char ch = '\0', ConsoleModifiers mod = 0)
        => new(ch, key, shift: (mod & ConsoleModifiers.Shift) != 0, alt: (mod & ConsoleModifiers.Alt) != 0, control: (mod & ConsoleModifiers.Control) != 0);

    private sealed class ScriptedReader : AppShell.IKeyReader
    {
        private readonly Queue<ConsoleKeyInfo> queue;

        public ScriptedReader(params ConsoleKeyInfo[] keys)
        {
            queue = new Queue<ConsoleKeyInfo>(keys);
        }

        public ConsoleKeyInfo? ReadKey() => queue.Count == 0 ? null : queue.Dequeue();
    }

    private sealed class FakeAuthService : IAuthService
    {
        public Task<IReadOnlyList<AuthSession>> ListSessionsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuthSession>>(Array.Empty<AuthSession>());

        public Task<AuthSession?> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<AuthSession?>(null);

        public Task<AuthSession> LoginAsync(CliRegion region, IAuthCallbackBroker broker, bool preAmazonUsername = false, CancellationToken ct = default)
        {
            // Simulate external login: the broker must be called
            return Task.FromResult(new AuthSession
            {
                ProfileAlias = "test",
                Region = region,
                AccountId = "acct-1",
            });
        }

        public Task LogoutAsync(string profileAlias, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<AuthSession> RefreshAsync(string profileAlias, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeLibraryService : ILibraryService
    {
        public Task<IReadOnlyList<LibraryItem>> ListAsync(LibraryFilter? filter = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LibraryItem>>(Array.Empty<LibraryItem>());

        public Task<LibraryItem?> GetAsync(string asin, CancellationToken ct = default)
            => Task.FromResult<LibraryItem?>(null);

        public Task<int> SyncAsync(string profileAlias, CancellationToken ct = default)
            => Task.FromResult(5);

        public Task EnsureFreshAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
