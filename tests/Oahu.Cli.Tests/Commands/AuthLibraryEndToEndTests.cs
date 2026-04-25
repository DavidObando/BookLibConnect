using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

/// <summary>
/// End-to-end command-mode integration: drives <see cref="RootCommandFactory"/>
/// against a seeded fake service container and asserts both exit code and the
/// JSON document shape the command emits to stdout.
/// </summary>
[Collection("EnvVarSerial")]
public class AuthLibraryEndToEndTests : IDisposable
{
    public AuthLibraryEndToEndTests()
    {
        CliServiceFactory.Reset();
    }

    public void Dispose()
    {
        CliServiceFactory.AuthServiceFactory = static () => new FakeAuthService();
        CliServiceFactory.LibraryServiceFactory = static () => new FakeLibraryService();
        CliServiceFactory.Reset();
    }

    [Fact]
    public async Task AuthStatus_NoProfiles_ExitsThree()
    {
        var (exit, _, _) = await RunAsync("auth", "status", "--json");
        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task AuthStatus_ListsSessionsAsJson()
    {
        var auth = new FakeAuthService();
        await auth.LoginAsync(CliRegion.De, new NonInteractiveCallbackBroker());
        CliServiceFactory.AuthServiceFactory = () => auth;

        var (exit, stdout, _) = await RunAsync("auth", "status", "--json");
        Assert.Equal(0, exit);
        Assert.Contains("\"region\": \"de\"", stdout);
        Assert.Contains("\"isActive\": true", stdout);
        Assert.Contains("\"resource\": \"auth-status\"", stdout);
    }

    [Fact]
    public async Task LibraryList_FiltersBySearch()
    {
        var lib = new FakeLibraryService(new[]
        {
            new LibraryItem { Asin = "A1", Title = "Project Hail Mary" },
            new LibraryItem { Asin = "A2", Title = "Dune" },
        });
        CliServiceFactory.LibraryServiceFactory = () => lib;

        var (exit, stdout, _) = await RunAsync("library", "list", "--filter", "Hail", "--json");
        Assert.Equal(0, exit);
        Assert.Contains("\"count\": 1", stdout);
        Assert.Contains("\"asin\": \"A1\"", stdout);
        Assert.DoesNotContain("Dune", stdout);
    }

    [Fact]
    public async Task LibraryShow_MissingAsin_ExitsOne()
    {
        var (exit, _, _) = await RunAsync("library", "show", "B0FAKE");
        Assert.Equal(1, exit);
    }

    [Fact]
    public async Task LibraryShow_FoundAsin_EmitsJson()
    {
        var lib = new FakeLibraryService(new[]
        {
            new LibraryItem { Asin = "A1", Title = "Test", Authors = new[] { "X" } },
        });
        CliServiceFactory.LibraryServiceFactory = () => lib;

        var (exit, stdout, _) = await RunAsync("library", "show", "A1", "--json");
        Assert.Equal(0, exit);
        Assert.Contains("\"resource\": \"library-show\"", stdout);
        Assert.Contains("\"asin\": \"A1\"", stdout);
    }

    [Fact]
    public async Task LibraryUnread_NotImplemented_Errors()
    {
        var (exit, _, stderr) = await RunAsync("library", "list", "--unread");
        Assert.Equal(1, exit);
        Assert.Contains("--unread is not implemented", stderr);
    }

    [Fact]
    public async Task AuthLogin_PositionalRegionParses()
    {
        var auth = new FakeAuthService();
        CliServiceFactory.AuthServiceFactory = () => auth;
        var (exit, _, _) = await RunAsync("auth", "login", "uk", "--json");
        Assert.Equal(0, exit);
        Assert.Single(await auth.ListSessionsAsync());
    }

    private static async Task<(int exit, string stdout, string stderr)> RunAsync(params string[] args)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var origCliOut = CliEnvironment.Out;
        var origCliErr = CliEnvironment.Error;
        var sw = new System.IO.StringWriter();
        var ew = new System.IO.StringWriter();
        Console.SetOut(sw);
        Console.SetError(ew);
        CliEnvironment.Out = sw;
        CliEnvironment.Error = ew;
        try
        {
            var root = RootCommandFactory.Create(() => Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            var parse = root.Parse(args);
            var exit = await parse.InvokeAsync();
            return (exit, sw.ToString(), ew.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
            CliEnvironment.Out = origCliOut;
            CliEnvironment.Error = origCliErr;
        }
    }
}
