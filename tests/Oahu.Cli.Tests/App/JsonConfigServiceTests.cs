using System;
using System.IO;
using System.Threading.Tasks;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Models;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class JsonConfigServiceTests : IDisposable
{
    private readonly string tempFile;

    public JsonConfigServiceTests()
    {
        tempFile = Path.Combine(Path.GetTempPath(), $"oahu-cli-config-{Guid.NewGuid():n}.json");
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

    [Fact]
    public async Task Load_Returns_Default_When_File_Missing()
    {
        var svc = new JsonConfigService(tempFile);
        var cfg = await svc.LoadAsync();
        Assert.Equal(OahuConfig.Default, cfg);
    }

    [Fact]
    public async Task Save_Then_Load_Round_Trips_All_Fields()
    {
        var svc = new JsonConfigService(tempFile);
        var cfg = OahuConfig.Default with
        {
            DownloadDirectory = "/tmp/x",
            DefaultQuality = DownloadQuality.Extreme,
            MaxParallelJobs = 4,
            KeepEncryptedFiles = true,
            MultiPartDownload = true,
            ExportToAax = true,
            ExportDirectory = "/tmp/aax",
            DefaultProfileAlias = "main",
            AllowEncryptedFileCredentials = true,
        };
        await svc.SaveAsync(cfg);

        var reloaded = await new JsonConfigService(tempFile).LoadAsync();
        Assert.Equal(cfg, reloaded);
    }

    [Fact]
    public async Task Save_Leaves_No_Tmp_File_Behind()
    {
        var svc = new JsonConfigService(tempFile);
        await svc.SaveAsync(OahuConfig.Default with { MaxParallelJobs = 7 });
        Assert.True(File.Exists(tempFile));
        Assert.False(File.Exists(tempFile + ".tmp"));
    }

    [Fact]
    public async Task Save_Overwrites_Existing_File_Atomically()
    {
        var svc = new JsonConfigService(tempFile);
        await svc.SaveAsync(OahuConfig.Default with { MaxParallelJobs = 1 });
        await svc.SaveAsync(OahuConfig.Default with { MaxParallelJobs = 9 });
        var reloaded = await svc.LoadAsync();
        Assert.Equal(9, reloaded.MaxParallelJobs);
        Assert.False(File.Exists(tempFile + ".tmp"));
    }
}
