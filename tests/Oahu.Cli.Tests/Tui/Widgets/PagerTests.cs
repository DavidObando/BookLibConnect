using System.Linq;
using Oahu.Cli.Tui.Widgets;
using Xunit;

namespace Oahu.Cli.Tests.Tui.Widgets;

public class PagerTests
{
    private static Pager NewPager(int viewport = 3, int lineCount = 10)
    {
        var p = new Pager { ViewportHeight = viewport };
        for (var i = 0; i < lineCount; i++)
        {
            p.Append($"line-{i}");
        }
        return p;
    }

    [Fact]
    public void Initial_Visible_Is_First_Window()
    {
        var p = NewPager(3, 10);
        var v = p.Visible();
        Assert.Equal(new[] { "line-0", "line-1", "line-2" }, v);
        Assert.True(p.AtTop);
        Assert.False(p.AtBottom);
    }

    [Fact]
    public void ScrollDown_Advances_Offset_And_Clamps_At_Bottom()
    {
        var p = NewPager(3, 10);
        p.ScrollDown(5);
        Assert.Equal(5, p.Offset);
        p.ScrollDown(100);
        Assert.True(p.AtBottom);
        Assert.Equal(p.MaxOffset, p.Offset);
    }

    [Fact]
    public void ScrollUp_Clamps_At_Top()
    {
        var p = NewPager(3, 10);
        p.ScrollDown(2);
        p.ScrollUp(50);
        Assert.True(p.AtTop);
    }

    [Fact]
    public void PageUp_PageDown_Move_By_Viewport()
    {
        var p = NewPager(3, 10);
        p.PageDown();
        Assert.Equal(3, p.Offset);
        p.PageUp();
        Assert.Equal(0, p.Offset);
    }

    [Fact]
    public void Top_And_Bottom_Jump()
    {
        var p = NewPager(3, 10);
        p.Bottom();
        Assert.Equal(p.MaxOffset, p.Offset);
        p.Top();
        Assert.Equal(0, p.Offset);
    }

    [Fact]
    public void SetContent_Preserves_Offset_When_Possible()
    {
        var p = NewPager(3, 10);
        p.ScrollDown(5);
        p.SetContent(Enumerable.Range(0, 8).Select(i => $"x-{i}"));
        Assert.True(p.Offset <= p.MaxOffset);
    }

    [Fact]
    public void Empty_Pager_Has_No_Visible_Lines()
    {
        var p = new Pager { ViewportHeight = 5 };
        Assert.Empty(p.Visible());
        Assert.True(p.AtTop);
        Assert.True(p.AtBottom);
    }
}
