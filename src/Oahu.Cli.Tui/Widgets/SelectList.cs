using System;
using System.Collections.Generic;
using System.Text;
using Oahu.Cli.Tui.Icons;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// Render-only listing of a keyboard-navigable list. The interactive prompt
/// lives in Phase 6 (it needs the AppShell input loop). Phase 2 ships the
/// presentation layer so screens can render selection state from any source —
/// queue order, library books, theme picker, etc.
/// </summary>
public sealed class SelectList<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public required Func<T, string> Format { get; init; }

    public int CursorIndex { get; init; } = -1;

    public IReadOnlySet<int>? SelectedIndices { get; init; }

    public bool UseAscii { get; init; }

    public IRenderable Render()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < Items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('\n');
            }

            var prompt = Icons.Icons.Prompt.Render(UseAscii);
            var filled = Icons.Icons.Filled.Render(UseAscii);
            var empty = Icons.Icons.Empty.Render(UseAscii);

            // 4-char fixed-width prefix: cursor + selection + 2 spaces.
            var cursor = i == CursorIndex
                ? $"[{Tokens.Tokens.Brand.Value.ToMarkup()}]{Markup.Escape(prompt)}[/]"
                : " ";
            var selected = SelectedIndices is not null && SelectedIndices.Contains(i)
                ? $"[{Tokens.Tokens.StatusSuccess.Value.ToMarkup()}]{Markup.Escape(filled)}[/]"
                : $"[{Tokens.Tokens.TextTertiary.Value.ToMarkup()}]{Markup.Escape(empty)}[/]";

            sb.Append(cursor).Append(' ').Append(selected).Append("  ");

            var label = Format(Items[i]);
            var colour = i == CursorIndex
                ? Tokens.Tokens.Selected
                : Tokens.Tokens.TextPrimary;
            sb.Append('[').Append(colour.Value.ToMarkup()).Append(']')
              .Append(Markup.Escape(label))
              .Append("[/]");
        }
        return new Markup(sb.ToString());
    }

    public void Write(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.Write(Render());
        console.WriteLine();
    }
}
