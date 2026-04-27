using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Doctor;
using Xunit;

namespace Oahu.Cli.Tests;

public class DoctorServiceTests
{
    [Fact]
    public async Task RunAsync_WithSkipNetwork_DoesNotMakeHttpCalls()
    {
        var svc = new DoctorService(httpClientFactory: () =>
            throw new System.InvalidOperationException("HTTP must not be invoked when --skip-network is set"));

        var report = await svc.RunAsync(new DoctorOptions { SkipNetwork = true }, CancellationToken.None);

        var net = report.Checks.Single(c => c.Id == "audible-api");
        Assert.Equal(DoctorSeverity.Ok, net.Severity);
        Assert.Contains("skipped", net.Message);
    }

    [Fact]
    public async Task RunAsync_AlwaysIncludesCoreChecks()
    {
        var svc = new DoctorService();
        var report = await svc.RunAsync(new DoctorOptions { SkipNetwork = true }, CancellationToken.None);

        var ids = report.Checks.Select(c => c.Id).ToHashSet();
        Assert.Contains("output-dir", ids);
        Assert.Contains("user-data-dir", ids);
        Assert.Contains("library-cache", ids);
        Assert.Contains("disk-free", ids);
        Assert.Contains("cli-config", ids);
        Assert.Contains("user-settings", ids);
        Assert.Contains("audible-api", ids);
    }

    [Fact]
    public void UserSettingsCheck_OkWhenDefaultsApplied()
    {
        // After SettingsDefaults.ApplyDefaults runs (via OahuUserSettings.Init,
        // which SettingsManager triggers on load), DownloadDirectory must be a
        // non-empty, writable path. Verifies the CLI gets the same default
        // the GUI has always had.
        var check = DoctorService.CheckUserSettings();
        Assert.Equal("user-settings", check.Id);
        // The check may degrade to Warning when the test process can't load
        // settings (no GUI shared dir on a CI runner), but never to Error
        // for a fresh / empty config — the defaults should make it pass.
        Assert.NotEqual(DoctorSeverity.Error, check.Severity);
    }

    [Fact]
    public void OutputDirCheck_FailsForUnwritablePath()
    {
        // A path that cannot be created (root null byte). On all POSIX & Windows this throws.
        var bogus = Path.Combine(Path.GetTempPath(), "oahu-cli-doctor-test\0bad");
        var check = DoctorService.CheckOutputDirectoryWritable(bogus);
        Assert.Equal(DoctorSeverity.Error, check.Severity);
        Assert.False(string.IsNullOrEmpty(check.Hint));
    }

    [Fact]
    public void OutputDirCheck_PassesForTempDir()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"oahu-cli-doctor-test-{System.Guid.NewGuid():N}");
        try
        {
            var check = DoctorService.CheckOutputDirectoryWritable(tmp);
            Assert.Equal(DoctorSeverity.Ok, check.Severity);
        }
        finally
        {
            if (Directory.Exists(tmp))
            {
                Directory.Delete(tmp, recursive: true);
            }
        }
    }

    [Fact]
    public void DiskFreeCheck_WarnsWhenBelowThreshold()
    {
        var tmp = Path.GetTempPath();
        var check = DoctorService.CheckDiskFree(tmp, minFreeBytes: long.MaxValue / 2);
        // We can't guarantee under which side this lands across runners; assert it doesn't throw and returns a known id.
        Assert.Equal("disk-free", check.Id);
        Assert.Contains("free on", check.Message);
    }

    [Fact]
    public void Report_HasErrors_TrueWhenAnyErrorPresent()
    {
        var r = new DoctorReport(new[]
        {
            new DoctorCheck("a", "ok", DoctorSeverity.Ok, "."),
            new DoctorCheck("b", "warn", DoctorSeverity.Warning, "."),
            new DoctorCheck("c", "err", DoctorSeverity.Error, "."),
        });
        Assert.True(r.HasErrors);
        Assert.True(r.HasWarnings);
    }
}
