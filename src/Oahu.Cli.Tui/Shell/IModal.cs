using System;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Shell;

/// <summary>
/// A single active modal overlay. The AppShell routes all key input to the
/// active modal before the tab screen. When <see cref="IsComplete"/> becomes
/// true the shell removes the modal and acts on the result.
/// </summary>
public interface IModal
{
    /// <summary>Render the modal body (shown in the content area, replacing the tab screen).</summary>
    IRenderable Render(int width, int height);

    /// <summary>Handle a key press. Return true if consumed.</summary>
    bool HandleKey(ConsoleKeyInfo key);

    /// <summary>True when the modal has finished (user submitted or cancelled).</summary>
    bool IsComplete { get; }

    /// <summary>True when the user cancelled (Esc). False when the user submitted a result.</summary>
    bool WasCancelled { get; }
}

/// <summary>A modal that produces a typed result.</summary>
public interface IModal<T> : IModal
{
    /// <summary>The result produced by the modal (valid only when <see cref="IModal.IsComplete"/> and not cancelled).</summary>
    T? Result { get; }
}
