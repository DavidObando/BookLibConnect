using System;
using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Auth;

/// <summary>
/// Typed representation of a single Audible-side challenge that needs human input.
/// Mirrors <c>Oahu.Core.Callbacks</c> but in CLI-owned types so the broker can be
/// driven from stdin (command mode) or Spectre dialogs (TUI / Phase 6) without
/// either of those layers depending on Oahu.Core.
/// </summary>
public abstract record CallbackChallenge
{
    public abstract string Kind { get; }
}

public sealed record CaptchaChallenge(byte[] ImageBytes) : CallbackChallenge
{
    public override string Kind => "captcha";
}

public sealed record MfaChallenge() : CallbackChallenge
{
    public override string Kind => "mfa";
}

public sealed record CvfChallenge() : CallbackChallenge
{
    public override string Kind => "cvf";
}

public sealed record ApprovalChallenge() : CallbackChallenge
{
    public override string Kind => "approval";
}

public sealed record ExternalLoginChallenge(Uri LoginUri) : CallbackChallenge
{
    public override string Kind => "external-login";
}

/// <summary>
/// Human-in-the-loop bridge for Audible login challenges. Implementations decide
/// where to surface the prompt (stdin, dialog, MCP request, …) and return the
/// user's answer.
/// </summary>
public interface IAuthCallbackBroker
{
    Task<string> SolveCaptchaAsync(CaptchaChallenge challenge, CancellationToken cancellationToken);

    Task<string> SolveMfaAsync(MfaChallenge challenge, CancellationToken cancellationToken);

    Task<string> SolveCvfAsync(CvfChallenge challenge, CancellationToken cancellationToken);

    Task ConfirmApprovalAsync(ApprovalChallenge challenge, CancellationToken cancellationToken);

    /// <summary>Show <paramref name="challenge"/> and return the URI the user pasted back from the browser.</summary>
    Task<Uri> CompleteExternalLoginAsync(ExternalLoginChallenge challenge, CancellationToken cancellationToken);
}
