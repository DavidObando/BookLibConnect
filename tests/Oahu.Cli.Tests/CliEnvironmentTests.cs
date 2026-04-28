using System;
using Oahu.Cli;
using Xunit;

namespace Oahu.Cli.Tests;

public class CliEnvironmentTests
{
    [Fact]
    public void Initialise_IsIdempotent()
    {
        CliEnvironment.Initialise();
        CliEnvironment.Initialise();
        // No throw, no duplicate exit-trap.
    }

    [Fact]
    public void RegisterRestore_IsCalledByRunRestore()
    {
        var called = 0;
        CliEnvironment.RegisterRestore(() => called++);

        CliEnvironment.RunRestore();
        CliEnvironment.RunRestore();   // second call is a no-op (callback was cleared).

        Assert.Equal(1, called);
    }

    [Fact]
    public void RunRestore_SwallowsExceptionsFromCallback()
    {
        CliEnvironment.RegisterRestore(() => throw new InvalidOperationException("boom"));
        // Must not throw — the exit-trap is the last line of defence.
        CliEnvironment.RunRestore();
    }

    [Fact]
    public void CanEnterTui_FalseWhenOahuNoTuiSet()
    {
        var prev = Environment.GetEnvironmentVariable("OAHU_NO_TUI");
        try
        {
            Environment.SetEnvironmentVariable("OAHU_NO_TUI", "1");
            CliEnvironment.Initialise();
            Assert.False(CliEnvironment.CanEnterTui);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OAHU_NO_TUI", prev);
        }
    }

    [Fact]
    public void Initialise_EnablesVirtualTerminal_WithoutThrowing()
    {
        // EnableWindowsVirtualTerminal is best-effort; on any platform
        // (Windows, Linux, macOS, CI) Initialise must complete without error.
        CliEnvironment.Initialise();
    }
}
