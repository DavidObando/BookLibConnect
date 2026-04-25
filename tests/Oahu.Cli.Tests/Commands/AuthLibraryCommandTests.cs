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

public class AuthLibraryCommandTests
{
    [Fact]
    public void AuthCommand_ToDictionary_HasStableKeys()
    {
        var session = new AuthSession
        {
            ProfileAlias = "us-1",
            Region = CliRegion.Us,
            AccountId = "A0001",
            AccountName = "Jane Doe",
            DeviceName = "Pixel",
            ExpiresAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var d = AuthCommand.ToDictionary(session);
        Assert.Equal("us-1", d["profileAlias"]);
        Assert.Equal("us", d["region"]);
        Assert.Equal("A0001", d["accountId"]);
        Assert.Equal("Jane Doe", d["accountName"]);
        Assert.Equal("Pixel", d["deviceName"]);
        Assert.False((bool)d["isExpired"]!);
    }

    [Fact]
    public void LibraryCommand_ToDictionary_MapsRuntimeToMinutes()
    {
        var item = new LibraryItem
        {
            Asin = "B01",
            Title = "T",
            Authors = new[] { "A" },
            Narrators = new[] { "N" },
            Series = "S",
            SeriesPosition = 2.0,
            Runtime = TimeSpan.FromMinutes(123.4),
            IsAvailable = true,
        };
        var d = LibraryCommand.ToDictionary(item);
        Assert.Equal("B01", d["asin"]);
        Assert.Equal(123, d["runtimeMinutes"]);
        Assert.Equal("S", d["series"]);
        Assert.Equal(2.0, d["seriesPosition"]);
    }

    [Fact]
    public void LibraryCommand_ToDictionary_NullRuntimeStaysNull()
    {
        var item = new LibraryItem { Asin = "B02", Title = "T2" };
        Assert.Null(LibraryCommand.ToDictionary(item)["runtimeMinutes"]);
        Assert.Null(LibraryCommand.ToDictionary(item)["series"]);
    }

    [Fact]
    public async Task FakeAuthService_RoundTripsLoginAndLogout()
    {
        var svc = new FakeAuthService();
        Assert.Empty(await svc.ListSessionsAsync());
        var s = await svc.LoginAsync(CliRegion.Uk, new NonInteractiveCallbackBroker());
        Assert.Equal(CliRegion.Uk, s.Region);
        Assert.Single(await svc.ListSessionsAsync());
        var active = await svc.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(s.ProfileAlias, active!.ProfileAlias);
        await svc.LogoutAsync(s.ProfileAlias);
        Assert.Empty(await svc.ListSessionsAsync());
        Assert.Null(await svc.GetActiveAsync());
    }

    [Fact]
    public async Task FakeLibraryService_FilterAndGetWork()
    {
        var svc = new FakeLibraryService(new[]
        {
            new LibraryItem { Asin = "A1", Title = "Project Hail Mary", Authors = new[] { "Andy Weir" } },
            new LibraryItem { Asin = "A2", Title = "Dune", Authors = new[] { "Frank Herbert" }, IsAvailable = false },
        });
        var all = await svc.ListAsync(new LibraryFilter { AvailableOnly = false });
        Assert.Equal(2, all.Count);
        var avail = await svc.ListAsync(new LibraryFilter { AvailableOnly = true });
        Assert.Single(avail);
        var search = await svc.ListAsync(new LibraryFilter { Search = "hail", AvailableOnly = false });
        Assert.Single(search);
        Assert.Equal("A1", search[0].Asin);
        var got = await svc.GetAsync("a2");
        Assert.NotNull(got);
        Assert.Equal("Dune", got!.Title);
    }
}
