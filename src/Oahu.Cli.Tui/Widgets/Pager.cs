using System;
using System.Collections.Generic;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// In-memory pager for a string buffer split by lines. Tracks viewport offset
/// and exposes scroll commands for use by TUI overlays (logs, help, etc.).
/// Rendering is the caller's job — this widget only manages window math.
/// </summary>
public sealed class Pager
{
    private readonly List<string> lines = new();
    private int offset;
    private int viewportHeight = 10;

    public int ViewportHeight
    {
        get => viewportHeight;
        set => viewportHeight = Math.Max(1, value);
    }

    public int Offset => offset;

    public int LineCount => lines.Count;

    public bool AtTop => offset <= 0;

    public bool AtBottom => offset >= MaxOffset;

    public int MaxOffset => Math.Max(0, lines.Count - viewportHeight);

    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lines.Add(line);
    }

    public void SetContent(IEnumerable<string> newLines)
    {
        ArgumentNullException.ThrowIfNull(newLines);
        lines.Clear();
        lines.AddRange(newLines);
        offset = Math.Min(offset, MaxOffset);
    }

    public void Clear()
    {
        lines.Clear();
        offset = 0;
    }

    public void ScrollUp(int n = 1) => offset = Math.Max(0, offset - Math.Max(1, n));

    public void ScrollDown(int n = 1) => offset = Math.Min(MaxOffset, offset + Math.Max(1, n));

    public void PageUp() => ScrollUp(viewportHeight);

    public void PageDown() => ScrollDown(viewportHeight);

    public void Top() => offset = 0;

    public void Bottom() => offset = MaxOffset;

    /// <summary>Returns the slice of lines currently within the viewport.</summary>
    public IReadOnlyList<string> Visible()
    {
        if (lines.Count == 0)
        {
            return Array.Empty<string>();
        }
        var end = Math.Min(lines.Count, offset + viewportHeight);
        var slice = new List<string>(end - offset);
        for (var i = offset; i < end; i++)
        {
            slice.Add(lines[i]);
        }
        return slice;
    }
}
