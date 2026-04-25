using System;
using Oahu.Cli.Tui.Shell;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

public class CtrlCStateTests
{
    [Fact]
    public void First_Press_With_No_Active_Job_Shows_Prompt()
    {
        var state = new CtrlCState();
        Assert.Equal(CtrlCAction.PromptToExit, state.OnPress());
        Assert.True(state.ToastActive);
    }

    [Fact]
    public void Active_Job_Cancels_First()
    {
        var state = new CtrlCState { HasActiveJob = true };
        Assert.Equal(CtrlCAction.CancelActiveJob, state.OnPress());
        Assert.False(state.ToastActive);
    }

    [Fact]
    public void Open_Dialog_Closes_When_No_Job()
    {
        var state = new CtrlCState { HasOpenDialog = true };
        Assert.Equal(CtrlCAction.CloseDialog, state.OnPress());
    }

    [Fact]
    public void Second_Press_Within_Window_Exits()
    {
        var t = DateTimeOffset.UtcNow;
        var state = new CtrlCState(clock: () => t);
        Assert.Equal(CtrlCAction.PromptToExit, state.OnPress());
        // Advance 1 second — well within the default 2s window.
        t = t.AddSeconds(1);
        Assert.Equal(CtrlCAction.Exit, state.OnPress());
    }

    [Fact]
    public void Second_Press_After_Window_Reprompts()
    {
        var t = DateTimeOffset.UtcNow;
        var state = new CtrlCState(clock: () => t);
        Assert.Equal(CtrlCAction.PromptToExit, state.OnPress());
        t = t.AddSeconds(5);
        Assert.Equal(CtrlCAction.PromptToExit, state.OnPress());
    }

    [Fact]
    public void Reset_Clears_Toast()
    {
        var state = new CtrlCState();
        state.OnPress();
        Assert.True(state.ToastActive);
        state.Reset();
        Assert.False(state.ToastActive);
    }
}
