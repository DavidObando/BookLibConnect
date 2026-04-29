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

    /// <summary>Move cursor to (1,1) without clearing — for flicker-free repaints.</summary>
    public const string HomeSequence = "\u001b[H";

    /// <summary>Erase from cursor to end of screen — cleans up leftover lines after a shorter frame.</summary>
    public const string EraseToEndSequence = "\u001b[J";

    /// <summary>DEC private mode 2026: begin synchronized update — terminal buffers output until <see cref="SyncEndSequence"/>.</summary>
    public const string SyncStartSequence = "\u001b[?2026h";

    /// <summary>DEC private mode 2026: end synchronized update — terminal renders the buffered frame atomically.</summary>
    public const string SyncEndSequence = "\u001b[?2026l";

    /// <summary>
    /// Normalize newlines and inject <c>\e[K</c> (erase-to-end-of-line) before each <c>\n</c>
    /// so each rendered line clears any residual characters from a longer previous frame.
    ///
    /// CRLF must be normalized to LF first: on Windows a naive <c>Replace("\n", "\e[K\n")</c>
    /// over <c>\r\n</c> input produces <c>\r\e[K\n</c>, which moves the cursor to column 1
    /// before erasing — wiping every line's content. Lone <c>\r</c> is also stripped
    /// defensively (Spectre.Console's Rule/TabStrip/Markup do not emit bare CRs).
    /// </summary>
    public static string InjectEraseBeforeNewlines(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return raw ?? string.Empty;
        }

        return raw
            .Replace("\r\n", "\n")
            .Replace("\r", string.Empty)
            .Replace("\n", "\u001b[K\n");
    }

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

    /// <summary>Move cursor to (1,1) without clearing for flicker-free redraw.</summary>
    public static void Home(TextWriter? writer = null)
    {
        var w = writer ?? Console.Out;
        try
        {
            w.Write(HomeSequence);
            w.Flush();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Erase from cursor to end of screen — call after rendering to clean leftover lines.</summary>
    public static void EraseToEnd(TextWriter? writer = null)
    {
        var w = writer ?? Console.Out;
        try
        {
            w.Write(EraseToEndSequence);
            w.Flush();
        }
        catch
        {
            // ignore
        }
    }
}
