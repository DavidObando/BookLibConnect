using Oahu.Cli.Tui.Icons;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

public class IconsTests
{
    [Fact]
    public void Render_With_Ascii_True_Returns_Ascii_Fallback()
    {
        Assert.Equal(Icons.Success.AsciiFallback, Icons.Success.Render(useAscii: true));
        Assert.Equal(Icons.Working.AsciiFallback, Icons.Working.Render(useAscii: true));
    }

    [Fact]
    public void Render_With_Ascii_False_Returns_Glyph()
    {
        Assert.Equal(Icons.Success.Glyph, Icons.Success.Render(useAscii: false));
        Assert.Equal(Icons.Working.Glyph, Icons.Working.Render(useAscii: false));
    }

    [Fact]
    public void Prefix_Icons_Are_Single_Cell_In_Both_Modes()
    {
        // Icons used in fixed-width prefixes must be single-cell in both modes.
        // (Arrows and other inline icons may be multi-char in ASCII fallback.)
        var prefixIcons = new[]
        {
            Icons.Success, Icons.Error, Icons.Warning, Icons.Info, Icons.Disabled,
            Icons.Prompt, Icons.Filled, Icons.Working, Icons.Empty,
        };
        foreach (var ic in prefixIcons)
        {
            Assert.True(ic.AsciiFallback.Length == 1, $"ASCII fallback of {ic.ScreenReaderLabel} not single-char: '{ic.AsciiFallback}'");

            var runeCount = 0;
            foreach (var _ in ic.Glyph.EnumerateRunes())
            {
                runeCount++;
            }
            Assert.Equal(1, runeCount);
        }
    }

    [Fact]
    public void Working_Indicator_Is_Half_Filled_Disc()
    {
        Assert.Equal("◐", Icons.Working.Glyph);
    }
}
