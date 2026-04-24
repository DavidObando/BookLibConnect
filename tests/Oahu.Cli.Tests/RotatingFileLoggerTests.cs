using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Oahu.Cli.Logging;
using Xunit;

namespace Oahu.Cli.Tests;

public class RotatingFileLoggerProviderTests
{
    [Fact]
    public void Log_WritesTodayFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"oahu-cli-log-test-{Guid.NewGuid():N}");
        try
        {
            using var provider = new RotatingFileLoggerProvider(LogLevel.Debug, dir);
            var logger = provider.CreateLogger("Test.Category");
            logger.LogInformation("hello {Who}", "world");
            logger.LogWarning("warning");
            provider.Dispose();   // flush

            var files = Directory.GetFiles(dir, "oahu-cli-*.log");
            Assert.Single(files);

            var contents = File.ReadAllText(files[0]);
            Assert.Contains("INF [Test.Category] hello world", contents);
            Assert.Contains("WRN [Test.Category] warning", contents);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Log_RespectsMinimumLevel()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"oahu-cli-log-test-{Guid.NewGuid():N}");
        try
        {
            using var provider = new RotatingFileLoggerProvider(LogLevel.Warning, dir);
            var logger = provider.CreateLogger("X");
            logger.LogInformation("info");
            logger.LogError("err");
            provider.Dispose();

            var contents = File.ReadAllText(Directory.GetFiles(dir).Single());
            Assert.DoesNotContain("info", contents);
            Assert.Contains("err", contents);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
