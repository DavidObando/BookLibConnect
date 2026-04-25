using System;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Core;

namespace Oahu.Cli.App.Auth;

/// <summary>
/// Adapts the CLI's async <see cref="IAuthCallbackBroker"/> to Core's synchronous
/// <see cref="Callbacks"/> delegates. Core invokes the resulting delegates on
/// background threads (typically inside <see cref="Task.Run(Func{Task})"/>
/// — see <c>AudibleClient.ConfigFromProgrammaticLoginAsync</c>) so blocking via
/// <c>GetAwaiter().GetResult()</c> does not deadlock the calling thread.
///
/// The bridge intentionally swallows nothing: if the broker raises
/// <see cref="NonInteractiveCallbackException"/> the underlying Core operation
/// receives the same exception and surfaces it to the CLI.
/// </summary>
internal static class CallbackBridge
{
    public static Callbacks ToCoreCallbacks(IAuthCallbackBroker broker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broker);

        return new Callbacks
        {
            CaptchaCallback = imageBytes =>
                broker.SolveCaptchaAsync(new CaptchaChallenge(imageBytes), cancellationToken)
                    .GetAwaiter().GetResult(),

            MfaCallback = () =>
                broker.SolveMfaAsync(new MfaChallenge(), cancellationToken)
                    .GetAwaiter().GetResult(),

            CvfCallback = () =>
                broker.SolveCvfAsync(new CvfChallenge(), cancellationToken)
                    .GetAwaiter().GetResult(),

            ApprovalCallback = () =>
                broker.ConfirmApprovalAsync(new ApprovalChallenge(), cancellationToken)
                    .GetAwaiter().GetResult(),

            ExternalLoginCallback = uri =>
                broker.CompleteExternalLoginAsync(new ExternalLoginChallenge(uri), cancellationToken)
                    .GetAwaiter().GetResult(),

            // No interactive de-registration confirmation in the CLI:
            // ConfigParseExternalLoginResponseAsync currently always
            // de-registers the previous device when one exists; mirror that
            // behaviour and let the caller surface the soft "previous device
            // de-registered" notice from the EAuthorizeResult.
            DeregisterDeviceConfirmCallback = _ => true,
        };
    }
}
