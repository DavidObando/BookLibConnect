using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Library;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Queue;
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
    private readonly Func<IQueueService>? queueServiceFactory;
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private readonly TextInput searchInput = new() { Label = "/", MaxLength = 128 };

    private IReadOnlyList<LibraryItem> allItems = Array.Empty<LibraryItem>();
    private IReadOnlyList<LibraryItem> filtered = Array.Empty<LibraryItem>();
    private int cursor;
    private int scrollOffset;
    private int lastListHeight = 20;
    private bool loaded;
    private bool loading;
    private Task? loadTask;
    private bool searchMode;
    private int spinnerTick;

    private IAppShellNavigator? navigator;
    private Task? enqueueTask;

    public LibraryScreen(AppShellState state, Func<ILibraryService> libraryServiceFactory)
        : this(state, libraryServiceFactory, queueServiceFactory: null)
    {
    }

    public LibraryScreen(
        AppShellState state,
        Func<ILibraryService> libraryServiceFactory,
        Func<IQueueService>? queueServiceFactory)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.libraryServiceFactory = libraryServiceFactory ?? throw new ArgumentNullException(nameof(libraryServiceFactory));
        this.queueServiceFactory = queueServiceFactory;
    }

    public string Title => "Library";

    public char NumberKey => '2';

    public bool IsLoading => loading;

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
                yield return new("PgUp/Dn", "page");
                yield return new("Space", "select");
                yield return new("a", "select all");
                if (queueServiceFactory is not null)
                {
                    yield return new("q", "enqueue");
                }
            }
        }
    }

    public void OnActivated(IAppShellNavigator navigator)
    {
        this.navigator = navigator;
    }

    public IRenderable Render(int width, int height)
    {
        EnsureLoaded();

        // Check if background load completed
        if (loading && loadTask is not null && loadTask.IsCompleted)
        {
            loading = false;
            loadTask = null;
        }

        // Show loading spinner while data is being fetched
        if (loading)
        {
            return RenderLoadingSpinner();
        }

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
        lastListHeight = listHeight;
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
            case ConsoleKey.PageUp:
                cursor = Math.Max(0, cursor - lastListHeight);
                return true;
            case ConsoleKey.PageDown:
                cursor = Math.Min(filtered.Count - 1, Math.Max(0, cursor + lastListHeight));
                return true;
            case ConsoleKey.Home:
                cursor = 0;
                return true;
            case ConsoleKey.End:
                cursor = Math.Max(0, filtered.Count - 1);
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
            case ConsoleKey.Q when key.Modifiers == 0:
                return EnqueueSelection();
        }

        if (key.KeyChar == '/')
        {
            searchMode = true;
            return true;
        }

        return false;
    }

    /// <summary>Load library items synchronously (used by tests and explicit refresh).</summary>
    public void Reload()
    {
        try
        {
            var lib = libraryServiceFactory();
            allItems = lib.ListAsync().GetAwaiter().GetResult();
            loaded = true;
            loading = false;
            ApplyFilter();
        }
        catch
        {
            loaded = true;
            loading = false;
            // Swallow to keep TUI stable.
        }
    }

    internal void EnsureLoaded()
    {
        if (!loaded && !loading)
        {
            loading = true;
            loadTask = Task.Run(() =>
            {
                try
                {
                    var lib = libraryServiceFactory();
                    var items = lib.ListAsync().GetAwaiter().GetResult();
                    allItems = items;
                    loaded = true;
                    ApplyFilter();
                }
                catch
                {
                    loaded = true;
                    // Swallow to keep TUI stable.
                }
                finally
                {
                    loading = false;
                }
            });
        }
    }

    /// <summary>Background task spawned by <c>q</c>; exposed for tests.</summary>
    internal Task? PendingEnqueue => enqueueTask;

    /// <summary>
    /// Enqueue the currently-selected items (or the cursor item when no
    /// multi-selection is active) into the persistent queue and switch to
    /// the Queue tab. No-op when no queue service was wired in.
    /// </summary>
    private bool EnqueueSelection()
    {
        if (queueServiceFactory is null)
        {
            return false;
        }

        IReadOnlyList<LibraryItem> targets;
        if (selected.Count > 0)
        {
            var byAsin = filtered.Concat(allItems)
                .GroupBy(i => i.Asin, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            targets = selected
                .Where(byAsin.ContainsKey)
                .Select(a => byAsin[a])
                .ToArray();
        }
        else if (cursor >= 0 && cursor < filtered.Count)
        {
            targets = new[] { filtered[cursor] };
        }
        else
        {
            return true;
        }

        if (targets.Count == 0)
        {
            return true;
        }

        var snapshot = targets;
        var nav = navigator;
        enqueueTask = Task.Run(async () =>
        {
            var added = 0;
            var skipped = 0;
            try
            {
                var queue = queueServiceFactory();
                foreach (var item in snapshot)
                {
                    var entry = new QueueEntry
                    {
                        Asin = item.Asin,
                        Title = item.Title,
                    };
                    if (await queue.AddAsync(entry).ConfigureAwait(false))
                    {
                        added++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
            }
            catch (Exception ex)
            {
                nav?.ShowToast($"Enqueue failed: {ex.Message}");
                return;
            }

            var msg = (added, skipped) switch
            {
                (0, 0) => "Nothing to enqueue.",
                (_, 0) => $"Enqueued {added} {Pluralize(added, "title", "titles")}.",
                (0, _) => $"Already in queue ({skipped} skipped).",
                _ => $"Enqueued {added} · {skipped} already in queue.",
            };
            nav?.ShowToast(msg);
            if (added > 0)
            {
                nav?.SwitchToTab('3');
            }
        });

        selected.Clear();
        return true;
    }

    private IRenderable RenderLoadingSpinner()
    {
        spinnerTick++;
        var spinChars = new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };
        var ch = spinChars[spinnerTick % spinChars.Length];
        var b = Tokens.Tokens.Brand.Value.ToMarkup();
        var s = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        return new Padder(new Rows(new IRenderable[]
        {
            new Markup($"[{b}]{ch}[/] [{s}]Loading library…[/]"),
        })).Padding(2, 1, 2, 1);
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

    private static string Pluralize(int n, string singular, string plural) =>
        n == 1 ? singular : plural;
}
