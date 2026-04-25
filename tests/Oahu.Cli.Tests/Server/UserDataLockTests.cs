using System;
using System.IO;
using System.Runtime.InteropServices;
using Oahu.Cli.Server.Hosting;
using Xunit;

namespace Oahu.Cli.Tests.Server;

public sealed class UserDataLockTests
{
    [Fact]
    public void Acquire_Then_Second_Acquire_Throws_On_Windows()
    {
        // FileShare exclusivity is only enforced by the OS on Windows. On Unix,
        // .NET's FileShare flags are advisory and not honored by the kernel, so
        // the second open succeeds. The lock file is still useful as a PID
        // marker on all platforms; cross-process exclusivity in CI is exercised
        // by the cooperative GUI/CLI design rather than this unit test.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        var path = Path.Combine(Path.GetTempPath(), $"oahu-lock-{Guid.NewGuid():n}");
        using var first = new UserDataLock(path);
        first.Acquire();

        using var second = new UserDataLock(path);
        var ex = Assert.Throws<InvalidOperationException>(() => second.Acquire());
        Assert.Contains("already running", ex.Message);
    }

    [Fact]
    public void Lock_Records_Pid_And_Cleans_Up_On_Dispose()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oahu-lock-{Guid.NewGuid():n}");
        using (var first = new UserDataLock(path))
        {
            first.Acquire();
            Assert.True(File.Exists(path));
            var pid = File.ReadAllText(path).Trim();
            Assert.Equal(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), pid);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Acquire_Is_Idempotent_On_Same_Instance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oahu-lock-{Guid.NewGuid():n}");
        using var lk = new UserDataLock(path);
        lk.Acquire();
        lk.Acquire(); // no-op, must not throw.
        Assert.True(File.Exists(path));
    }
}
