using System;
using System.IO;
using Oahu.Cli.App.Paths;
using Xunit;

namespace Oahu.Cli.Tests;

public class CliPathsTests
{
    [Fact]
    public void EnsureDirectories_IsIdempotent()
    {
        CliPaths.EnsureDirectories();
        CliPaths.EnsureDirectories();
        Assert.True(Directory.Exists(CliPaths.ConfigDir));
        Assert.True(Directory.Exists(CliPaths.LogDir));
    }

    [Fact]
    public void TodayLogFile_HasExpectedShape()
    {
        var path = CliPaths.TodayLogFile();
        Assert.StartsWith(CliPaths.LogDir, path);
        Assert.EndsWith(".log", path);
        var name = Path.GetFileName(path);
        Assert.Matches(@"^oahu-cli-\d{8}\.log$", name);
    }

    [Fact]
    public void DefaultDownloadDir_IsUnderUserMusicOahu()
    {
        Assert.Contains("Oahu", CliPaths.DefaultDownloadDir);
        Assert.Contains("Downloads", CliPaths.DefaultDownloadDir);
    }
}
