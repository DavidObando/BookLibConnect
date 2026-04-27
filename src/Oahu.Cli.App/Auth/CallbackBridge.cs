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
public static class CallbackBridge
{
    public static Callbacks ToCoreCallbacks(IAuthCallbackBroker broker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broker);

        return new Callbacks
        {
            CaptchaCallback = imageBytes =>
                broker.SolveCaptchaAsync(new CaptchaChallenge(imageBytes), cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter().GetResult(),

            MfaCallback = () =>
                broker.SolveMfaAsync(new MfaChallenge(), cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter().GetResult(),

            CvfCallback = () =>
                broker.SolveCvfAsync(new CvfChallenge(), cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter().GetResult(),

            ApprovalCallback = () =>
                broker.ConfirmApprovalAsync(new ApprovalChallenge(), cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter().GetResult(),

            ExternalLoginCallback = uri =>
                broker.CompleteExternalLoginAsync(new ExternalLoginChallenge(uri), cancellationToken)
                    .ConfigureAwait(false)
                    .GetAwaiter().GetResult(),

            // No interactive de-registration confirmation in the CLI:
            // ConfigParseExternalLoginResponseAsync currently always
            // de-registers the previous device when one exists; mirror that
            // behaviour and let the caller surface the soft "previous device
            // de-registered" notice from the EAuthorizeResult.
            DeregisterDeviceConfirmCallback = _ => true,

            // Default the local alias to the customer name when none is set,
            // matching the GUI's ProfileWizard behaviour
            // (`AccountAlias = key?.AccountName`). Without this, BookLibrary
            // never persists an alias for the new profile and subsequent
            // `GetAccountAliases()` lookups return empty, breaking
            // alias-based profile resolution in `CoreLibraryService.SyncAsync`.
            GetAccountAliasFunc = ctxt =>
            {
                if (string.IsNullOrWhiteSpace(ctxt.Alias))
                {
                    ctxt.Alias = ctxt.CustomerName;
                }
                return true;
            },
        };
    }
}
