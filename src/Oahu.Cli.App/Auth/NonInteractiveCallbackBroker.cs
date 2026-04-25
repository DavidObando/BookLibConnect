using System.Threading;
using System.Threading.Tasks;

namespace Oahu.Cli.App.Auth;

/// <summary>
/// Non-interactive broker that fails fast on every challenge. Used as the default
/// when the runtime detects <c>--no-prompt</c>, a non-TTY stdin, or the MCP
/// "unattended" mode (Phase 5). Phase 6 swaps in a Spectre-dialog broker for the
/// TUI; Phase 4 wires <see cref="StdinCallbackBroker"/> into command mode.
/// </summary>
public sealed class NonInteractiveCallbackBroker : IAuthCallbackBroker
{
    public Task<string> SolveCaptchaAsync(CaptchaChallenge c, CancellationToken ct) => throw new NonInteractiveCallbackException("captcha");

    public Task<string> SolveMfaAsync(MfaChallenge c, CancellationToken ct) => throw new NonInteractiveCallbackException("mfa");

    public Task<string> SolveCvfAsync(CvfChallenge c, CancellationToken ct) => throw new NonInteractiveCallbackException("cvf");

    public Task ConfirmApprovalAsync(ApprovalChallenge c, CancellationToken ct) => throw new NonInteractiveCallbackException("approval");

    public Task<System.Uri> CompleteExternalLoginAsync(ExternalLoginChallenge c, CancellationToken ct) => throw new NonInteractiveCallbackException("external-login");
}
