using System;
using Oahu.Cli.Tui.Themes;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

public class ThemeTests : IDisposable
{
    public ThemeTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    [Fact]
    public void Default_Is_Active_On_Reset()
    {
        Theme.Reset();
        Assert.Equal("Default", Theme.Current.Name);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Mono")]
    [InlineData("HighContrast")]
    [InlineData("highcontrast")]
    [InlineData("Colorblind")]
    [InlineData("colorblind")]
    public void Use_Switches_To_Named_Theme(string name)
    {
        Theme.Use(name);
        Assert.Equal(name, Theme.Current.Name, ignoreCase: true);
    }

    [Fact]
    public void Use_Throws_For_Unknown_Theme()
    {
        var ex = Assert.Throws<ArgumentException>(() => Theme.Use("does-not-exist"));
        Assert.Contains("does-not-exist", ex.Message);
    }

    [Fact]
    public void Mono_Theme_Has_No_Coloured_Tokens()
    {
        // Every token in Mono must equal Spectre's Color.Default to guarantee no ANSI is emitted.
        var t = Themes.Mono;
        Assert.Equal(Spectre.Console.Color.Default, t.TextPrimary.Value);
        Assert.Equal(Spectre.Console.Color.Default, t.StatusError.Value);
        Assert.Equal(Spectre.Console.Color.Default, t.Brand.Value);
        Assert.Equal(Spectre.Console.Color.Default, t.Selected.Value);
    }

    [Fact]
    public void Available_Includes_All_Three_Builtins()
    {
        Assert.Contains(Theme.Available, t => t.Name == "Default");
        Assert.Contains(Theme.Available, t => t.Name == "Mono");
        Assert.Contains(Theme.Available, t => t.Name == "HighContrast");
    }
}
