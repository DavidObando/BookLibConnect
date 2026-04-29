using System.IO;
using Oahu.Cli.Tui.Shell;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

public class AltScreenTests
{
    [Fact]
    public void SyncSequences_AreValidDecPrivateMode2026()
    {
        Assert.Equal("\u001b[?2026h", AltScreen.SyncStartSequence);
        Assert.Equal("\u001b[?2026l", AltScreen.SyncEndSequence);
    }

    [Fact]
    public void EnterSequence_SwitchesToAltScreenAndHidesCursor()
    {
        Assert.Contains("\u001b[?1049h", AltScreen.EnterSequence);
        Assert.Contains("\u001b[?25l", AltScreen.EnterSequence);
    }

    [Fact]
    public void LeaveSequence_RestoresPrimaryBufferAndShowsCursor()
    {
        Assert.Contains("\u001b[?25h", AltScreen.LeaveSequence);
        Assert.Contains("\u001b[?1049l", AltScreen.LeaveSequence);
    }

    /// <summary>
    /// Regression: on Windows, StringWriter.NewLine is \r\n. If the frame
    /// post-process only replaces \n with \e[K\n, the result is \r\e[K\n
    /// which moves the cursor to column 1 then erases the entire line,
    /// producing a blank screen.
    /// </summary>
    [Fact]
    public void InjectEraseBeforeNewlines_CrLf_MustNotProduceCrEscK()
    {
        // Simulate a Windows-style StringWriter (CRLF newlines).
        var sw = new StringWriter { NewLine = "\r\n" };
        sw.Write("Header");
        sw.WriteLine();
        sw.Write("Body");
        sw.WriteLine();

        var frame = AltScreen.InjectEraseBeforeNewlines(sw.ToString());

        Assert.DoesNotContain("\r\u001b[K", frame); // would erase each line
        Assert.Contains("Header\u001b[K\n", frame);
        Assert.Contains("Body\u001b[K\n", frame);
    }

    /// <summary>
    /// Even when the StringWriter uses LF-only newlines (macOS/Linux),
    /// the post-process should still inject \e[K before each \n.
    /// </summary>
    [Fact]
    public void InjectEraseBeforeNewlines_LfOnly_InjectsEraseBeforeNewline()
    {
        var sw = new StringWriter { NewLine = "\n" };
        sw.Write("Line1");
        sw.WriteLine();
        sw.Write("Line2");
        sw.WriteLine();

        var frame = AltScreen.InjectEraseBeforeNewlines(sw.ToString());

        Assert.Equal("Line1\u001b[K\nLine2\u001b[K\n", frame);
    }

    [Fact]
    public void InjectEraseBeforeNewlines_StripsLoneCarriageReturns()
    {
        var frame = AltScreen.InjectEraseBeforeNewlines("a\rb\nc");

        Assert.Equal("ab\u001b[K\nc", frame);
    }

    [Fact]
    public void InjectEraseBeforeNewlines_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, AltScreen.InjectEraseBeforeNewlines(string.Empty));
        Assert.Equal(string.Empty, AltScreen.InjectEraseBeforeNewlines(null!));
    }
}
