using System;
using Oahu.BooksDatabase;
using Oahu.Cli.App.Jobs;
using Oahu.Core;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class PerJobDownloadSettingsTests
{
    [Fact]
    public void Override_Wins_Over_Inner_Quality()
    {
        var inner = new DownloadSettings
        {
            DownloadQuality = EDownloadQuality.Normal,
            DownloadDirectory = "/tmp",
            MultiPartDownload = true,
            KeepEncryptedFiles = true,
        };
        var sut = new PerJobDownloadSettings(inner, EDownloadQuality.Extreme);

        Assert.Equal(EDownloadQuality.Extreme, sut.DownloadQuality);
        // Other members delegate.
        Assert.Equal("/tmp", sut.DownloadDirectory);
        Assert.True(sut.MultiPartDownload);
        Assert.True(sut.KeepEncryptedFiles);

        // Inner was not mutated.
        Assert.Equal(EDownloadQuality.Normal, inner.DownloadQuality);
    }

    [Fact]
    public void ChangedSettings_Subscription_Forwards_To_Inner()
    {
        var inner = new DownloadSettings { DownloadQuality = EDownloadQuality.High };
        var sut = new PerJobDownloadSettings(inner, EDownloadQuality.High);

        int hits = 0;
        EventHandler handler = (_, _) => hits++;
        sut.ChangedSettings += handler;
        inner.OnChange();
        Assert.Equal(1, hits);

        sut.ChangedSettings -= handler;
        inner.OnChange();
        Assert.Equal(1, hits);
    }
}
