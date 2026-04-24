using System;
using System.Collections.Generic;
using Oahu.Cli.Tui.Themes;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class WidgetSnapshotTests : IDisposable
{
    public WidgetSnapshotTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    private static TestConsole NewConsole(int width = 120)
    {
        var c = new TestConsole();
        c.Profile.Width = width;
        c.EmitAnsiSequences = false;
        return c;
    }

    [Fact]
    public void StatusLine_Includes_Verb_Hint_And_Metric()
    {
        var c = NewConsole();
        new StatusLine { Verb = "Decrypting", Hint = "Esc to cancel", Metric = "42 MB" }.Write(c);
        var output = c.Output;
        Assert.Contains("Decrypting", output);
        Assert.Contains("Esc to cancel", output);
        Assert.Contains("42 MB", output);
    }

    [Fact]
    public void HintBar_Filters_Empty_Actions()
    {
        var c = NewConsole();
        new HintBar()
            .Add("Tab", "next")
            .Add("Enter", null)
            .Add("Esc", string.Empty)
            .Add("?", "help")
            .Write(c);
        var output = c.Output;
        Assert.Contains("Tab", output);
        Assert.Contains("next", output);
        Assert.Contains("?", output);
        Assert.Contains("help", output);

        // Nothing should appear for the suppressed bindings.
        Assert.DoesNotContain("Enter", output);
    }

    [Fact]
    public void HintBar_Empty_Is_NoOp()
    {
        var c = NewConsole();
        new HintBar().Write(c);
        Assert.Equal(Environment.NewLine, c.Output);
    }

    [Fact]
    public void TimelineItem_Prefix_Width_Constant_Across_States()
    {
        // Render the same title in two different states; confirm the column where the title
        // begins is identical. This is the core "no layout shift" invariant from §6.5.
        var loading = RenderItem(new TimelineItem { Title = "Doing", State = TimelineState.Loading });
        var success = RenderItem(new TimelineItem { Title = "Doing", State = TimelineState.Success });
        var error = RenderItem(new TimelineItem { Title = "Doing", State = TimelineState.Error });

        var loadingTitle = loading.IndexOf("Doing", StringComparison.Ordinal);
        var successTitle = success.IndexOf("Doing", StringComparison.Ordinal);
        var errorTitle = error.IndexOf("Doing", StringComparison.Ordinal);

        Assert.True(loadingTitle > 0);
        Assert.Equal(loadingTitle, successTitle);
        Assert.Equal(loadingTitle, errorTitle);
    }

    [Fact]
    public void TimelineItem_Renders_Detail_Indented()
    {
        var c = NewConsole();
        new TimelineItem
        {
            Title = "Library cache",
            Description = "287 books",
            State = TimelineState.Info,
            Detail = "→ run 'library sync'",
        }.Write(c);
        Assert.Contains("Library cache", c.Output);
        Assert.Contains("287 books", c.Output);
        Assert.Contains("library sync", c.Output);
    }

    [Fact]
    public void SelectList_Marks_Cursor_And_Selection()
    {
        var c = NewConsole();
        new SelectList<string>
        {
            Items = new[] { "alpha", "beta", "gamma" },
            Format = s => s,
            CursorIndex = 1,
            SelectedIndices = new HashSet<int> { 0 },
        }.Write(c);
        var output = c.Output;
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
        Assert.Contains("gamma", output);

        // Cursor glyph (❯) and filled marker (●) should be emitted.
        Assert.Contains("❯", output);
        Assert.Contains("●", output);
    }

    [Fact]
    public void Dialog_Wraps_Body_And_Footer()
    {
        var c = NewConsole();
        new Dialog
        {
            Title = "Heads up",
            Body = new Spectre.Console.Markup("Body text"),
            Footer = new HintBar().Add("Esc", "dismiss"),
        }.Write(c);
        var output = c.Output;
        Assert.Contains("Heads up", output);
        Assert.Contains("Body text", output);
        Assert.Contains("Esc", output);
        Assert.Contains("dismiss", output);
    }

    [Fact]
    public void StyledTable_Adds_Bold_Headers_And_Rows()
    {
        var c = NewConsole();
        var t = StyledTable.Create()
            .AddBoldColumn("Title")
            .AddBoldColumn("Author");
        t.AddRow("Project Hail Mary", "Andy Weir");
        c.Write(t);
        var output = c.Output;
        Assert.Contains("Title", output);
        Assert.Contains("Author", output);
        Assert.Contains("Andy Weir", output);
    }

    [Fact]
    public void Mono_Theme_Renders_Without_Ansi_Escapes()
    {
        Theme.Use("Mono");
        var c = NewConsole();
        c.EmitAnsiSequences = true; // ensure we'd see escapes if any were emitted
        new StatusLine { Verb = "Working" }.Write(c);
        new TimelineItem { Title = "Step", State = TimelineState.Success }.Write(c);

        // Mono uses Color.Default for every token — Spectre suppresses colour markup for it.
        Assert.DoesNotContain("\u001b[3", c.Output); // foreground SGR like ESC[31m, ESC[32m, etc.
        Assert.DoesNotContain("\u001b[9", c.Output); // bright-foreground SGR like ESC[91m
    }

    [Fact]
    public void Ascii_Mode_Uses_Ascii_Glyphs_Only()
    {
        var c = NewConsole();
        new TimelineItem { Title = "x", State = TimelineState.Loading, UseAscii = true }.Write(c);
        new TimelineItem { Title = "y", State = TimelineState.Success, UseAscii = true }.Write(c);
        var output = c.Output;
        Assert.DoesNotContain("◐", output);
        Assert.DoesNotContain("✓", output);
        Assert.Contains("*", output); // Working ASCII fallback
        Assert.Contains("+", output); // Success ASCII fallback (will be confirmed by IconsTests)
    }

    private static string RenderItem(TimelineItem item)
    {
        var c = NewConsole();
        item.Write(c);
        return c.Output;
    }
}
