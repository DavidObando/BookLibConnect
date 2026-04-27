using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Queue;
using Oahu.Cli.Tui.Shell;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Screens;

/// <summary>
/// Queue screen (tab 3). Shows the persistent <c>queue.json</c> entries with
/// reorder / remove / submit actions. Per design TUI-exploration §5.
/// </summary>
public sealed class QueueScreen : ITabScreen
{
    private readonly Func<IQueueService> queueServiceFactory;
    private readonly Func<IJobService> jobServiceFactory;

    private IReadOnlyList<QueueEntry> entries = Array.Empty<QueueEntry>();
    private int cursor;
    private int scrollOffset;
    private int lastListHeight = 20;

    private bool loading;
    private Task? loadTask;
    private bool busy;
    private string? statusMessage;
    private int spinnerTick;

    private IAppShellNavigator? navigator;

    public QueueScreen(Func<IQueueService> queueServiceFactory, Func<IJobService> jobServiceFactory)
    {
        this.queueServiceFactory = queueServiceFactory ?? throw new ArgumentNullException(nameof(queueServiceFactory));
        this.jobServiceFactory = jobServiceFactory ?? throw new ArgumentNullException(nameof(jobServiceFactory));
    }

    public string Title => "Queue";

    public char NumberKey => '3';

    public bool IsLoading
    {
        get
        {
            // Reconcile with the background load task so AppShell sees the
            // current truth when it samples NeedsTimedRefresh right after a
            // Render call. Without this, a load that finishes between the
            // spinner being drawn and AppShell reading NeedsTimedRefresh would
            // leave a frozen spinner on screen until the next keypress.
            var t = loadTask;
            if (loading && t is not null && t.IsCompleted)
            {
                loading = false;
                loadTask = null;
            }
            return loading;
        }
    }

    public bool NeedsTimedRefresh => IsLoading || busy;

    public IReadOnlyList<QueueEntry> Entries => entries;

    public int Cursor => cursor;

    public IEnumerable<KeyValuePair<string, string?>> Hints
    {
        get
        {
            yield return new("↑↓", "navigate");
            yield return new("Shift+↑↓", "move");
            yield return new("x", "remove");
            yield return new("Enter", "run");
            yield return new("R", "run all");
            yield return new("c", "clear");
            yield return new("F5", "reload");
        }
    }

    public void OnActivated(IAppShellNavigator navigator)
    {
        this.navigator = navigator;
        Reload();
    }

    public void OnDeactivated()
    {
        // Nothing to tear down — load tasks finish on their own.
    }

    public IRenderable Render(int width, int height)
    {
        // Pump background load completions.
        if (loading && loadTask is not null && loadTask.IsCompleted)
        {
            loading = false;
            loadTask = null;
        }

        if (loading)
        {
            return RenderSpinner("Loading queue…");
        }

        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();

        var lines = new List<IRenderable>
        {
            new Markup($"[{primary} bold]Queue[/]  [{secondary}]({entries.Count} pending)[/]"),
            new Markup(string.Empty),
        };

        if (busy)
        {
            spinnerTick++;
            var spinChars = new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };
            var ch = spinChars[spinnerTick % spinChars.Length];
            lines.Add(new Markup($"[{brand}]{ch}[/] [{secondary}]Working…[/]"));
            lines.Add(new Markup(string.Empty));
        }
        else if (!string.IsNullOrEmpty(statusMessage))
        {
            lines.Add(new Markup($"[{tertiary}]{Markup.Escape(statusMessage!)}[/]"));
            lines.Add(new Markup(string.Empty));
        }

        if (entries.Count == 0)
        {
            lines.Add(new Markup($"[{tertiary}]Queue is empty. Add titles from the Library tab.[/]"));
            return new Padder(new Rows(lines)).Padding(2, 1, 2, 1);
        }

        var listHeight = Math.Max(1, height - lines.Count - 2);
        lastListHeight = listHeight;
        AdjustScroll(listHeight);

        var end = Math.Min(scrollOffset + listHeight, entries.Count);
        for (var i = scrollOffset; i < end; i++)
        {
            var entry = entries[i];
            var isCursor = i == cursor;
            var pointer = isCursor ? $"[{brand}]❯[/]" : " ";
            var style = isCursor ? $"bold {primary}" : secondary;
            var quality = entry.Quality.ToString().ToLowerInvariant();
            var profile = entry.ProfileAlias is null ? string.Empty : $"  [{tertiary}]@{Markup.Escape(entry.ProfileAlias)}[/]";

            lines.Add(new Markup(
                $"  {pointer}  [{style}]{(i + 1),3}. {Markup.Escape(Truncate(entry.Title, width - 28))}[/]  [{tertiary}]{quality}[/]{profile}"));
        }

        if (entries.Count > listHeight)
        {
            lines.Add(new Markup($"[{tertiary}]  ↕ {scrollOffset + 1}–{end} of {entries.Count}[/]"));
        }

