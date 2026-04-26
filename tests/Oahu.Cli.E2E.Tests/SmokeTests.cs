using System.Threading.Tasks;
using Xunit;

namespace Oahu.Cli.E2E.Tests;

public class SmokeTests
{
    [Fact]
    public async Task Version_PrintsVersion()
    {
        var cli = new CliRunner();
        var r = await cli.RunRawAsync("--version");
        Assert.Equal(0, r.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(r.StdOut));
    }

    [Fact]
    public async Task Help_PrintsUsage()
    {
        var cli = new CliRunner();
        var r = await cli.RunRawAsync("--help");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("Usage", r.AllOutput);
    }

    [Fact]
    public async Task Doctor_RunsAndExits()
    {
        var cli = new CliRunner();
        var r = await cli.RunAsync("doctor");
        // Doctor may surface warnings (exit 0) or environment problems (non-zero).
        // We only assert that it runs to completion and produces output.
        Assert.False(string.IsNullOrWhiteSpace(r.AllOutput));
    }

    [Fact]
    public async Task Config_Get_Json_Returns_Valid_Output()
    {
        var cli = new CliRunner();
        var r = await cli.RunAsync("config", "get", "--json");
        Assert.Equal(0, r.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(r.StdOut));
    }

    [Fact]
    public async Task Unknown_Command_Returns_NonZero()
    {
        var cli = new CliRunner();
        var r = await cli.RunAsync("definitely-not-a-real-command");
        Assert.NotEqual(0, r.ExitCode);
    }
}
