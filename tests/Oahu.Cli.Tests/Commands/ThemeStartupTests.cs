using System;
using Oahu.Cli.Commands;
using Oahu.Cli.Tui.Themes;
using Xunit;

namespace Oahu.Cli.Tests.Commands;

/// <summary>
/// Verifies the precedence of theme resolution at TUI startup:
/// <c>--theme</c> flag &gt; <c>NO_COLOR</c>/<c>--no-color</c> &gt; persisted config &gt; default.
/// </summary>
[Collection("EnvVarSerial")]
public class ThemeStartupTests : IDisposable
{
    public ThemeStartupTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    [Fact]
    public void ExplicitFlag_Wins_Over_Config_And_NoColor()
    {
        var globals = new GlobalOptions { ThemeOverride = "Colorblind", ForceNoColor = true };
        var name = TuiCommand.ResolveStartupThemeName(globals, configuredTheme: "HighContrast");
        Assert.Equal("Colorblind", name);
    }

    [Fact]
    public void ExplicitFlag_IsCaseInsensitive()
    {
        var globals = new GlobalOptions { ThemeOverride = "highcontrast" };
        var name = TuiCommand.ResolveStartupThemeName(globals, configuredTheme: null);
        Assert.Equal("HighContrast", name);
    }

    [Fact]
    public void NoColor_Forces_Mono_When_No_Explicit_Flag()
    {
        var globals = new GlobalOptions { ForceNoColor = true };
        var name = TuiCommand.ResolveStartupThemeName(globals, configuredTheme: "HighContrast");
        Assert.Equal("Mono", name);
    }

    [Fact]
    public void Config_Theme_Wins_Over_Default_When_No_Flag()
    {
        var globals = new GlobalOptions();
        var name = TuiCommand.ResolveStartupThemeName(globals, configuredTheme: "HighContrast");
        Assert.Equal("HighContrast", name);
    }

    [Fact]
    public void Unknown_Configured_Theme_Falls_Back_To_Default()
    {
        var globals = new GlobalOptions();
        var name = TuiCommand.ResolveStartupThemeName(globals, configuredTheme: "Solarized");
        Assert.Equal("Default", name);
    }

    [Fact]
    public void Unknown_Explicit_Override_Falls_Through_To_Config()
    {
        var globals = new GlobalOptions { ThemeOverride = "Solarized" };
        var name = TuiCommand.ResolveStartupThemeName(globals, configuredTheme: "Mono");
        Assert.Equal("Mono", name);
    }

    [Fact]
    public void All_Empty_Returns_Default()
    {
        var name = TuiCommand.ResolveStartupThemeName(new GlobalOptions(), configuredTheme: null);
        Assert.Equal("Default", name);
    }
}
