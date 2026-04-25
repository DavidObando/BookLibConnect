using System;
using Spectre.Console;

namespace Oahu.Cli.Tui.Hooks;

/// <summary>
/// Reads the current terminal dimensions, exposing change events so widgets can
/// re-layout. Backed by Spectre's <see cref="IAnsiConsole"/> when one is available;
/// falls back to <see cref="Console"/> on plain stdout.
/// </summary>
public sealed class TerminalSize
{
    private readonly Func<int> readWidth;
    private readonly Func<int> readHeight;
    private int width;
    private int height;

    public TerminalSize(IAnsiConsole? console = null)
    {
        if (console is not null)
        {
            readWidth = () => console.Profile.Width;
            readHeight = () => console.Profile.Height;
        }
        else
        {
            readWidth = SafeConsoleWidth;
            readHeight = SafeConsoleHeight;
        }
        width = readWidth();
        height = readHeight();
    }

    public int Width => width;

    public int Height => height;

    public event Action? Changed;

    /// <summary>Re-read the dimensions; raises <see cref="Changed"/> if either changed.</summary>
    public bool Poll()
    {
        var w = readWidth();
        var h = readHeight();
        if (w == width && h == height)
        {
            return false;
        }
        width = w;
        height = h;
        Changed?.Invoke();
        return true;
    }

    private static int SafeConsoleWidth()
    {
        try
        {
            return Math.Max(40, Console.WindowWidth);
        }
        catch
        {
            return 80;
        }
    }

    private static int SafeConsoleHeight()
    {
        try
        {
            return Math.Max(10, Console.WindowHeight);
        }
        catch
        {
            return 24;
        }
    }
}