        return new Padder(new Rows(lines)).Padding(2, 0, 2, 0);
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (busy)
        {
            // Ignore input while a mutation is in flight to keep the model consistent.
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                MoveSelected(-1);
                return true;
            case ConsoleKey.DownArrow when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                MoveSelected(+1);
                return true;
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                cursor = Math.Max(0, cursor - 1);
                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                cursor = Math.Min(entries.Count - 1, Math.Max(0, cursor + 1));
                return true;
            case ConsoleKey.PageUp:
                cursor = Math.Max(0, cursor - lastListHeight);
                return true;
            case ConsoleKey.PageDown:
                cursor = Math.Min(entries.Count - 1, Math.Max(0, cursor + lastListHeight));
                return true;
            case ConsoleKey.Home:
                cursor = 0;
                return true;
            case ConsoleKey.End:
                cursor = Math.Max(0, entries.Count - 1);
                return true;
            case ConsoleKey.X:
            case ConsoleKey.Delete:
                RemoveSelected();
                return true;
            case ConsoleKey.Enter:
                RunSelected();
                return true;
            case ConsoleKey.R when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                RunAll();
                return true;
            case ConsoleKey.C when key.Modifiers == 0:
                ClearAll();
                return true;
            case ConsoleKey.F5:
                Reload();
                return true;
        }
        return false;
    }

    private void Reload()
    {
        if (loading)
        {
            return;
        }
        loading = true;
        loadTask = Task.Run(async () =>
        {
            try
            {
                var svc = queueServiceFactory();
                entries = await svc.ListAsync().ConfigureAwait(false);
                cursor = Math.Min(cursor, Math.Max(0, entries.Count - 1));
            }
            catch (Exception ex)
            {
                statusMessage = $"Failed to load queue: {ex.Message}";
                entries = Array.Empty<QueueEntry>();
            }
        });
    }

    private void MoveSelected(int delta)
    {
        if (cursor < 0 || cursor >= entries.Count)
        {
            return;
        }
        var asin = entries[cursor].Asin;
        var newIndex = cursor + delta;
        if (newIndex < 0 || newIndex >= entries.Count)
        {
            return;
        }
        StartMutation(async () =>
        {
            var svc = queueServiceFactory();
            await svc.MoveAsync(asin, delta).ConfigureAwait(false);
            entries = await svc.ListAsync().ConfigureAwait(false);
            cursor = Math.Max(0, Math.Min(newIndex, entries.Count - 1));
        }, msg: null);
    }

    private void RemoveSelected()
    {
        if (cursor < 0 || cursor >= entries.Count)
        {
            return;
        }
        var asin = entries[cursor].Asin;
        var prevCursor = cursor;
        StartMutation(async () =>
        {
            var svc = queueServiceFactory();
            await svc.RemoveAsync(asin).ConfigureAwait(false);
            entries = await svc.ListAsync().ConfigureAwait(false);
            cursor = Math.Max(0, Math.Min(prevCursor, entries.Count - 1));
        }, "Removed.");
    }

    private void RunSelected()
    {
        if (cursor < 0 || cursor >= entries.Count)
        {
            return;
        }
        var entry = entries[cursor];
        StartMutation(async () =>
        {
            await SubmitEntryAsync(entry).ConfigureAwait(false);
            var svc = queueServiceFactory();
            await svc.RemoveAsync(entry.Asin).ConfigureAwait(false);
            entries = await svc.ListAsync().ConfigureAwait(false);
            cursor = Math.Max(0, Math.Min(cursor, entries.Count - 1));
        }, "Submitted.", switchToJobs: true);
    }

    private void RunAll()
    {
        if (entries.Count == 0)
        {
            return;
        }
        var snapshot = entries.ToArray();
        StartMutation(async () =>
        {
            var svc = queueServiceFactory();
            var submitted = 0;
            foreach (var entry in snapshot)
            {
                try
                {
                    await SubmitEntryAsync(entry).ConfigureAwait(false);
                    await svc.RemoveAsync(entry.Asin).ConfigureAwait(false);
                    submitted++;
                }
                catch
                {
                    // Stop on first failure, leaving the unsubmitted entries
                    // (and the failing one) in the queue for inspection.
                    break;
                }
            }
            entries = await svc.ListAsync().ConfigureAwait(false);
            cursor = Math.Max(0, Math.Min(cursor, entries.Count - 1));
            statusMessage = $"Submitted {submitted} of {snapshot.Length}.";
        }, msg: null, switchToJobs: true);
    }

    private void ClearAll()
    {
        if (entries.Count == 0)
        {
            return;
        }
        StartMutation(async () =>
        {
            var svc = queueServiceFactory();
            await svc.ClearAsync().ConfigureAwait(false);
            entries = Array.Empty<QueueEntry>();
            cursor = 0;
        }, "Queue cleared.");
    }

    private async Task SubmitEntryAsync(QueueEntry entry)
    {
        var job = jobServiceFactory();
        var req = new JobRequest
        {
            Asin = entry.Asin,
            Title = entry.Title,
            Quality = entry.Quality,
            ProfileAlias = entry.ProfileAlias,
        };
        await job.SubmitAsync(req).ConfigureAwait(false);
    }

    private void StartMutation(Func<Task> work, string? msg, bool switchToJobs = false)
    {
        busy = true;
        statusMessage = null;
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
                if (msg is not null)
                {
                    statusMessage = msg;
                }
                if (switchToJobs)
                {
                    navigator?.SwitchToTab('4');
                }
            }
            catch (Exception ex)
            {
                statusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                busy = false;
            }
        });
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

    private IRenderable RenderSpinner(string label)
    {
        spinnerTick++;
        var spinChars = new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' };
        var ch = spinChars[spinnerTick % spinChars.Length];
        var b = Tokens.Tokens.Brand.Value.ToMarkup();
        var s = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        return new Padder(new Rows(new IRenderable[]
        {
            new Markup($"[{b}]{ch}[/] [{s}]{Markup.Escape(label)}[/]"),
        })).Padding(2, 1, 2, 1);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(Math.Max(1, max - 1))] + "…";
}
