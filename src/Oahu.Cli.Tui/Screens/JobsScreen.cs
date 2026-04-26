using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Widgets;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Oahu.Cli.Tui.Screens;

/// <summary>
/// Jobs screen (tab 4). Shows live in-flight jobs, each with current phase,
/// per-phase progress, and an aggregate OSC 9;4 progress emitted to the
/// terminal. Per design TUI-exploration §6.
/// </summary>
public sealed class JobsScreen : ITabScreen, ITerminalProgressProvider
{
    private readonly Func<IJobService> jobServiceFactory;
    private readonly object gate = new();
    private readonly Dictionary<string, JobSnapshot> snapshots = new(StringComparer.Ordinal);
    private readonly List<string> order = new();

    private CancellationTokenSource? observerCts;
    private Task? observerTask;
    private int cursor;
    private string? statusMessage;

    public JobsScreen(Func<IJobService> jobServiceFactory)
    {
        this.jobServiceFactory = jobServiceFactory ?? throw new ArgumentNullException(nameof(jobServiceFactory));
    }

    public string Title => "Jobs";

    public char NumberKey => '4';

    public bool IsLoading => false;

    /// <summary>Always true while observer is running so externally-started jobs render promptly.</summary>
    public bool NeedsTimedRefresh => observerCts is not null && !observerCts.IsCancellationRequested;

    public IEnumerable<KeyValuePair<string, string?>> Hints
    {
        get
        {
            yield return new("↑↓", "navigate");
            yield return new("c", "cancel");
        }
    }

    public IReadOnlyList<JobSnapshot> Snapshots
    {
        get
        {
            lock (gate)
            {
                return order.Select(id => snapshots[id]).ToArray();
            }
        }
    }

    public void OnActivated(IAppShellNavigator navigator)
    {
        if (observerCts is not null)
        {
            return;
        }

        // Seed from ListActive() so jobs that started before the user
        // navigated to this tab are visible immediately.
        try
        {
            var svc = jobServiceFactory();
            var active = svc.ListActive();
            lock (gate)
            {
                snapshots.Clear();
                order.Clear();
                foreach (var s in active)
                {
                    snapshots[s.JobId] = s;
                    order.Add(s.JobId);
                }
            }

            observerCts = new CancellationTokenSource();
            var token = observerCts.Token;
            observerTask = Task.Run(() => ObserveAsync(svc, token), token);
        }
        catch (Exception ex)
        {
            statusMessage = $"Failed to start observer: {ex.Message}";
        }
    }

    public void OnDeactivated()
    {
        StopObserver();
    }

    public void OnShutdown()
    {
        StopObserver();
    }

    private void StopObserver()
    {
        try
        {
            observerCts?.Cancel();
        }
        catch
        {
            // ignore
        }
        observerCts = null;
        observerTask = null;
    }

