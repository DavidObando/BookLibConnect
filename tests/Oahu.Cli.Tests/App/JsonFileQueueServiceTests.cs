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
}
