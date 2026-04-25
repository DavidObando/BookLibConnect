using System;
using Oahu.Cli.Tui.Themes;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console.Testing;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class TabStripTests : IDisposable
{
    public TabStripTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    [Fact]
    public void Renders_All_Titles_With_Number_Prefixes()
    {
        var c = new TestConsole();
        c.Profile.Width = 120;
        c.EmitAnsiSequences = false;
        new TabStrip
        {
            Titles = new[] { "Home", "Library", "Queue" },
            ActiveIndex = 0,
        }.Write(c);
        var output = c.Output;
        Assert.Contains("1 Home", output);
        Assert.Contains("2 Library", output);
        Assert.Contains("3 Queue", output);
    }

    [Fact]
    public void Active_Index_Out_Of_Range_Renders_Without_Highlight()
    {
        // Defensive: the strip should not crash if ActiveIndex is out of range.
        var c = new TestConsole();
        c.Profile.Width = 80;
        c.EmitAnsiSequences = false;
        var ex = Record.Exception(() => new TabStrip
        {
            Titles = new[] { "A", "B" },
            ActiveIndex = 99,
        }.Write(c));
        Assert.Null(ex);
    }
}
