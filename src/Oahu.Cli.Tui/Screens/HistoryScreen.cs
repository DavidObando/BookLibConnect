using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Shell;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Screens;

/// <summary>
/// History screen (tab 5). Lists terminal-state jobs from <c>history.jsonl</c>
/// with paginated navigation, retry, and a JSON detail toggle. Per design
/// TUI-exploration §7.
/// </summary>
public sealed class HistoryScreen : ITabScreen
{
    private readonly Func<IJobService> jobServiceFactory;

    private IReadOnlyList<JobRecord> records = Array.Empty<JobRecord>();
    private int cursor;
    private int scrollOffset;
    private int lastListHeight = 20;

    private bool busy;
    private bool jsonMode;
    private string? statusMessage;
    private int spinnerTick;

    private IAppShellNavigator? navigator;

    public HistoryScreen(Func<IJobService> jobServiceFactory)
    {
        this.jobServiceFactory = jobServiceFactory ?? throw new ArgumentNullException(nameof(jobServiceFactory));
    }

    public string Title => "History";

    public char NumberKey => '5';

    public bool NeedsTimedRefresh => busy;

    public IReadOnlyList<JobRecord> Records => records;

    public int Cursor => cursor;

    public IEnumerable<KeyValuePair<string, string?>> Hints
    {
        get
        {
            yield return new("↑↓", "navigate");
            yield return new("PgUp/Dn", "page");
            yield return new("Enter/j", "details");
            yield return new("r", "retry");
            yield return new("Ctrl+R", "reload");
        }
    }

    public Task? OnActivatedAsync(IAppShellNavigator navigator)
    {
        this.navigator = navigator;
        if (records.Count == 0)
        {
            return LoadAsync();
        }
        return null;
    }

    public void OnDeactivated()
    {
    }

    public IRenderable Render(int width, int height)
    {
        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();
        var success = Tokens.Tokens.StatusSuccess.Value.ToMarkup();
        var danger = Tokens.Tokens.StatusError.Value.ToMarkup();
        var warning = Tokens.Tokens.StatusWarning.Value.ToMarkup();

        var lines = new List<IRenderable>
        {
            new Markup($"[{primary} bold]History[/]  [{secondary}]({records.Count} records)[/]"),
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

        if (records.Count == 0)
        {
            lines.Add(new Markup($"[{tertiary}]No history yet. Completed jobs land here.[/]"));
            return new Padder(new Rows(lines)).Padding(2, 1, 2, 1);
        }

        if (jsonMode && cursor < records.Count)
        {
            var rec = records[cursor];
            var json = JsonSerializer.Serialize(rec, new JsonSerializerOptions { WriteIndented = true });
            lines.Add(new Markup($"[{secondary}]{Markup.Escape(rec.Title)}[/]"));
            lines.Add(new Markup(string.Empty));
            foreach (var line in json.Split('\n'))
            {
                lines.Add(new Markup($"[{tertiary}]{Markup.Escape(line.TrimEnd('\r'))}[/]"));
            }
            lines.Add(new Markup(string.Empty));
            lines.Add(new Markup($"[{tertiary}](press j to close)[/]"));
            return new Padder(new Rows(lines)).Padding(2, 0, 2, 0);
        }

        var listHeight = Math.Max(1, height - lines.Count - 2);
        lastListHeight = listHeight;
        AdjustScroll(listHeight);

        var end = Math.Min(scrollOffset + listHeight, records.Count);
        for (var i = scrollOffset; i < end; i++)
        {
            var rec = records[i];
            var isCursor = i == cursor;
            var pointer = isCursor ? $"[{brand}]❯[/]" : " ";
            var style = isCursor ? $"bold {primary}" : secondary;
            var (icon, color) = rec.TerminalPhase switch
            {
                JobPhase.Completed => ("✓", success),
                JobPhase.Failed => ("✗", danger),
                JobPhase.Canceled => ("⊘", warning),
                _ => ("·", tertiary),
            };
            var when = rec.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            lines.Add(new Markup(
                $"  {pointer} [{color}]{icon}[/]  [{style}]{Markup.Escape(Truncate(rec.Title, width - 38))}[/]  [{tertiary}]{when}[/]"));
        }

        if (records.Count > listHeight)
        {
            lines.Add(new Markup($"[{tertiary}]  ↕ {scrollOffset + 1}–{end} of {records.Count}[/]"));
        }

        return new Padder(new Rows(lines)).Padding(2, 0, 2, 0);
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        if (busy)
        {
            return false;
        }

        if (jsonMode)
        {
            if (key.Key is ConsoleKey.J or ConsoleKey.Escape or ConsoleKey.Enter)
            {
                jsonMode = false;
                return true;
            }
            return false;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                cursor = Math.Max(0, cursor - 1);
                return true;
            case ConsoleKey.DownArrow:
                cursor = Math.Min(records.Count - 1, Math.Max(0, cursor + 1));
                return true;
            case ConsoleKey.PageUp:
                cursor = Math.Max(0, cursor - lastListHeight);
                return true;
            case ConsoleKey.PageDown:
                cursor = Math.Min(records.Count - 1, Math.Max(0, cursor + lastListHeight));
                return true;
            case ConsoleKey.Home:
                cursor = 0;
                return true;
            case ConsoleKey.End:
                cursor = Math.Max(0, records.Count - 1);
                return true;
            case ConsoleKey.J:
            case ConsoleKey.Enter:
                if (records.Count > 0)
                {
                    jsonMode = true;
                }
                return true;
            case ConsoleKey.R when (key.Modifiers & ConsoleModifiers.Control) != 0:
                Reload();
                return true;
            case ConsoleKey.R:
                RetrySelected();
                return true;
        }
        return false;
    }

    private Task LoadAsync()
    {
        return Task.Run(async () =>
        {
            try
            {
                var svc = jobServiceFactory();
                var list = new List<JobRecord>();
                await foreach (var r in svc.ReadHistoryAsync().ConfigureAwait(false))
                {
                    list.Add(r);
                }
                // Newest first.
                list.Reverse();
                records = list;
                cursor = 0;
                scrollOffset = 0;
            }
            catch (Exception ex)
            {
                statusMessage = $"Failed to load history: {ex.Message}";
                records = Array.Empty<JobRecord>();
            }
        });
    }

    private void Reload()
    {
        navigator?.TrackLoad(LoadAsync());
    }

    private void RetrySelected()
    {
        if (cursor < 0 || cursor >= records.Count)
        {
            return;
        }
        var rec = records[cursor];
        busy = true;
        statusMessage = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var svc = jobServiceFactory();
                var req = new JobRequest
                {
                    Asin = rec.Asin,
                    Title = rec.Title,
                    Quality = rec.Quality ?? DownloadQuality.High,
                    ProfileAlias = rec.ProfileAlias,
                };
                await svc.SubmitAsync(req).ConfigureAwait(false);
                statusMessage = "Resubmitted with current defaults.";
                navigator?.SwitchToTab('4');
            }
            catch (Exception ex)
            {
                statusMessage = $"Retry failed: {ex.Message}";
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

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(Math.Max(1, max - 1))] + "…";
}
