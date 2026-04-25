using System;
using System.Collections.Generic;
using System.Diagnostics;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Auth;

/// <summary>
/// External-login modal: shows the Audible login URL and waits for the
/// user to paste the redirect URL. Per design TUI-exploration §10.2.b.
/// </summary>
public sealed class ExternalLoginModal : IModal<Uri>
{
    private readonly Uri loginUri;
    private readonly TextInput redirectInput = new()
    {
        Label = "Paste redirect URL:",
        MaxLength = 2048,
    };

    private string? statusMessage;

    public ExternalLoginModal(Uri loginUri)
    {
        this.loginUri = loginUri ?? throw new ArgumentNullException(nameof(loginUri));
    }

    public bool IsComplete { get; private set; }

    public bool WasCancelled { get; private set; }

    public Uri? Result { get; private set; }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                WasCancelled = true;
                IsComplete = true;
                return true;
            case ConsoleKey.Enter:
                var text = redirectInput.Text.Trim();
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
                {
                    Result = uri;
                    IsComplete = true;
                    return true;
                }
                statusMessage = string.IsNullOrEmpty(text)
                    ? "Please paste the redirect URL first."
                    : "Not a valid URL. Try again.";
                return true;
            case ConsoleKey.B when (key.Modifiers & ConsoleModifiers.Control) == 0:
                // 'b' opens browser
                if (redirectInput.Text.Length == 0)
                {
                    TryOpenBrowser(loginUri);
                    statusMessage = "✓ Opened in browser";
                    return true;
                }
                return redirectInput.HandleKey(key);
            case ConsoleKey.Y when (key.Modifiers & ConsoleModifiers.Control) == 0:
                if (redirectInput.Text.Length == 0)
                {
                    TryCopyToClipboard(loginUri.ToString());
                    statusMessage = "✓ URL copied to clipboard";
                    return true;
                }
                return redirectInput.HandleKey(key);
        }
        return redirectInput.HandleKey(key);
    }

    public IRenderable Render(int width, int height)
    {
        var lines = new List<IRenderable>();
        lines.Add(new Markup($"[{Tokens.Tokens.TextPrimary.Value.ToMarkup()} bold]Sign in via browser[/]"));
        lines.Add(new Markup(string.Empty));
        lines.Add(new Markup($"[{Tokens.Tokens.TextSecondary.Value.ToMarkup()}]1. Open this URL in your browser:[/]"));
        lines.Add(new Markup(string.Empty));

        // Truncate long URLs for display
        var urlStr = loginUri.ToString();
        var displayUrl = urlStr.Length > 80 ? urlStr[..77] + "…" : urlStr;
        lines.Add(new Markup($"   [{Tokens.Tokens.StatusInfo.Value.ToMarkup()}]{Markup.Escape(displayUrl)}[/]"));
        lines.Add(new Markup(string.Empty));
        lines.Add(new Markup($"   [{Tokens.Tokens.TextTertiary.Value.ToMarkup()}]b open browser · y copy URL[/]"));
        lines.Add(new Markup(string.Empty));

        if (statusMessage is not null)
        {
            lines.Add(new Markup($"   [{Tokens.Tokens.StatusSuccess.Value.ToMarkup()}]{Markup.Escape(statusMessage)}[/]"));
            lines.Add(new Markup(string.Empty));
        }

        lines.Add(new Markup($"[{Tokens.Tokens.TextSecondary.Value.ToMarkup()}]2. After signing in, paste the final URL your browser ended up on:[/]"));
        lines.Add(new Markup(string.Empty));
        lines.Add(redirectInput.Render());
        lines.Add(new Markup(string.Empty));

        var bar = new HintBar()
            .Add("Enter", "submit")
            .Add("b", "open browser")
            .Add("y", "copy URL")
            .Add("Esc", "cancel");
        lines.Add(bar.Render());

        return new Padder(new Rows(lines)).Padding(4, 1, 4, 1);
    }

    private static void TryOpenBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch
        {
            // Ignore — user can copy manually.
        }
    }

    private static void TryCopyToClipboard(string text)
    {
        try
        {
            // OSC 52 clipboard sequence (works in iTerm2, Windows Terminal, Kitty, modern xterm).
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
            Console.Write($"\u001b]52;c;{b64}\u001b\\");
        }
        catch
        {
            // ignore
        }
    }
}
