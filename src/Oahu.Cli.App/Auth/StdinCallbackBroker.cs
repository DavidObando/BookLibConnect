using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Auth;

/// <summary>
/// Stdin-driven implementation of <see cref="IAuthCallbackBroker"/>. Prompts on
/// <see cref="TextWriter"/> (defaults to <see cref="Console.Error"/>) and reads from
/// <see cref="TextReader"/> (defaults to <see cref="Console.In"/>). In
/// non-interactive mode (stdin not a TTY, or <c>requireInteractive</c> wired by the
/// caller) every method throws <see cref="NonInteractiveCallbackException"/> so
/// the CLI can surface a clear "missing input X" error rather than blocking forever.
/// </summary>
public sealed class StdinCallbackBroker : IAuthCallbackBroker
{
    private readonly TextReader reader;
    private readonly TextWriter writer;
    private readonly bool isInteractive;

    public StdinCallbackBroker(TextReader? reader = null, TextWriter? writer = null, bool? interactive = null)
    {
        this.reader = reader ?? Console.In;
        this.writer = writer ?? Console.Error;
        this.isInteractive = interactive ?? !Console.IsInputRedirected;
    }

    public Task<string> SolveCaptchaAsync(CaptchaChallenge challenge, CancellationToken cancellationToken)
    {
        EnsureInteractive("captcha");
        writer.WriteLine("Audible CAPTCHA required.");
        writer.WriteLine($"  ({challenge.ImageBytes.Length} bytes — open your browser to view, or use a TUI client.)");
        writer.Write("Enter CAPTCHA text: ");
        return ReadLineAsync("captcha", cancellationToken);
    }

    public Task<string> SolveMfaAsync(MfaChallenge challenge, CancellationToken cancellationToken)
    {
        EnsureInteractive("mfa");
        writer.Write("Enter MFA code: ");
        return ReadLineAsync("mfa", cancellationToken);
    }

    public Task<string> SolveCvfAsync(CvfChallenge challenge, CancellationToken cancellationToken)
    {
        EnsureInteractive("cvf");
        writer.Write("Enter CVF (account verification) code: ");
        return ReadLineAsync("cvf", cancellationToken);
    }

    public Task ConfirmApprovalAsync(ApprovalChallenge challenge, CancellationToken cancellationToken)
    {
        EnsureInteractive("approval");
        writer.WriteLine("Audible needs you to approve this sign-in via a notification on a trusted device.");
        writer.Write("Press Enter once approved... ");
        return ReadLineAsync("approval", cancellationToken);
    }

    public async Task<Uri> CompleteExternalLoginAsync(ExternalLoginChallenge challenge, CancellationToken cancellationToken)
    {
        EnsureInteractive("external-login");
        writer.WriteLine("Open this URL in a browser to sign in:");
        writer.WriteLine($"  {challenge.LoginUri}");
        writer.Write("Paste the final redirect URL: ");
        var line = await ReadLineAsync("external-login", cancellationToken).ConfigureAwait(false);
        if (!Uri.TryCreate(line, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The pasted text is not a valid absolute URL.");
        }
        return uri;
    }

    private void EnsureInteractive(string kind)
    {
        if (!isInteractive)
        {
            throw new NonInteractiveCallbackException(kind);
        }
    }

    private async Task<string> ReadLineAsync(string kind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // TextReader.ReadLineAsync(CancellationToken) was added in net7+; works with both Console.In and StringReader.
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            throw new NonInteractiveCallbackException(kind);
        }
        return line.Trim();
    }
}

/// <summary>Thrown by callback brokers when the runtime cannot prompt the user (non-TTY, --no-prompt, MCP-unattended).</summary>
public sealed class NonInteractiveCallbackException : Exception
{
    public NonInteractiveCallbackException(string kind)
        : base($"Audible requires {kind} input but no interactive prompt is available. Re-run with an interactive terminal, or use the TUI / GUI to complete sign-in.")
    {
        Kind = kind;
    }

    public string Kind { get; }
}
