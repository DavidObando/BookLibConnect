using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Doctor;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Queue;
using Oahu.Cli.Server.Tools;
using Xunit;

namespace Oahu.Cli.Tests.Server;

public sealed class OahuToolsTests
{
    private static OahuTools Build(
        IAuthService? auth = null,
        ILibraryService? lib = null,
        IQueueService? queue = null,
        IJobService? jobs = null) =>
        new(
            auth ?? new FakeAuthService(),
            lib ?? new FakeLibraryService(),
            queue ?? new InMemoryQueueService(),
            jobs ?? new JobScheduler(new FakeJobExecutor()),
            new JsonConfigService(Path.Combine(Path.GetTempPath(), $"oahu-tools-cfg-{System.Guid.NewGuid():n}.json")),
            new DoctorService());

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o);

    [Fact]
    public async Task LibraryList_Returns_Items_And_Total()
    {
        var lib = new FakeLibraryService(new[]
        {
            new LibraryItem { Asin = "B1", Title = "Foundation" },
            new LibraryItem { Asin = "B2", Title = "Dune" },
        });
        var t = Build(lib: lib);
        var result = Json(await t.LibraryListAsync(filter: null, limit: null));
        Assert.Equal(2, result.GetProperty("total").GetInt32());
        Assert.Equal(2, result.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task LibraryShow_Throws_KeyNotFound_For_Unknown_Asin()
    {
        var t = Build();
        await Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() => t.LibraryShowAsync("MISSING"));
    }

    [Fact]
    public async Task QueueAdd_Then_QueueList_Roundtrip()
    {
        var queue = new InMemoryQueueService();
        var t = Build(queue: queue);
        var add = Json(await t.QueueAddAsync(new[] { "B1", "B2" }));
        Assert.Equal(2, add.GetProperty("added").GetArrayLength());

        var list = Json(await t.QueueListAsync());
        Assert.Equal(2, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task QueueAdd_Skips_Duplicates()
    {
        var queue = new InMemoryQueueService();
        var t = Build(queue: queue);
        await t.QueueAddAsync(new[] { "B1" });
        var again = Json(await t.QueueAddAsync(new[] { "B1", "B2" }));
        Assert.Equal(1, again.GetProperty("skipped").GetArrayLength());
        Assert.Equal(1, again.GetProperty("added").GetArrayLength());
    }

    [Fact]
    public async Task Download_Returns_JobId_And_Snapshot_Visible()
    {
        await using var sched = new JobScheduler(new FakeJobExecutor(delayPerPhase: System.TimeSpan.FromMilliseconds(50)));
        var t = Build(jobs: sched);
        var accepted = Json(await t.DownloadAsync(new[] { "B1" }));
        Assert.Equal(1, accepted.GetProperty("accepted").GetArrayLength());
        await Task.Delay(20);
        var status = Json(await t.JobsStatusAsync(jobId: null));
        Assert.True(status.GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task ConfigGet_Returns_Defaults_When_Key_Omitted()
    {
        var t = Build();
        var all = Json(await t.ConfigGetAsync(key: null));
        Assert.True(all.TryGetProperty("config", out _));
        Assert.True(all.TryGetProperty("path", out _));
    }

    [Fact]
    public async Task History_Show_Throws_KeyNotFound_For_Unknown_Job()
    {
        var t = Build();
        await Assert.ThrowsAsync<System.Collections.Generic.KeyNotFoundException>(() => t.HistoryShowAsync("nonexistent"));
    }
}
