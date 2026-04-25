using System;
using System.Collections.Generic;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Auth;

/// <summary>
/// Region picker modal for sign-in step 1.
/// </summary>
public sealed class RegionPickerModal : IModal<string>
{
    private static readonly (string Code, string Name)[] Regions = new[]
    {
        ("us", "United States"),
        ("uk", "United Kingdom"),
        ("de", "Germany"),
        ("fr", "France"),
        ("jp", "Japan"),
        ("ca", "Canada"),
        ("au", "Australia"),
        ("it", "Italy"),
        ("es", "Spain"),
        ("in", "India"),
        ("br", "Brazil"),
    };

    private int cursor;

    public bool IsComplete { get; private set; }

    public bool WasCancelled { get; private set; }

    public string? Result { get; private set; }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                cursor = Math.Max(0, cursor - 1);
                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                cursor = Math.Min(Regions.Length - 1, cursor + 1);
                return true;
            case ConsoleKey.Enter:
                Result = Regions[cursor].Code;
                IsComplete = true;
                return true;
            case ConsoleKey.Escape:
                WasCancelled = true;
                IsComplete = true;
                return true;
        }
        return false;
    }

    public IRenderable Render(int width, int height)
    {
        var lines = new List<IRenderable>();
        lines.Add(new Markup($"[{Tokens.Tokens.TextPrimary.Value.ToMarkup()} bold]Sign in to Audible[/]"));
        lines.Add(new Markup(string.Empty));
        lines.Add(new Markup($"[{Tokens.Tokens.TextSecondary.Value.ToMarkup()}]Choose your Audible region:[/]"));
        lines.Add(new Markup(string.Empty));

        for (var i = 0; i < Regions.Length; i++)
        {
            var (code, name) = Regions[i];
            var prefix = i == cursor
                ? $"[{Tokens.Tokens.Brand.Value.ToMarkup()}]  ❯ [/]"
                : "    ";
            var style = i == cursor ? Tokens.Tokens.TextPrimary.Value.ToMarkup() : Tokens.Tokens.TextSecondary.Value.ToMarkup();
            lines.Add(new Markup($"{prefix}[{style}]{Markup.Escape(name)}[/]  [{Tokens.Tokens.TextTertiary.Value.ToMarkup()}]({Markup.Escape(code)})[/]"));
        }

        lines.Add(new Markup(string.Empty));
        var bar = new HintBar()
            .Add("↑↓", "select")
            .Add("Enter", "continue")
            .Add("Esc", "cancel");
        lines.Add(bar.Render());

        return new Padder(new Rows(lines)).Padding(4, 1, 4, 1);
    }
}
