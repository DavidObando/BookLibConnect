using System;
using System.Collections.Generic;
using System.Linq;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Shell;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Screens;

/// <summary>
/// Library screen (tab 2). Searchable, multi-select table with detail
/// panel. Per design TUI-exploration §3–4.
/// </summary>
public sealed class LibraryScreen : ITabScreen
{
    private readonly AppShellState state;
    private readonly Func<ILibraryService> libraryServiceFactory;
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private readonly TextInput searchInput = new() { Label = "/", MaxLength = 128 };

    private IReadOnlyList<LibraryItem> allItems = Array.Empty<LibraryItem>();
    private IReadOnlyList<LibraryItem> filtered = Array.Empty<LibraryItem>();
    private int cursor;
    private int scrollOffset;
    private bool loaded;
    private bool searchMode;

    public LibraryScreen(AppShellState state, Func<ILibraryService> libraryServiceFactory)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.libraryServiceFactory = libraryServiceFactory ?? throw new ArgumentNullException(nameof(libraryServiceFactory));
    }

    public string Title => "Library";

    public char NumberKey => '2';

    public int Cursor => cursor;

    public int SelectedCount => selected.Count;

    public IReadOnlyList<LibraryItem> Items => filtered;

    public IEnumerable<KeyValuePair<string, string?>> Hints
    {
        get
        {
            if (searchMode)
            {
                yield return new("Enter", "search");
                yield return new("Esc", "cancel");
            }
            else
            {
                yield return new("/", "search");
                yield return new("↑↓", "navigate");
                yield return new("Space", "select");
                yield return new("a", "select all");
            }
        }
    }

    public IRenderable Render(int width, int height)
    {
        EnsureLoaded();
        var lines = new List<IRenderable>();

        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();
        var success = Tokens.Tokens.StatusSuccess.Value.ToMarkup();

        // Search bar
        if (searchMode)
        {
            lines.Add(searchInput.Render());
        }
        else if (!string.IsNullOrEmpty(searchInput.Text))
        {
            lines.Add(new Markup($"[{tertiary}]Filter: {Markup.Escape(searchInput.Text)}  (/ to change, Esc to clear)[/]"));
        }

        // Summary line
        var selStr = selected.Count > 0 ? $"  [{brand}]{selected.Count} selected[/]" : string.Empty;
        lines.Add(new Markup($"[{secondary}]{filtered.Count} of {allItems.Count} titles{selStr}[/]"));
        lines.Add(new Markup(string.Empty));

        if (filtered.Count == 0)
        {
            lines.Add(new Markup($"[{tertiary}]{(allItems.Count == 0 ? "Library is empty. Sync from the Home tab." : "No matches.")}[/]"));
            return new Padder(new Rows(lines)).Padding(2, 1, 2, 1);
        }

        // Visible rows
        var listHeight = Math.Max(1, height - lines.Count - 2);
        AdjustScroll(listHeight);

        var end = Math.Min(scrollOffset + listHeight, filtered.Count);
        for (var i = scrollOffset; i < end; i++)
        {
            var item = filtered[i];
            var isCursor = i == cursor;
            var isSel = selected.Contains(item.Asin);
            var mark = isSel ? $"[{success}]✓[/]" : " ";
            var pointer = isCursor ? $"[{brand}]❯[/]" : " ";
            var style = isCursor ? $"bold {primary}" : secondary;
            var authors = item.Authors.Length > 0 ? string.Join(", ", item.Authors) : string.Empty;
            var runtime = item.Runtime is { } r ? FormatRuntime(r) : string.Empty;

            lines.Add(new Markup($"  {pointer} {mark}  [{style}]{Markup.Escape(Truncate(item.Title, width - 30))}[/]  [{tertiary}]{Markup.Escape(Truncate(authors, 30))}[/]  [{tertiary}]{runtime}[/]"));
        }

        if (filtered.Count > listHeight)
        {
            lines.Add(new Markup($"[{tertiary}]  ↕ {scrollOffset + 1}–{end} of {filtered.Count}[/]"));
        }

        return new Padder(new Rows(lines)).Padding(2, 0, 2, 0);
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (searchMode)
        {
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    searchMode = false;
                    ApplyFilter();
                    return true;
                case ConsoleKey.Escape:
                    searchMode = false;
                    searchInput.Text = string.Empty;
                    ApplyFilter();
                    return true;
                default:
                    return searchInput.HandleKey(key);
            }
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                cursor = Math.Max(0, cursor - 1);
                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                cursor = Math.Min(filtered.Count - 1, Math.Max(0, cursor + 1));
                return true;
            case ConsoleKey.Spacebar:
                if (cursor >= 0 && cursor < filtered.Count)
                {
                    var asin = filtered[cursor].Asin;
                    if (!selected.Remove(asin))
                    {
                        selected.Add(asin);
                    }
                }
                return true;
            case ConsoleKey.A when key.Modifiers == 0:
                if (selected.Count == filtered.Count)
                {
                    selected.Clear();
                }
                else
                {
                    foreach (var item in filtered)
                    {
                        selected.Add(item.Asin);
                    }
                }
                return true;
            case ConsoleKey.Escape:
                if (!string.IsNullOrEmpty(searchInput.Text))
                {
                    searchInput.Text = string.Empty;
                    ApplyFilter();
                    return true;
                }
                if (selected.Count > 0)
                {
                    selected.Clear();
                    return true;
                }
                break;
        }

        if (key.KeyChar == '/')
        {
            searchMode = true;
            return true;
        }

        return false;
    }

    /// <summary>Load library items.</summary>
    public void Reload()
    {
        try
        {
            var lib = libraryServiceFactory();
            allItems = lib.ListAsync().GetAwaiter().GetResult();
            loaded = true;
            ApplyFilter();
        }
        catch
        {
            // Swallow to keep TUI stable.
        }
    }

    internal void EnsureLoaded()
    {
        if (!loaded)
        {
            Reload();
        }
    }

    private void ApplyFilter()
    {
        var search = searchInput.Text.Trim();
        if (string.IsNullOrEmpty(search))
        {
            filtered = allItems;
        }
        else
        {
            filtered = allItems
                .Where(i =>
                    i.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    i.Authors.Any(a => a.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (i.Series?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToArray();
        }
        cursor = Math.Min(cursor, Math.Max(0, filtered.Count - 1));
    }

    private void AdjustScroll(int visibleHeight)
    {
        if (cursor < scrollOffset)
        {
            scrollOffset = cursor;
        }
        else if (cursor >= scrollOffset + visibleHeight)
        {
            scrollOffset = cursor - visibleHeight + 1;
        }
    }

    private static string FormatRuntime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h{ts.Minutes:D2}m";
        }
        return $"{ts.Minutes}m";
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
