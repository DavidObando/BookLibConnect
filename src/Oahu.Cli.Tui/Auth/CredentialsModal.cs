using System;
using System.Collections.Generic;
using Oahu.Cli.App.Auth;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Auth;

/// <summary>
/// Email + password modal for in-process Audible sign-in. Mirrors the GUI's
/// "direct login" step (Avalonia <c>ProfileWizardViewModel</c>): the user
/// enters their Amazon/Audible credentials, then any 2FA / CAPTCHA challenge
/// is handled by the broker via <see cref="ChallengeModal"/>.
/// </summary>
public sealed class CredentialsModal : IModal<AuthCredentials>
{
    private readonly TextInput emailInput = new()
    {
        Label = "Email:   ",
        MaxLength = 320,
    };

    private readonly TextInput passwordInput = new()
    {
        Label = "Password:",
        MaxLength = 256,
        Masked = true,
    };

    private readonly string? regionLabel;
    private int focus; // 0 = email, 1 = password
    private string? statusMessage;

    public CredentialsModal(string? regionLabel = null)
    {
        this.regionLabel = regionLabel;
    }

    public bool IsComplete { get; private set; }

    public bool WasCancelled { get; private set; }

    public AuthCredentials? Result { get; private set; }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                WasCancelled = true;
                IsComplete = true;
                return true;

            case ConsoleKey.Tab:
                // With only two fields, Tab and Shift+Tab both toggle.
                focus = (focus + 1) % 2;
                return true;

            case ConsoleKey.UpArrow:
                focus = 0;
                return true;

            case ConsoleKey.DownArrow:
                focus = 1;
                return true;

            case ConsoleKey.Enter:
                var email = emailInput.Text.Trim();
                var password = passwordInput.Text;
                if (string.IsNullOrEmpty(email))
                {
                    statusMessage = "Email is required.";
                    focus = 0;
                    return true;
                }
                if (string.IsNullOrEmpty(password))
                {
                    statusMessage = "Password is required.";
                    focus = 1;
                    return true;
                }
                Result = new AuthCredentials(email, password);
                IsComplete = true;
                return true;

            default:
                // Forward to the focused input.
                return focus == 0
                    ? emailInput.HandleKey(key)
                    : passwordInput.HandleKey(key);
        }
    }

    public IRenderable Render(int width, int height)
    {
        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var error = Tokens.Tokens.StatusError.Value.ToMarkup();

        var heading = string.IsNullOrEmpty(regionLabel)
            ? "Sign in to Audible"
            : $"Sign in to Audible ({regionLabel})";

        var lines = new List<IRenderable>
        {
            new Markup($"[{primary} bold]{Markup.Escape(heading)}[/]"),
            new Markup(string.Empty),
            new Markup($"[{secondary}]Enter your Amazon / Audible account credentials.[/]"),
            new Markup($"[{tertiary}]2FA, CAPTCHA, and verification codes (if required) are prompted next.[/]"),
            new Markup(string.Empty),
            emailInput.Render(focused: focus == 0),
            new Markup(string.Empty),
            passwordInput.Render(focused: focus == 1),
            new Markup(string.Empty),
        };

        if (statusMessage is not null)
        {
            lines.Add(new Markup($"[{error}]{Markup.Escape(statusMessage)}[/]"));
            lines.Add(new Markup(string.Empty));
        }

        var bar = new HintBar()
            .Add("Enter", "submit")
            .Add("Tab/↑↓", "next field")
            .Add("Esc", "cancel");
        lines.Add(bar.Render());

        return new Padder(new Rows(lines)).Padding(4, 1, 4, 1);
    }
}
