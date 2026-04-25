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
}
