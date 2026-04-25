using Oahu.Cli.Tui.Hooks;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

public class BreakpointTests
{
    [Theory]
    [InlineData(40, BreakpointKind.Compact)]
    [InlineData(79, BreakpointKind.Compact)]
    [InlineData(80, BreakpointKind.Narrow)]
    [InlineData(119, BreakpointKind.Narrow)]
    [InlineData(120, BreakpointKind.Wide)]
    [InlineData(220, BreakpointKind.Wide)]
    public void For_Width_Maps_To_Expected_Kind(int width, BreakpointKind expected)
    {
        Assert.Equal(expected, Breakpoint.For(width));
    }
}
