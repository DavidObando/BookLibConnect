using Oahu.Cli.App.Jobs;
using Oahu.Core;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class PerJobExportSettingsTests
{
    [Fact]
    public void Override_Wins_Over_Inner()
    {
        var inner = new ExportSettings
        {
            ExportToAax = false,
            ExportDirectory = "/tmp/inner",
        };
        var sut = new PerJobExportSettings(inner, exportToAax: true, exportDirectory: "/tmp/job");

        Assert.Equal(true, sut.ExportToAax);
        Assert.Equal("/tmp/job", sut.ExportDirectory);

        // Inner was not mutated.
        Assert.Equal(false, inner.ExportToAax);
        Assert.Equal("/tmp/inner", inner.ExportDirectory);
    }

    [Fact]
    public void Null_Overrides_Delegate_To_Inner()
    {
        var inner = new ExportSettings
        {
            ExportToAax = true,
            ExportDirectory = "/tmp/inner",
        };
        var sut = new PerJobExportSettings(inner);

        Assert.Equal(true, sut.ExportToAax);
        Assert.Equal("/tmp/inner", sut.ExportDirectory);
    }
}
