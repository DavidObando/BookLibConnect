using Oahu.Core;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class SettingsDefaultsTests
{
    [Fact]
    public void ApplyDefaults_FillsMissingDownloadDirectory()
    {
        var dl = new DownloadSettings();
        var ex = new ExportSettings();

        SettingsDefaults.ApplyDefaults(dl, ex);

        Assert.Equal(SettingsDefaults.DefaultDownloadDirectory, dl.DownloadDirectory);
    }

    [Fact]
    public void ApplyDefaults_PreservesExistingDownloadDirectory()
    {
        const string Custom = "/tmp/custom-oahu-downloads";
        var dl = new DownloadSettings { DownloadDirectory = Custom };
        var ex = new ExportSettings();

        SettingsDefaults.ApplyDefaults(dl, ex);

        Assert.Equal(Custom, dl.DownloadDirectory);
    }

    [Fact]
    public void ApplyDefaults_TreatsWhitespaceAsMissing()
    {
        var dl = new DownloadSettings { DownloadDirectory = "   " };
        var ex = new ExportSettings();

        SettingsDefaults.ApplyDefaults(dl, ex);

        Assert.Equal(SettingsDefaults.DefaultDownloadDirectory, dl.DownloadDirectory);
    }

    [Fact]
    public void ApplyDefaults_LeavesExportDirectoryAlone()
    {
        // ExportDirectory is opt-in (only meaningful when ExportToAax is true)
        // and ApplyDefaults must not silently set it. Otherwise a user who has
        // never enabled AAX export would suddenly find a path written into
        // their settings.
        var dl = new DownloadSettings();
        var ex = new ExportSettings();

        SettingsDefaults.ApplyDefaults(dl, ex);

        Assert.Null(ex.ExportDirectory);
    }
}
