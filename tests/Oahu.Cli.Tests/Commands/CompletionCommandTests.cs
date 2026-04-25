using System;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

public class CompletionCommandTests
{
    [Theory]
    [InlineData("bash")]
    [InlineData("zsh")]
    [InlineData("fish")]
    [InlineData("pwsh")]
    public void Render_ProducesNonEmptyScriptForEverySupportedShell(string shell)
    {
        var script = CompletionCommand.Render(shell);
        Assert.False(string.IsNullOrWhiteSpace(script));
        Assert.Contains("oahu-cli", script);
    }

    [Fact]
    public void Render_BashIncludesEveryV1Subcommand()
    {
        var script = CompletionCommand.Render("bash");
        foreach (var sub in CompletionCommand.V1Subcommands)
        {
            Assert.Contains(sub, script);
        }
    }

    [Fact]
    public void Render_UnknownShellThrows()
    {
        Assert.Throws<ArgumentException>(() => CompletionCommand.Render("powershell"));
    }

    [Fact]
    public void SupportedShells_StableSet()
    {
        Assert.Equal(new[] { "bash", "zsh", "fish", "pwsh" }, CompletionCommand.SupportedShells);
    }
}
