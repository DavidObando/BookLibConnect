using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;

namespace Oahu.Cli.Tui.Auth;

/// <summary>
/// Request from the auth background thread to the TUI main thread.
/// The TUI shell creates the appropriate modal, and the completion source
/// is set when the user responds.
/// </summary>
public sealed class ModalRequest
{
    public required CallbackChallenge Challenge { get; init; }

    public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// TUI-side implementation of <see cref="IAuthCallbackBroker"/>. Each
/// challenge method posts a <see cref="ModalRequest"/> to a concurrent queue
/// and awaits the result. The AppShell input loop polls
/// <see cref="TryDequeue"/> and creates a matching modal; when the user
/// submits the modal result, it sets the completion source.
/// </summary>
public sealed class TuiCallbackBroker : IAuthCallbackBroker
{
    private readonly ConcurrentQueue<ModalRequest> requests = new();

    /// <summary>Try to dequeue the next pending modal request.</summary>
    public bool TryDequeue(out ModalRequest? request)
        => requests.TryDequeue(out request);

    /// <summary>Whether there are pending requests.</summary>
    public bool HasPending => !requests.IsEmpty;

    public async Task<string> SolveCaptchaAsync(CaptchaChallenge challenge, CancellationToken cancellationToken)
    {
        var req = new ModalRequest { Challenge = challenge };
        requests.Enqueue(req);
        using var reg = cancellationToken.Register(() => req.Completion.TrySetCanceled(cancellationToken));
        return await req.Completion.Task.ConfigureAwait(false);
    }

    public async Task<string> SolveMfaAsync(MfaChallenge challenge, CancellationToken cancellationToken)
    {
        var req = new ModalRequest { Challenge = challenge };
        requests.Enqueue(req);
        using var reg = cancellationToken.Register(() => req.Completion.TrySetCanceled(cancellationToken));
        return await req.Completion.Task.ConfigureAwait(false);
    }

    public async Task<string> SolveCvfAsync(CvfChallenge challenge, CancellationToken cancellationToken)
    {
        var req = new ModalRequest { Challenge = challenge };
        requests.Enqueue(req);
        using var reg = cancellationToken.Register(() => req.Completion.TrySetCanceled(cancellationToken));
        return await req.Completion.Task.ConfigureAwait(false);
    }

    public async Task ConfirmApprovalAsync(ApprovalChallenge challenge, CancellationToken cancellationToken)
    {
        var req = new ModalRequest { Challenge = challenge };
        requests.Enqueue(req);
        using var reg = cancellationToken.Register(() => req.Completion.TrySetCanceled(cancellationToken));
        await req.Completion.Task.ConfigureAwait(false);
    }

    public async Task<Uri> CompleteExternalLoginAsync(ExternalLoginChallenge challenge, CancellationToken cancellationToken)
    {
        var req = new ModalRequest { Challenge = challenge };
        requests.Enqueue(req);
        using var reg = cancellationToken.Register(() => req.Completion.TrySetCanceled(cancellationToken));
        var result = await req.Completion.Task.ConfigureAwait(false);
        if (!Uri.TryCreate(result, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The pasted text is not a valid absolute URL.");
        }
        return uri;
    }
}
