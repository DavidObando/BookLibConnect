using System;

namespace Oahu.Cli.Tui.Shell;

/// <summary>
/// Progressive <c>Ctrl+C</c> state machine, per design §10 / TUI exploration §14.
///
/// The shell asks <see cref="OnPress"/> what to do for each Ctrl+C press; the
/// state machine returns one of <see cref="CtrlCAction"/> values:
///
///   1. <see cref="CtrlCAction.CancelActiveJob"/> — a job is running, cancel it.
///   2. <see cref="CtrlCAction.CloseDialog"/>     — a modal dialog is open, close it.
///   3. <see cref="CtrlCAction.PromptToExit"/>    — show "Press Ctrl+C again to quit" toast.
///   4. <see cref="CtrlCAction.Exit"/>            — second press within window, leave the TUI.
///
/// The shell is responsible for telling the state machine what's currently
/// happening (active job? open dialog?) before it presses the button —
/// that's what the <see cref="HasActiveJob"/> / <see cref="HasOpenDialog"/>
/// flags are for.
/// </summary>
public sealed class CtrlCState
{
    /// <summary>Window during which a second Ctrl+C escalates to exit.</summary>
    public TimeSpan ExitWindow { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Set by the shell — true when a cancellable job is running.</summary>
    public bool HasActiveJob { get; set; }

    /// <summary>Set by the shell — true when a modal dialog is open.</summary>
    public bool HasOpenDialog { get; set; }

    private readonly Func<DateTimeOffset> clock;
    private DateTimeOffset? promptShownAt;

    public CtrlCState(Func<DateTimeOffset>? clock = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>True while the "press Ctrl+C again to quit" toast is active.</summary>
    public bool ToastActive
    {
        get
        {
            if (promptShownAt is not { } at)
            {
                return false;
            }
            return clock() - at <= ExitWindow;
        }
    }

    /// <summary>Compute the next action for a Ctrl+C press, then advance the state.</summary>
    public CtrlCAction OnPress()
    {
        var now = clock();

        // Second press within the exit window? Leave the shell, regardless of the rest.
        if (promptShownAt is { } at && now - at <= ExitWindow)
        {
            promptShownAt = null;
            return CtrlCAction.Exit;
        }

        if (HasActiveJob)
        {
            return CtrlCAction.CancelActiveJob;
        }

        if (HasOpenDialog)
        {
            return CtrlCAction.CloseDialog;
        }

        promptShownAt = now;
        return CtrlCAction.PromptToExit;
    }

    /// <summary>Cancel the toast / pending exit.</summary>
    public void Reset()
    {
        promptShownAt = null;
    }
}

public enum CtrlCAction
{
    CancelActiveJob,
    CloseDialog,
    PromptToExit,
    Exit,
}
