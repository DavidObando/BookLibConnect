using System;
using System.Collections.Generic;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Widgets;

/// <summary>
/// The pinned footer that shows contextual key bindings. Built from a
/// <c>(key, action)</c> dictionary so screens can mutate hints without
/// touching markup. Empty / null values are filtered out — that lets a
/// caller pass an exhaustive set and conditionally clear entries.
/// </summary>
public sealed class HintBar
{
    private readonly List<(string Key, string Action)> hints = new();

    public bool UseAscii { get; init; }

    public string Separator { get; init; } = "·";

    public HintBar Add(string key, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return this;
        }
        hints.Add((key, action!));
        return this;
    }

    public HintBar AddRange(IEnumerable<KeyValuePair<string, string?>> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var kv in source)
        {
            Add(kv.Key, kv.Value);
        }
        return this;
    }

    public IRenderable Render()
    {
        if (hints.Count == 0)
        {
            return new Markup(string.Empty);
        }

        var sep = UseAscii ? "|" : Separator;
        var sb = new StringBuilder();
        for (var i = 0; i < hints.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ')
                  .Append('[').Append(Tokens.Tokens.TextTertiary.Value.ToMarkup()).Append(']')
                  .Append(Markup.Escape(sep))
                  .Append("[/] ");
            }
            sb.Append('[').Append(Tokens.Tokens.Brand.Value.ToMarkup()).Append(']')
              .Append(Markup.Escape(hints[i].Key))
              .Append("[/] ")
              .Append('[').Append(Tokens.Tokens.TextSecondary.Value.ToMarkup()).Append(']')
              .Append(Markup.Escape(hints[i].Action))
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
