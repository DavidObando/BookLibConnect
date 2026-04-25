using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class FakeLibraryServiceTests
{
    private static FakeLibraryService Seed() => new(new[]
    {
        new LibraryItem { Asin = "A1", Title = "Project Hail Mary", Authors = new[] { "Andy Weir" }, Series = "Standalone", IsAvailable = true },
        new LibraryItem { Asin = "A2", Title = "The Way of Kings", Authors = new[] { "Brandon Sanderson" }, Series = "Stormlight", IsAvailable = true },
        new LibraryItem { Asin = "A3", Title = "Words of Radiance", Authors = new[] { "Brandon Sanderson" }, Series = "Stormlight", IsAvailable = false },
    });

    [Fact]
    public async Task List_All_Returns_Available_By_Default()
    {
        var svc = Seed();
        var items = await svc.ListAsync();
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, i => i.Asin == "A3");
    }

    [Fact]
    public async Task List_With_Search_Filters_Title_Case_Insensitive()
    {
        var svc = Seed();
        var items = await svc.ListAsync(new LibraryFilter { Search = "kings" });
        Assert.Single(items);
        Assert.Equal("A2", items[0].Asin);
    }

    [Fact]
    public async Task List_With_Author_Filter()
    {
        var svc = Seed();
        var items = await svc.ListAsync(new LibraryFilter { Author = "sanderson" });
        Assert.Single(items); // A3 is unavailable
    }

    [Fact]
    public async Task List_With_AvailableOnly_False_Includes_Unavailable()
    {
        var svc = Seed();
        var items = await svc.ListAsync(new LibraryFilter { AvailableOnly = false });
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task Get_By_Asin_Case_Insensitive()
    {
        var svc = Seed();
        var item = await svc.GetAsync("a1");
        Assert.NotNull(item);
        Assert.Equal("Project Hail Mary", item!.Title);
    }
}

public class FakeAuthServiceTests
{
    [Fact]
    public async Task Login_Then_GetActive_Returns_Session()
    {
        var svc = new Oahu.Cli.App.Auth.FakeAuthService();
        var s = await svc.LoginAsync(CliRegion.Us, new Oahu.Cli.App.Auth.NonInteractiveCallbackBroker());
        Assert.Equal(CliRegion.Us, s.Region);

        var active = await svc.GetActiveAsync();
        Assert.NotNull(active);
        Assert.Equal(s.ProfileAlias, active!.ProfileAlias);
    }

    [Fact]
    public async Task Logout_Removes_Session()
    {
        var svc = new Oahu.Cli.App.Auth.FakeAuthService();
        var s = await svc.LoginAsync(CliRegion.De, new Oahu.Cli.App.Auth.NonInteractiveCallbackBroker());
        await svc.LogoutAsync(s.ProfileAlias);
        Assert.Empty(await svc.ListSessionsAsync());
        Assert.Null(await svc.GetActiveAsync());
    }

    [Fact]
    public async Task Refresh_Updates_ExpiresAt()
    {
        var svc = new Oahu.Cli.App.Auth.FakeAuthService();
        var s = await svc.LoginAsync(CliRegion.Uk, new Oahu.Cli.App.Auth.NonInteractiveCallbackBroker());
        await Task.Delay(10);
        var refreshed = await svc.RefreshAsync(s.ProfileAlias);
        Assert.True(refreshed.ExpiresAt > s.ExpiresAt);
    }
}
