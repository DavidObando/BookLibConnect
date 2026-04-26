using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Queue;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class JsonFileQueueServiceTests : IDisposable
{
    private readonly string tempFile;

    public JsonFileQueueServiceTests()
    {
        tempFile = Path.Combine(Path.GetTempPath(), $"oahu-cli-queue-{Guid.NewGuid():n}.json");
    }

    public void Dispose()
    {
        if (File.Exists(tempFile))
        {
            File.Delete(tempFile);
        }
        var tmp = tempFile + ".tmp";
        if (File.Exists(tmp))
        {
            File.Delete(tmp);
        }
    }

    private QueueEntry Sample(string asin) => new() { Asin = asin, Title = $"Book {asin}" };

    [Fact]
    public async Task List_Empty_When_File_Missing()
    {
        var svc = new JsonFileQueueService(tempFile);
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Add_Then_List_Persists_Across_Instances()
    {
        var svc = new JsonFileQueueService(tempFile);
        Assert.True(await svc.AddAsync(Sample("A1")));
        Assert.True(await svc.AddAsync(Sample("A2")));

        var fresh = new JsonFileQueueService(tempFile);
        var list = await fresh.ListAsync();
        Assert.Equal(new[] { "A1", "A2" }, list.Select(e => e.Asin).ToArray());
    }

    [Fact]
    public async Task Add_Returns_False_For_Duplicate_Asin()
    {
        var svc = new JsonFileQueueService(tempFile);
        Assert.True(await svc.AddAsync(Sample("A1")));
        Assert.False(await svc.AddAsync(Sample("a1")));
        Assert.Single(await svc.ListAsync());
    }

    [Fact]
    public async Task Remove_Returns_False_When_Missing()
    {
        var svc = new JsonFileQueueService(tempFile);
        Assert.False(await svc.RemoveAsync("missing"));
    }

    [Fact]
    public async Task Remove_Persists()
    {
        var svc = new JsonFileQueueService(tempFile);
        await svc.AddAsync(Sample("A1"));
        await svc.AddAsync(Sample("A2"));
        Assert.True(await svc.RemoveAsync("A1"));

        var fresh = new JsonFileQueueService(tempFile);
        Assert.Equal(new[] { "A2" }, (await fresh.ListAsync()).Select(e => e.Asin).ToArray());
    }

    [Fact]
    public async Task Clear_Empties_The_Queue()
    {
        var svc = new JsonFileQueueService(tempFile);
        await svc.AddAsync(Sample("A1"));
        await svc.ClearAsync();
        Assert.Empty(await svc.ListAsync());
        Assert.False(File.Exists(tempFile + ".tmp"));
    }

    [Fact]
    public async Task MoveAsync_Swaps_Adjacent_Entries_And_Persists()
    {
        var svc = new JsonFileQueueService(tempFile);
        await svc.AddAsync(Sample("A1"));
        await svc.AddAsync(Sample("A2"));
        await svc.AddAsync(Sample("A3"));

        Assert.True(await svc.MoveAsync("A1", +1));
        Assert.Equal(new[] { "A2", "A1", "A3" }, (await svc.ListAsync()).Select(e => e.Asin).ToArray());

        Assert.True(await svc.MoveAsync("A3", -1));
        Assert.Equal(new[] { "A2", "A3", "A1" }, (await svc.ListAsync()).Select(e => e.Asin).ToArray());

        var fresh = new JsonFileQueueService(tempFile);
        Assert.Equal(new[] { "A2", "A3", "A1" }, (await fresh.ListAsync()).Select(e => e.Asin).ToArray());
    }

    [Fact]
    public async Task MoveAsync_Returns_False_At_Boundaries_Or_Unknown()
    {
        var svc = new JsonFileQueueService(tempFile);
        await svc.AddAsync(Sample("A1"));
        await svc.AddAsync(Sample("A2"));

        Assert.False(await svc.MoveAsync("A1", -1));
        Assert.False(await svc.MoveAsync("A2", +1));
        Assert.False(await svc.MoveAsync("missing", +1));
        Assert.Equal(new[] { "A1", "A2" }, (await svc.ListAsync()).Select(e => e.Asin).ToArray());
    }

    [Fact]
    public async Task MoveAsync_Preserves_AddedAt_Of_Other_Entries()
    {
        var svc = new JsonFileQueueService(tempFile);
        var when = DateTimeOffset.UtcNow.AddDays(-1);
        await svc.AddAsync(new QueueEntry { Asin = "A1", Title = "First", AddedAt = when });
        await svc.AddAsync(new QueueEntry { Asin = "A2", Title = "Second", AddedAt = when.AddMinutes(10) });

        Assert.True(await svc.MoveAsync("A1", +1));

        var list = await svc.ListAsync();
        Assert.Equal("A2", list[0].Asin);
        Assert.Equal(when.AddMinutes(10), list[0].AddedAt);
        Assert.Equal("A1", list[1].Asin);
        Assert.Equal(when, list[1].AddedAt);
    }
}