    private async Task ObserveAsync(IJobService svc, CancellationToken ct)
    {
        try
        {
            await foreach (var update in svc.ObserveAll(ct).WithCancellation(ct).ConfigureAwait(false))
            {
                Apply(svc, update);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on tab deactivate / shutdown
        }
        catch (Exception ex)
        {
            statusMessage = $"Observer error: {ex.Message}";
        }
    }

    private void Apply(IJobService svc, JobUpdate update)
    {
        // JobUpdate carries no Title/Asin — re-fetch a fresh snapshot. Note
        // GetSnapshot may return null after the scheduler has rolled the
        // job to history; in that case keep the last-known snapshot but
        // overlay the terminal phase so the row reflects completion.
        var fresh = svc.GetSnapshot(update.JobId);
        lock (gate)
        {
            if (fresh is not null)
            {
                if (!snapshots.ContainsKey(update.JobId))
                {
                    order.Add(update.JobId);
                }
                snapshots[update.JobId] = fresh;
            }
            else if (snapshots.TryGetValue(update.JobId, out var prev))
            {
                snapshots[update.JobId] = prev with
                {
                    Phase = update.Phase,
                    Progress = update.Progress,
                    Message = update.Message,
                    UpdatedAt = update.Timestamp,
                };
            }
            // else: terminal update for a job we never saw — drop.
        }
    }

#pragma warning disable SA1202 // Render below is grouped with other rendering members.
    public IRenderable Render(int width, int height)
    {
        var primary = Tokens.Tokens.TextPrimary.Value.ToMarkup();
        var secondary = Tokens.Tokens.TextSecondary.Value.ToMarkup();
        var tertiary = Tokens.Tokens.TextTertiary.Value.ToMarkup();
        var brand = Tokens.Tokens.Brand.Value.ToMarkup();

        IReadOnlyList<JobSnapshot> snap;
        lock (gate)
        {
            snap = order.Select(id => snapshots[id]).ToArray();
        }

        var lines = new List<IRenderable>
        {
            new Markup($"[{primary} bold]Jobs[/]  [{secondary}]({snap.Count} active)[/]"),
            new Markup(string.Empty),
        };

        if (!string.IsNullOrEmpty(statusMessage))
        {
            lines.Add(new Markup($"[{tertiary}]{Markup.Escape(statusMessage!)}[/]"));
            lines.Add(new Markup(string.Empty));
        }

        if (snap.Count == 0)
        {
            lines.Add(new Markup($"[{tertiary}]No jobs running. Submit one from the Library or Queue tab.[/]"));
            return new Padder(new Rows(lines)).Padding(2, 1, 2, 1);
        }

        if (cursor >= snap.Count)
        {
            cursor = snap.Count - 1;
        }
        if (cursor < 0)
        {
            cursor = 0;
        }

        for (var i = 0; i < snap.Count; i++)
        {
            var s = snap[i];
            var pointer = i == cursor ? $"[{brand}]❯[/]" : " ";
            var item = new TimelineItem
            {
                Title = Truncate(s.Title, width - 30),
                Description = $"{s.Phase} · {RenderProgressBar(s.Progress)} {FormatPercent(s.Progress)}",
                Detail = s.Message,
                State = MapState(s.Phase),
            };
            lines.Add(new Markup($"  {pointer} "));
            lines.Add(item.Render());
        }

        return new Padder(new Rows(lines)).Padding(2, 0, 2, 0);
    }

    public bool HandleKey(ConsoleKeyInfo key)
    {
        IReadOnlyList<JobSnapshot> snap;
        lock (gate)
        {
            snap = order.Select(id => snapshots[id]).ToArray();
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.K:
                cursor = Math.Max(0, cursor - 1);
                return true;
            case ConsoleKey.DownArrow:
            case ConsoleKey.J:
                cursor = Math.Min(snap.Count - 1, Math.Max(0, cursor + 1));
                return true;
            case ConsoleKey.C when key.Modifiers == 0:
                if (cursor >= 0 && cursor < snap.Count)
                {
                    var id = snap[cursor].JobId;
                    var ok = jobServiceFactory().Cancel(id);
                    statusMessage = ok ? "Cancellation requested." : "Job not found.";
                }
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public string GetTerminalProgressSequence()
    {
        IReadOnlyList<JobSnapshot> snap;
        lock (gate)
        {
            snap = order.Select(id => snapshots[id]).ToArray();
        }

        var hasFailure = false;
        var totals = 0d;
        var count = 0;
        foreach (var s in snap)
        {
            if (s.Phase == JobPhase.Completed || s.Phase == JobPhase.Canceled)
            {
                continue;
            }
            if (s.Phase == JobPhase.Failed)
            {
                hasFailure = true;
                continue;
            }
            totals += AggregateProgress(s.Phase, s.Progress);
            count++;
        }

        if (hasFailure && count == 0)
        {
            return "\u001b]9;4;2;100\u001b\\";
        }
        if (count == 0)
        {
            return AppShell.TerminalProgressClearSequence;
        }

        var pct = (int)Math.Clamp(totals / count * 100, 0, 100);
        var state = hasFailure ? 2 : 1;
        return string.Create(CultureInfo.InvariantCulture, $"\u001b]9;4;{state};{pct}\u001b\\");
    }
#pragma warning restore SA1202

    /// <summary>Phase-weighted aggregate in [0,1] used for OSC 9;4.</summary>
    private static double AggregateProgress(JobPhase phase, double? p)
    {
        var v = Math.Clamp(p ?? 0d, 0d, 1d);
        return phase switch
        {
            JobPhase.Queued => 0d,
            JobPhase.Licensing => 0.05 + 0.10 * v,
            JobPhase.Downloading => 0.15 + 0.55 * v,
            JobPhase.Decrypting => 0.70 + 0.20 * v,
            JobPhase.Muxing => 0.90 + 0.10 * v,
            JobPhase.Completed => 1d,
            _ => 0d,
        };
    }

    private static TimelineState MapState(JobPhase p) => p switch
    {
        JobPhase.Completed => TimelineState.Success,
        JobPhase.Failed => TimelineState.Error,
        JobPhase.Canceled => TimelineState.Warning,
        _ => TimelineState.Loading,
    };

    private static string RenderProgressBar(double? p)
    {
        const int width = 20;
        var v = Math.Clamp(p ?? 0d, 0d, 1d);
        var filled = (int)Math.Round(width * v);
        return new string('█', filled) + new string('░', width - filled);
    }

    private static string FormatPercent(double? p) => p is { } v
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(v * 100),3}%")
        : "  …";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(Math.Max(1, max - 1))] + "…";
}
