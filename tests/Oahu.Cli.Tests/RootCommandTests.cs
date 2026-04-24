using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oahu.Cli;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests;

public class RootCommandTests
{
    [Fact]
    public void Create_RegistersDoctorAndTuiSubcommands()
    {
        var root = RootCommandFactory.Create(NullFactory);
        var names = new System.Collections.Generic.List<string>();
        foreach (var c in root.Subcommands)
        {
            names.Add(c.Name);
        }
        Assert.Contains("doctor", names);
        Assert.Contains("tui", names);
    }

    [Fact]
    public void Create_ExposesGlobalOptions()
    {
        var root = RootCommandFactory.Create(NullFactory);
        var names = new System.Collections.Generic.List<string>();
        foreach (var o in root.Options)
        {
            names.Add(o.Name);
        }
        Assert.Contains("--quiet", names);
        Assert.Contains("--verbose", names);
        Assert.Contains("--no-color", names);
        Assert.Contains("--config-dir", names);
        Assert.Contains("--log-dir", names);
        Assert.Contains("--log-level", names);
    }

    [Fact]
    public async Task DoctorCommand_RunsAndReturnsZeroOnHealthyEnv()
    {
        // Redirect output through CliEnvironment so the test doesn't pollute stdout.
        using var sw = new StringWriter();
        CliEnvironment.Initialise();
        var prevOut = CliEnvironment.Out;
        var prevErr = CliEnvironment.Error;
        CliEnvironment.Out = sw;
        CliEnvironment.Error = sw;
        try
        {
            var root = RootCommandFactory.Create(NullFactory);
            var rc = await root.Parse(new[] { "doctor", "--skip-network", "--json" }).InvokeAsync();
            Assert.Equal(0, rc);
            Assert.Contains("\"_schemaVersion\":1", sw.ToString());
        }
        finally
        {
            CliEnvironment.Out = prevOut;
            CliEnvironment.Error = prevErr;
        }
    }

    [Fact]
    public void TuiPlaceholder_ReturnsExitCode2WhenNotATty()
    {
        // The test runner does not provide a TTY, so CanEnterTui should be false.
        using var sw = new StringWriter();
        var prevErr = CliEnvironment.Error;
        CliEnvironment.Error = sw;
        try
        {
            CliEnvironment.Initialise();
            var rc = TuiCommand.RunPlaceholder();
            Assert.Equal(2, rc);
            Assert.Contains("TUI mode requires", sw.ToString());
        }
        finally
        {
            CliEnvironment.Error = prevErr;
        }
    }

    private static ILoggerFactory NullFactory() => new NullLoggerFactoryImpl();

    private sealed class NullLoggerFactoryImpl : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName) =>
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose() { }
    }
}
