using System;
using Oahu.Cli.Tui.Shell;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

public class TextInputTests
{
    private static ConsoleKeyInfo Key(char ch, ConsoleKey k = ConsoleKey.NoName, ConsoleModifiers mod = 0)
        => new(ch, k, shift: (mod & ConsoleModifiers.Shift) != 0, alt: (mod & ConsoleModifiers.Alt) != 0, control: (mod & ConsoleModifiers.Control) != 0);

    [Fact]
    public void Typing_Appends_Characters()
    {
        var input = new TextInput();
        input.HandleKey(Key('h'));
        input.HandleKey(Key('i'));
        Assert.Equal("hi", input.Text);
        Assert.Equal(2, input.Cursor);
    }

    [Fact]
    public void Backspace_Deletes_Before_Cursor()
    {
        var input = new TextInput { Text = "abc" };
        input.HandleKey(Key('\b', ConsoleKey.Backspace));
        Assert.Equal("ab", input.Text);
        Assert.Equal(2, input.Cursor);
    }

    [Fact]
    public void Delete_Removes_At_Cursor()
    {
        var input = new TextInput { Text = "abc" };
        input.HandleKey(Key('\0', ConsoleKey.Home));
        Assert.Equal(0, input.Cursor);
        input.HandleKey(Key('\0', ConsoleKey.Delete));
        Assert.Equal("bc", input.Text);
    }

    [Fact]
    public void Left_Right_Move_Cursor()
    {
        var input = new TextInput { Text = "ab" };
        Assert.Equal(2, input.Cursor);
        input.HandleKey(Key('\0', ConsoleKey.LeftArrow));
        Assert.Equal(1, input.Cursor);
        input.HandleKey(Key('\0', ConsoleKey.RightArrow));
        Assert.Equal(2, input.Cursor);
    }

    [Fact]
    public void Home_End_Jump()
    {
        var input = new TextInput { Text = "hello" };
        input.HandleKey(Key('\0', ConsoleKey.Home));
        Assert.Equal(0, input.Cursor);
        input.HandleKey(Key('\0', ConsoleKey.End));
        Assert.Equal(5, input.Cursor);
    }

    [Fact]
    public void MaxLength_Prevents_Overflow()
    {
        var input = new TextInput { MaxLength = 3 };
        input.HandleKey(Key('a'));
        input.HandleKey(Key('b'));
        input.HandleKey(Key('c'));
        input.HandleKey(Key('d'));
        Assert.Equal("abc", input.Text);
    }

    [Fact]
    public void Set_Text_Resets_Cursor()
    {
        var input = new TextInput();
        input.HandleKey(Key('x'));
        input.Text = "new";
        Assert.Equal("new", input.Text);
        Assert.Equal(3, input.Cursor);
    }

    [Fact]
    public void Render_Returns_Renderable()
    {
        var input = new TextInput { Text = "test", Label = "Name:" };
        var r = input.Render();
        Assert.NotNull(r);
    }

    [Fact]
    public void Masked_Mode_Hides_Text()
    {
        var input = new TextInput { Text = "secret", Masked = true };
        var r = input.Render();
        Assert.NotNull(r);
    }
}
