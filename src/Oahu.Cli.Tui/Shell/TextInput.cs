using System;
using System.Text;
using Oahu.Cli.Tui.Tokens;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Shell;

/// <summary>
/// Basic single-line text input with cursor. Handles printable characters,
/// Backspace, Delete, Home, End, Left, Right. Renders as a Spectre
/// <see cref="IRenderable"/>.
/// </summary>
public sealed class TextInput
{
    private readonly StringBuilder buffer = new();
    private int cursor;

    public string Label { get; init; } = string.Empty;

    public bool Masked { get; init; }

    public int MaxLength { get; init; } = 256;

    public string Text
    {
        get => buffer.ToString();
        set
        {
            buffer.Clear();
            buffer.Append(value ?? string.Empty);
            cursor = buffer.Length;
        }
    }

    public int Cursor => cursor;

    /// <summary>Process a key press. Returns true if the key was consumed.</summary>
    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                if (cursor > 0)
                {
                    buffer.Remove(cursor - 1, 1);
                    cursor--;
                }
                return true;
            case ConsoleKey.Delete:
                if (cursor < buffer.Length)
                {
                    buffer.Remove(cursor, 1);
                }
                return true;
            case ConsoleKey.LeftArrow:
                if (cursor > 0)
                {
                    cursor--;
                }
                return true;
            case ConsoleKey.RightArrow:
                if (cursor < buffer.Length)
                {
                    cursor++;
                }
                return true;
            case ConsoleKey.Home:
                cursor = 0;
                return true;
            case ConsoleKey.End:
                cursor = buffer.Length;
                return true;
            default:
                if (key.KeyChar >= ' ' && !char.IsControl(key.KeyChar) && buffer.Length < MaxLength)
                {
                    buffer.Insert(cursor, key.KeyChar);
                    cursor++;
                    return true;
                }
                return false;
        }
    }

    /// <summary>Render the input field as an <see cref="IRenderable"/>.</summary>
    public IRenderable Render(bool focused = true)
    {
        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();

        var display = Masked ? new string('•', buffer.Length) : buffer.ToString();

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(Label))
        {
            sb.Append($"[{secondary}]{Markup.Escape(Label)}[/]").Append("  ");
        }

        if (focused && cursor <= display.Length)
        {
            var before = display[..cursor];
            var cursorChar = cursor < display.Length ? display[cursor].ToString() : " ";
            var after = cursor < display.Length ? display[(cursor + 1)..] : string.Empty;
            sb.Append($"[{primary}]{Markup.Escape(before)}[/]");
            sb.Append($"[{brand} underline]{Markup.Escape(cursorChar)}[/]");
            sb.Append($"[{primary}]{Markup.Escape(after)}[/]");
        }
        else
        {
            sb.Append($"[{primary}]{Markup.Escape(display)}[/]");
        }

        return new Markup(sb.ToString());
    }
}
