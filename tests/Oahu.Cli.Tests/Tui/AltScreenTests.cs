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
}
