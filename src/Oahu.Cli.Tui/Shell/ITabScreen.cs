using System;
using System.Collections.Generic;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Shell;

/// <summary>
/// Contract for a tab inside <see cref="AppShell"/>. Phase 6 ships
/// placeholder implementations; phases 7–8 replace them with real screens.
/// </summary>
public interface ITabScreen
{
    /// <summary>Display title in the tab strip (e.g. "Home", "Library").</summary>
    string Title { get; }

    /// <summary>The numeric jump key (e.g. <c>'1'</c> for Home).</summary>
    char NumberKey { get; }

    /// <summary>
    /// Build the body renderable for the current size. The shell already
    /// renders the chrome (header / tabs / hint bar); the screen owns
    /// only the inside of the body box.
    /// </summary>
    IRenderable Render(int width, int height);

    /// <summary>
    /// Handle a key the shell did not handle as a global. Return true to
    /// signal that the screen consumed the key (the shell skips its own
    /// fallback handlers).
    /// </summary>
    bool HandleKey(ConsoleKeyInfo key);

    /// <summary>Hints contributed by this screen (mixed into the global hint bar).</summary>
    IEnumerable<KeyValuePair<string, string?>> Hints { get; }

    /// <summary>
    /// True when the screen is loading data asynchronously. The shell
    /// switches to a timed-poll input loop so the spinner animates.
    /// </summary>
    bool IsLoading => false;

    /// <summary>
    /// True when the screen wants the shell to re-render on a 100 ms tick
    /// even while no key has been pressed. Defaults to <see cref="IsLoading"/>;
    /// JobsScreen overrides this to keep ticking while observing.
    /// </summary>
    bool NeedsTimedRefresh => IsLoading;

    /// <summary>Called when this tab becomes the active one (default: no-op).</summary>
    void OnActivated(IAppShellNavigator navigator)
    {
    }

    /// <summary>Called when this tab is no longer the active one (default: no-op).</summary>
    void OnDeactivated()
    {
    }

    /// <summary>Called once when the shell is shutting down (default: no-op).</summary>
    void OnShutdown()
    {
    }
}

/// <summary>
/// Optional shell-side service exposed to screens for tab navigation, modal
/// presentation, and toast notifications. Implemented by <see cref="AppShell"/>.
/// </summary>
public interface IAppShellNavigator
{
    /// <summary>Switch to the tab whose <see cref="ITabScreen.NumberKey"/> matches.</summary>
    void SwitchToTab(char numberKey);

    /// <summary>Show a modal overlay; routes keys until the modal completes.</summary>
    void ShowModal(IModal modal);

    /// <summary>Show a transient one-line toast (warning style).</summary>
    void ShowToast(string message);
}

/// <summary>
/// Optional capability for screens that want to drive the terminal's
/// title-bar / dock progress indicator (OSC 9;4). The shell queries the
/// active screen each frame and appends the returned escape sequence to
/// the same atomic write as the rendered frame.
/// </summary>
public interface ITerminalProgressProvider
{
    /// <summary>
    /// Return the current OSC 9;4 sequence to emit (e.g. <c>"\e]9;4;1;42\e\\"</c>),
    /// or null to emit nothing this frame. Return the clear sequence
    /// (<c>"\e]9;4;0;0\e\\"</c>) to remove the indicator.
    /// </summary>
    string? GetTerminalProgressSequence();
}
