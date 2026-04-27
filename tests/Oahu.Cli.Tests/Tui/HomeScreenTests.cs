using System;
using System.Collections.Generic;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Screens;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Themes;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class HomeScreenTests : IDisposable
{
    public HomeScreenTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    private static ConsoleKeyInfo Key(char ch, ConsoleKey k = ConsoleKey.NoName, ConsoleModifiers mod = 0)
        => new(ch, k, shift: (mod & ConsoleModifiers.Shift) != 0, alt: (mod & ConsoleModifiers.Alt) != 0, control: (mod & ConsoleModifiers.Control) != 0);

    private static HomeScreen CreateScreen(AppShellState? state = null)
    {
        state ??= new AppShellState();
        return new HomeScreen(
            state,
            () => new FakeAuthService(),
            () => new FakeLibraryService());
    }

    [Fact]
    public void Render_Shows_Not_Signed_In_When_No_Profile()
    {
        var screen = CreateScreen();
        var r = screen.Render(80, 20);
        Assert.NotNull(r);
    }

    [Fact]
    public void Render_Shows_Profile_When_Signed_In()
    {
        var state = new AppShellState { Profile = "alice", Region = "us" };
        var screen = CreateScreen(state);
        var r = screen.Render(80, 20);
        Assert.NotNull(r);
    }

    [Fact]
    public void S_Key_Fires_SignIn_When_Not_Signed_In()
    {
        var screen = CreateScreen();
        var fired = false;
        screen.OnSignInRequested = () => fired = true;
        screen.HandleKey(Key('s', ConsoleKey.S));
        Assert.True(fired);
    }

    [Fact]
    public void S_Key_Ignored_When_Signed_In()
    {
        var state = new AppShellState { Profile = "alice" };
        var screen = CreateScreen(state);
        var fired = false;
        screen.OnSignInRequested = () => fired = true;
        screen.HandleKey(Key('s', ConsoleKey.S));
        Assert.False(fired);
    }

    [Fact]
    public void S_Key_Opens_Region_Picker_When_Navigator_Available()
    {
        var screen = CreateScreen();
        var nav = new RecordingNavigator();
        screen.OnActivatedAsync(nav);

        screen.HandleKey(Key('s', ConsoleKey.S));

        Assert.NotNull(nav.LastModal);
        Assert.IsType<Oahu.Cli.Tui.Auth.RegionPickerModal>(nav.LastModal);
    }

    [Fact]
    public void Region_Cancel_Tears_Down_Without_Starting_Flow()
    {
        var screen = CreateScreen();
        var nav = new RecordingNavigator();
        screen.OnActivatedAsync(nav);

        screen.HandleKey(Key('s', ConsoleKey.S));
        var modal = (Oahu.Cli.Tui.Auth.RegionPickerModal)nav.LastModal!;
        modal.HandleKey(Key((char)0, ConsoleKey.Escape));

        // First Render after completion drives the state machine forward.
        screen.Render(80, 20);

        Assert.False(screen.NeedsTimedRefresh);
        Assert.Null(nav.LastBroker);
    }

    [Fact]
    public void S_Key_Works_Again_After_External_Modal_Dismissal()
    {
        // Repro for the bug where Esc-via-shell left HomeScreen thinking a
        // modal was still pending, so the next `s` press silently did nothing.
        var screen = CreateScreen();
        var nav = new RecordingNavigator();
        screen.OnActivatedAsync(nav);

        screen.HandleKey(Key('s', ConsoleKey.S));
        var firstModal = nav.LastModal;
        Assert.NotNull(firstModal);

        // Simulate AppShell's external dismiss (Ctrl+C path) without ever
        // setting modal.IsComplete.
        nav.DismissModal();
        screen.Render(80, 20);

        // Now `s` should open a fresh region picker.
        screen.HandleKey(Key('s', ConsoleKey.S));
        Assert.NotNull(nav.LastModal);
        Assert.NotSame(firstModal, nav.LastModal);
    }

    [Fact]
    public void Region_Selection_Advances_To_Credentials_Modal()
    {
        var screen = CreateScreen();
        var nav = new RecordingNavigator();
        screen.OnActivatedAsync(nav);

        screen.HandleKey(Key('s', ConsoleKey.S));
        var region = (Oahu.Cli.Tui.Auth.RegionPickerModal)nav.LastModal!;
        region.HandleKey(Key((char)0, ConsoleKey.Enter));

        // Render runs the state machine: it should swap the modal to
        // CredentialsModal (default to programmatic / username+password flow,
        // matching the Avalonia GUI's "direct login" path).
        screen.Render(80, 20);

        Assert.NotNull(nav.LastModal);
        Assert.IsType<Oahu.Cli.Tui.Auth.CredentialsModal>(nav.LastModal);
        Assert.Null(nav.LastBroker);
    }

    [Fact]
    public void S_Key_Works_Again_After_External_Credentials_Dismissal()
    {
        var screen = CreateScreen();
        var nav = new RecordingNavigator();
        screen.OnActivatedAsync(nav);

        screen.HandleKey(Key('s', ConsoleKey.S));
        var region = (Oahu.Cli.Tui.Auth.RegionPickerModal)nav.LastModal!;
        region.HandleKey(Key((char)0, ConsoleKey.Enter));
        screen.Render(80, 20);

        Assert.IsType<Oahu.Cli.Tui.Auth.CredentialsModal>(nav.LastModal);

        nav.DismissModal();
        screen.Render(80, 20);

        screen.HandleKey(Key('s', ConsoleKey.S));
        Assert.IsType<Oahu.Cli.Tui.Auth.RegionPickerModal>(nav.LastModal);
    }

    [Fact]
    public void Title_Is_Home()
    {
        var screen = CreateScreen();
        Assert.Equal("Home", screen.Title);
        Assert.Equal('1', screen.NumberKey);
    }

    private sealed class RecordingNavigator : IAppShellNavigator
    {
        public IModal? LastModal { get; private set; }
        public string? LastToast { get; private set; }
        public bool DismissCalled { get; private set; }
        public Oahu.Cli.Tui.Auth.TuiCallbackBroker? LastBroker { get; private set; }

        public IModal? ActiveModal => LastModal;
        public void SwitchToTab(char numberKey) { }
        public void ShowModal(IModal modal) => LastModal = modal;
        public void ShowToast(string message) => LastToast = message;
        public void DismissModal() { DismissCalled = true; LastModal = null; }
        public void SetBroker(Oahu.Cli.Tui.Auth.TuiCallbackBroker? broker) => LastBroker = broker;
        public void TrackLoad(System.Threading.Tasks.Task loadTask) { }
    }

    private sealed class FakeAuthService : IAuthService
    {
        public Task<IReadOnlyList<AuthSession>> ListSessionsAsync(System.Threading.CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuthSession>>(Array.Empty<AuthSession>());

        public Task<AuthSession?> GetActiveAsync(System.Threading.CancellationToken ct = default)
            => Task.FromResult<AuthSession?>(null);

        public Task<AuthSession> LoginAsync(CliRegion region, IAuthCallbackBroker broker, bool preAmazonUsername = false, System.Threading.CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task LogoutAsync(string profileAlias, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<AuthSession> RefreshAsync(string profileAlias, System.Threading.CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeLibraryService : ILibraryService
    {
        public Task<IReadOnlyList<LibraryItem>> ListAsync(LibraryFilter? filter = null, System.Threading.CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LibraryItem>>(Array.Empty<LibraryItem>());

        public Task<LibraryItem?> GetAsync(string asin, System.Threading.CancellationToken ct = default)
            => Task.FromResult<LibraryItem?>(null);

        public Task<int> SyncAsync(string profileAlias, System.Threading.CancellationToken ct = default)
            => Task.FromResult(0);

        public Task EnsureFreshAsync(System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
