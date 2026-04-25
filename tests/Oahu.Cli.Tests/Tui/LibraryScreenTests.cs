using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Screens;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Themes;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class LibraryScreenTests : IDisposable
{
    public LibraryScreenTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    private static ConsoleKeyInfo Key(char ch, ConsoleKey k = ConsoleKey.NoName, ConsoleModifiers mod = 0)
        => new(ch, k, shift: (mod & ConsoleModifiers.Shift) != 0, alt: (mod & ConsoleModifiers.Alt) != 0, control: (mod & ConsoleModifiers.Control) != 0);

    private static LibraryScreen CreateScreen(IReadOnlyList<LibraryItem>? items = null)
    {
        var lib = new FakeLibraryService { Items = items ?? Array.Empty<LibraryItem>() };
        return new LibraryScreen(new AppShellState(), () => lib);
    }

    private static LibraryItem MakeItem(string asin, string title, params string[] authors) =>
        new() { Asin = asin, Title = title, Authors = authors };

    [Fact]
    public void Render_Empty_Library()
    {
        var screen = CreateScreen();
        screen.Reload();
        var r = screen.Render(80, 20);
        Assert.NotNull(r);
        Assert.Empty(screen.Items);
    }

    [Fact]
    public void Navigate_With_JK()
    {
        var screen = CreateScreen(new[] { MakeItem("1", "Book A"), MakeItem("2", "Book B"), MakeItem("3", "Book C") });
        screen.Reload();
        Assert.Equal(0, screen.Cursor);
        screen.HandleKey(Key('j', ConsoleKey.J));
        Assert.Equal(1, screen.Cursor);
        screen.HandleKey(Key('k', ConsoleKey.K));
        Assert.Equal(0, screen.Cursor);
    }

    [Fact]
    public void Space_Toggles_Selection()
    {
        var screen = CreateScreen(new[] { MakeItem("1", "Book A") });
        screen.Reload();
        Assert.Equal(0, screen.SelectedCount);
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar));
        Assert.Equal(1, screen.SelectedCount);
        screen.HandleKey(Key(' ', ConsoleKey.Spacebar));
        Assert.Equal(0, screen.SelectedCount);
    }

    [Fact]
    public void A_Selects_All_Then_Deselects()
    {
        var screen = CreateScreen(new[] { MakeItem("1", "A"), MakeItem("2", "B") });
        screen.Reload();
        screen.HandleKey(Key('a', ConsoleKey.A));
        Assert.Equal(2, screen.SelectedCount);
        screen.HandleKey(Key('a', ConsoleKey.A));
        Assert.Equal(0, screen.SelectedCount);
    }

    [Fact]
    public void Search_Filters_Items()
    {
        var screen = CreateScreen(new[]
        {
            MakeItem("1", "The Great Gatsby"),
            MakeItem("2", "Moby Dick"),
            MakeItem("3", "Gatsby Returns"),
        });
        screen.Reload();
        Assert.Equal(3, screen.Items.Count);

        // Enter search mode
        screen.HandleKey(Key('/', ConsoleKey.Oem2));
        screen.HandleKey(Key('g'));
        screen.HandleKey(Key('a'));
        screen.HandleKey(Key('t'));
        screen.HandleKey(Key('\r', ConsoleKey.Enter));

        Assert.Equal(2, screen.Items.Count); // "Gatsby" matches 2
    }

    [Fact]
    public void Esc_Clears_Search()
    {
        var screen = CreateScreen(new[] { MakeItem("1", "Book A"), MakeItem("2", "Book B") });
        screen.Reload();

        // Search for "A"
        screen.HandleKey(Key('/', ConsoleKey.Oem2));
        screen.HandleKey(Key('A'));
        screen.HandleKey(Key('\r', ConsoleKey.Enter));
        Assert.Single(screen.Items);

        // Esc clears filter
        screen.HandleKey(Key((char)27, ConsoleKey.Escape));
        Assert.Equal(2, screen.Items.Count);
    }

    [Fact]
    public void Title_Is_Library()
    {
        var screen = CreateScreen();
        Assert.Equal("Library", screen.Title);
        Assert.Equal('2', screen.NumberKey);
    }

    private sealed class FakeLibraryService : ILibraryService
    {
        public IReadOnlyList<LibraryItem> Items { get; set; } = Array.Empty<LibraryItem>();

        public Task<IReadOnlyList<LibraryItem>> ListAsync(LibraryFilter? filter = null, CancellationToken ct = default)
            => Task.FromResult(Items);

        public Task<LibraryItem?> GetAsync(string asin, CancellationToken ct = default)
            => Task.FromResult<LibraryItem?>(null);

        public Task<int> SyncAsync(string profileAlias, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
