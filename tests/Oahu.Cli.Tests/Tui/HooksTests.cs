using System;
using Oahu.Cli.Tui.Hooks;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class HooksTests
{
    [Fact]
    public void ScreenReaderProbe_Honours_Force_Env_Var()
    {
        var prev = Environment.GetEnvironmentVariable("OAHU_SCREEN_READER");
        try
        {
            Environment.SetEnvironmentVariable("OAHU_SCREEN_READER", "1");
            Assert.True(ScreenReaderProbe.IsActive());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OAHU_SCREEN_READER", prev);
        }
    }

    [Fact]
    public void ScreenReaderProbe_NoTui_Env_Forces_True()
    {
        var prev = Environment.GetEnvironmentVariable("OAHU_NO_TUI");
        try
        {
            Environment.SetEnvironmentVariable("OAHU_NO_TUI", "1");
            Assert.True(ScreenReaderProbe.IsActive());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OAHU_NO_TUI", prev);
        }
    }

    [Theory]
    [InlineData("SSH_TTY")]
    [InlineData("SSH_CONNECTION")]
    [InlineData("SSH_CLIENT")]
    public void SshDetector_True_When_Any_Ssh_Env_Set(string envVar)
    {
        var prev = Environment.GetEnvironmentVariable(envVar);
        try
        {
            Environment.SetEnvironmentVariable(envVar, "test-value");
            Assert.True(SshDetector.IsSshSession());
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, prev);
        }
    }
}

[CollectionDefinition("EnvVarSerial", DisableParallelization = true)]
public class EnvVarSerialCollection
{
}
