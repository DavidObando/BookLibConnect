using System;
using System.Collections.Generic;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Auth;

/// <summary>
/// Generic challenge modal for MFA, CVF, CAPTCHA, and approval challenges.
/// </summary>
public sealed class ChallengeModal : IModal<string>
{
    private readonly TextInput input;

    public required string Title { get; init; }

    public required string Instructions { get; init; }

    public string? Detail { get; init; }

    public ChallengeModal()
    {
        input = new TextInput { MaxLength = 64 };
    }

    /// <summary>For approval challenges that only need Enter to confirm.</summary>
    public bool ApprovalOnly { get; init; }

    public bool IsComplete { get; private set; }

    public bool WasCancelled { get; private set; }

    public string? Result { get; private set; }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                WasCancelled = true;
                IsComplete = true;
                return true;
            case ConsoleKey.Enter:
                if (ApprovalOnly)
                {
                    Result = string.Empty;
                    IsComplete = true;
                    return true;
                }
                var text = input.Text.Trim();
                if (text.Length > 0)
                {
                    Result = text;
                    IsComplete = true;
                    return true;
                }
                return true;
        }
        if (!ApprovalOnly)
        {
            return input.HandleKey(key);
        }
        return false;
    }

    public IRenderable Render(int width, int height)
    {
        var lines = new List<IRenderable>();
        lines.Add(new Markup($"[{Tokens.Tokens.TextPrimary.Value.ToMarkup()} bold]{Markup.Escape(Title)}[/]"));
        lines.Add(new Markup(string.Empty));
        lines.Add(new Markup($"[{Tokens.Tokens.TextSecondary.Value.ToMarkup()}]{Markup.Escape(Instructions)}[/]"));

        if (Detail is not null)
        {
            lines.Add(new Markup($"[{Tokens.Tokens.TextTertiary.Value.ToMarkup()}]{Markup.Escape(Detail)}[/]"));
        }

        lines.Add(new Markup(string.Empty));

        if (!ApprovalOnly)
        {
            lines.Add(input.Render());
            lines.Add(new Markup(string.Empty));
        }

        var bar = new HintBar()
            .Add("Enter", ApprovalOnly ? "confirm" : "submit")
            .Add("Esc", "cancel");
        lines.Add(bar.Render());

        return new Padder(new Rows(lines)).Padding(4, 1, 4, 1);
    }
}
