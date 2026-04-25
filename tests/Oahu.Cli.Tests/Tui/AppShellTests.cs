using System;
using System.Collections.Generic;
using Oahu.Cli.Tui.Logging;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Themes;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class AppShellTests : IDisposable
{
    public AppShellTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    private static TestConsole NewConsole(int width = 120)
    {
        var c = new TestConsole();
        c.Profile.Width = width;
        c.Profile.Height = 30;
        c.EmitAnsiSequences = false;
        return c;
    }

    private static AppShell NewShell(IReadOnlyList<ITabScreen>? tabs = null, LogRingBuffer? buf = null) =>
        new(NewConsole(), new AppShellOptions
        {
            Tabs = tabs,
            LogBuffer = buf,
            Profile = "alice",
            Region = "us",
            Version = "0.1.0",
        });

    private static ConsoleKeyInfo Key(char ch, ConsoleKey k = ConsoleKey.NoName, ConsoleModifiers mod = 0)
        => new(ch, k, shift: (mod & ConsoleModifiers.Shift) != 0, alt: (mod & ConsoleModifiers.Alt) != 0, control: (mod & ConsoleModifiers.Control) != 0);

    [Fact]
    public void Number_Keys_Switch_Tabs()
    {
        var shell = NewShell();
        Assert.Equal(0, shell.ActiveTab);
        shell.Dispatch(Key('3', ConsoleKey.D3));
        Assert.Equal(2, shell.ActiveTab);
        shell.Dispatch(Key('6', ConsoleKey.D6));
        Assert.Equal(5, shell.ActiveTab);
        // Out-of-range numbers are ignored.
        shell.Dispatch(Key('9', ConsoleKey.D9));
        Assert.Equal(5, shell.ActiveTab);
    }

    [Fact]
    public void Tab_And_ShiftTab_Cycle()
    {
        var shell = NewShell();
        shell.Dispatch(Key('\t', ConsoleKey.Tab));
        Assert.Equal(1, shell.ActiveTab);
        shell.Dispatch(Key('\t', ConsoleKey.Tab, ConsoleModifiers.Shift));
        Assert.Equal(0, shell.ActiveTab);
        // Wrap backward from 0 -> last.
        shell.Dispatch(Key('\t', ConsoleKey.Tab, ConsoleModifiers.Shift));
        Assert.Equal(shell.Tabs.Count - 1, shell.ActiveTab);
    }

    [Fact]
    public void Single_CtrlC_Shows_Toast_Without_Exiting()
    {
        var shell = NewShell();
        var action = shell.Dispatch(Key((char)3, ConsoleKey.C, ConsoleModifiers.Control));
        Assert.Equal(ShellAction.Continue, action);
    }

    [Fact]
    public void Double_CtrlC_Within_Window_Exits()
    {
        var shell = NewShell();
        var first = shell.Dispatch(Key((char)3, ConsoleKey.C, ConsoleModifiers.Control));
        var second = shell.Dispatch(Key((char)3, ConsoleKey.C, ConsoleModifiers.Control));
        Assert.Equal(ShellAction.Continue, first);
        Assert.Equal(ShellAction.ExitSigInt, second);
    }

    [Fact]
    public void L_Toggles_Logs_When_Buffer_Set()
    {
        var buf = new LogRingBuffer();
        var shell = NewShell(buf: buf);
        Assert.False(shell.LogsOpen);
        shell.Dispatch(Key('l', ConsoleKey.L));
        Assert.True(shell.LogsOpen);
        // L again (no Ctrl) closes via the overlay's own handler.
        shell.Dispatch(Key('l', ConsoleKey.L));
        Assert.False(shell.LogsOpen);
    }

    [Fact]
    public void L_Without_Buffer_Does_Nothing()
    {
        var shell = NewShell(buf: null);
        shell.Dispatch(Key('l', ConsoleKey.L));
        Assert.False(shell.LogsOpen);
    }

    [Fact]
    public void Logs_Esc_Closes_Overlay()
    {
        var buf = new LogRingBuffer();
        var shell = NewShell(buf: buf);
        shell.Dispatch(Key('l', ConsoleKey.L));
        Assert.True(shell.LogsOpen);
        shell.Dispatch(Key((char)27, ConsoleKey.Escape));
        Assert.False(shell.LogsOpen);
    }

    [Fact]
    public void KeyReader_EOF_Returns_Cleanly()
    {
        var shell = NewShell();
        var reader = new ScriptedReader();
        Assert.Equal(0, shell.Run(reader));
    }

    [Fact]
    public void Run_Honours_ExitSigInt_From_Dispatch()
    {
        var shell = NewShell();
        var reader = new ScriptedReader(
            Key((char)3, ConsoleKey.C, ConsoleModifiers.Control),
            Key((char)3, ConsoleKey.C, ConsoleModifiers.Control));
        Assert.Equal(130, shell.Run(reader));
    }

    [Fact]
    public void Screen_Capturing_Input_Suppresses_Global_L()
    {
        // Regression: typing 'l' in Library search opened the logs overlay
        // instead of being forwarded to the search TextInput.
        var buf = new LogRingBuffer();
        var capturingScreen = new InputCapturingScreen();
        var shell = new AppShell(NewConsole(), new AppShellOptions
        {
            Tabs = new ITabScreen[] { capturingScreen },
            LogBuffer = buf,
        });

        // Screen is capturing — 'l' should go to screen, not open logs.
        capturingScreen.Capturing = true;
        shell.Dispatch(Key('l', ConsoleKey.L));
        Assert.False(shell.LogsOpen, "'l' opened logs even though the screen was capturing input");
        Assert.True(capturingScreen.ReceivedL, "Screen did not receive the 'l' key");

        // Screen is NOT capturing — 'l' should open logs.
        capturingScreen.Capturing = false;
        shell.Dispatch(Key('l', ConsoleKey.L));
        Assert.True(shell.LogsOpen, "'l' should open logs when screen is not capturing input");
    }

    [Fact]
    public void Screen_Capturing_Input_Suppresses_Number_Keys()
    {
        var capturingScreen = new InputCapturingScreen();
        var placeholder = new InputCapturingScreen { Title = "Other", NumberKey = '2' };
        var shell = new AppShell(NewConsole(), new AppShellOptions
        {
            Tabs = new ITabScreen[] { capturingScreen, placeholder },
        });
        capturingScreen.Capturing = true;
        shell.Dispatch(Key('2', ConsoleKey.D2));
        Assert.Equal(0, shell.ActiveTab); // Should NOT switch tabs
    }

    private sealed class InputCapturingScreen : ITabScreen
    {
        public string Title { get; init; } = "Test";
        public char NumberKey { get; init; } = '1';
        public bool Capturing { get; set; }
        public bool ReceivedL { get; private set; }

        public IRenderable Render(int width, int height) => new Markup(string.Empty);

        public bool HandleKey(ConsoleKeyInfo key)
        {
            if (key.Key == ConsoleKey.L)
            {
                ReceivedL = true;
            }
            return Capturing; // Consume all keys when capturing
        }

        public IEnumerable<KeyValuePair<string, string?>> Hints =>
            Array.Empty<KeyValuePair<string, string?>>();
    }

    private sealed class ScriptedReader : AppShell.IKeyReader
    {
        private readonly Queue<ConsoleKeyInfo> queue;

        public ScriptedReader(params ConsoleKeyInfo[] keys)
        {
            queue = new Queue<ConsoleKeyInfo>(keys);
        }

        public ConsoleKeyInfo? ReadKey() => queue.Count == 0 ? null : queue.Dequeue();
    }
}
