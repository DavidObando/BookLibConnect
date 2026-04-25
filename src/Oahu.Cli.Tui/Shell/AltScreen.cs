using System;
using System.IO;

namespace Oahu.Cli.Tui.Shell;

/// <summary>
/// Tiny ANSI helper that toggles the alt-screen buffer and cursor visibility.
///
/// We intentionally bypass Spectre here: <see cref="Enter"/> / <see cref="Leave"/>
/// must work even when the AppShell crashes mid-render and the only thing left
/// running is the exit-trap installed by <c>CliEnvironment</c>.
/// </summary>
public static class AltScreen
{
    /// <summary>DECSET 1049 — switch to alt-screen, save cursor.</summary>
    public const string EnterSequence = "\u001b[?1049h\u001b[?25l";

    /// <summary>DECRST 1049 — restore primary buffer, restore cursor.</summary>
    public const string LeaveSequence = "\u001b[?25h\u001b[?1049l";

    /// <summary>Move cursor to (1,1) and clear screen — used when redrawing without leaving alt-screen.</summary>
    public const string ClearSequence = "\u001b[H\u001b[2J";

    public static void Enter(TextWriter? writer = null)
    {
        var w = writer ?? Console.Out;
        try
        {
            w.Write(EnterSequence);
            w.Flush();
        }
        catch
        {
            // Best effort: a TTY that can't accept ANSI shouldn't have got here.
        }
    }

    public static void Leave(TextWriter? writer = null)
    {
        var w = writer ?? Console.Out;
        try
        {
            w.Write(LeaveSequence);
            w.Flush();
        }
        catch
        {
            // ignore
        }
    }

    public static void Clear(TextWriter? writer = null)
    {
        var w = writer ?? Console.Out;
        try
        {
            w.Write(ClearSequence);
            w.Flush();
        }
        catch
        {
            // ignore
        }
    }
}
