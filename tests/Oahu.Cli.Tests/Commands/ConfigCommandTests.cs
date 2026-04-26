using System;
using System.IO;
using System.Threading.Tasks;
using Oahu.Cli.App.Config;
using Oahu.Cli.App.Models;
using Oahu.Cli.Commands;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

public class ConfigCommandTests
{
    [Fact]
    public void ToDictionary_ContainsEveryDocumentedKey()
    {
        var dict = ConfigCommand.ToDictionary(OahuConfig.Default);
        foreach (var key in ConfigCommand.Keys)
        {
            Assert.True(dict.ContainsKey(key), $"missing key: {key}");
        }
        Assert.Equal(ConfigCommand.Keys.Count, dict.Count);
    }

    [Fact]
    public void ApplySetting_RoundTripsAllKeys()
    {
        var cfg = OahuConfig.Default;
        cfg = ConfigCommand.ApplySetting(cfg, "download-dir", "/tmp/foo");
        cfg = ConfigCommand.ApplySetting(cfg, "default-quality", "Extreme");
        cfg = ConfigCommand.ApplySetting(cfg, "max-parallel-jobs", "4");
        cfg = ConfigCommand.ApplySetting(cfg, "keep-encrypted-files", "true");
        cfg = ConfigCommand.ApplySetting(cfg, "multi-part-download", "yes");
        cfg = ConfigCommand.ApplySetting(cfg, "export-to-aax", "1");
        cfg = ConfigCommand.ApplySetting(cfg, "export-dir", "/tmp/aax");
        cfg = ConfigCommand.ApplySetting(cfg, "default-profile-alias", "main");
        cfg = ConfigCommand.ApplySetting(cfg, "allow-encrypted-file-credentials", "off");
        cfg = ConfigCommand.ApplySetting(cfg, "theme", "HighContrast");

        Assert.Equal("/tmp/foo", cfg.DownloadDirectory);
        Assert.Equal(DownloadQuality.Extreme, cfg.DefaultQuality);
        Assert.Equal(4, cfg.MaxParallelJobs);
        Assert.True(cfg.KeepEncryptedFiles);
        Assert.True(cfg.MultiPartDownload);
        Assert.True(cfg.ExportToAax);
        Assert.Equal("/tmp/aax", cfg.ExportDirectory);
        Assert.Equal("main", cfg.DefaultProfileAlias);
        Assert.False(cfg.AllowEncryptedFileCredentials);
        Assert.Equal("HighContrast", cfg.Theme);
    }

    [Fact]
    public void ApplySetting_Theme_AcceptsCaseInsensitive_And_NormalizesCasing()
    {
        var cfg = ConfigCommand.ApplySetting(OahuConfig.Default, "theme", "highcontrast");
        Assert.Equal("HighContrast", cfg.Theme);
    }

    [Fact]
    public void ApplySetting_Theme_EmptyClearsOverride()
    {
        var cfg = OahuConfig.Default with { Theme = "Mono" };
        cfg = ConfigCommand.ApplySetting(cfg, "theme", string.Empty);
        Assert.Null(cfg.Theme);
    }

    [Fact]
    public void ApplySetting_InvalidThemeThrows()
    {
        Assert.Throws<ArgumentException>(() => ConfigCommand.ApplySetting(OahuConfig.Default, "theme", "Solarized"));
    }

    [Fact]
    public void ApplySetting_UnknownKeyThrows()
    {
        Assert.Throws<ArgumentException>(() => ConfigCommand.ApplySetting(OahuConfig.Default, "nope", "x"));
    }

    [Fact]
    public void ApplySetting_InvalidQualityThrows()
    {
        Assert.Throws<ArgumentException>(() => ConfigCommand.ApplySetting(OahuConfig.Default, "default-quality", "ultra"));
    }

    [Fact]
    public void ApplySetting_InvalidBoolThrows()
    {
        Assert.Throws<ArgumentException>(() => ConfigCommand.ApplySetting(OahuConfig.Default, "keep-encrypted-files", "maybe"));
    }

    [Fact]
    public void ApplySetting_InvalidIntThrows()
    {
        Assert.Throws<ArgumentException>(() => ConfigCommand.ApplySetting(OahuConfig.Default, "max-parallel-jobs", "0"));
    }

    [Fact]
    public async Task Save_Then_Load_PersistsAllChanges()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oahu-cfg-{Guid.NewGuid():n}.json");
        try
        {
            var svc = new JsonConfigService(path);
            var cfg = ConfigCommand.ApplySetting(OahuConfig.Default, "max-parallel-jobs", "7");
            await svc.SaveAsync(cfg);
            var reloaded = await new JsonConfigService(path).LoadAsync();
            Assert.Equal(7, reloaded.MaxParallelJobs);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
