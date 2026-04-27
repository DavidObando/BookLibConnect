using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Oahu.Cli.App.Jobs;
using Oahu.Cli.App.Models;
using Oahu.Cli.App.Queue;
using Oahu.Cli.Tui.Screens;
using Oahu.Cli.Tui.Shell;
using Oahu.Cli.Tui.Themes;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;

namespace Oahu.Cli.Tests.Tui;

[Collection("EnvVarSerial")]
public class QueueScreenTests : IDisposable
{
    private readonly string tempFile;

    public QueueScreenTests()
    {
        Theme.Reset();
        tempFile = Path.Combine(Path.GetTempPath(), $"oahu-cli-queue-{Guid.NewGuid():n}.json");
    }

    public void Dispose()
    {
        Theme.Reset();
        if (File.Exists(tempFile))
        {
            File.Delete(tempFile);
        }
    }

    private static QueueEntry E(string asin, string? title = null) =>
        new() { Asin = asin, Title = title ?? $"Book {asin}" };

    private QueueScreen NewScreen(IQueueService q, IJobService? j = null) =>
        new(() => q, () => j ?? new FakeJobService());

    private static async Task WaitForLoad(QueueScreen s)
    {
        // OnActivated kicks off load; pump Render() until it completes.
        for (var i = 0; i < 50; i++)
        {
            // Trigger the completion check.
            s.Render(120, 30);
            if (!s.IsLoading)
            {
                return;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("queue did not finish loading");
    }

    [Fact]
    public async Task OnActivated_Loads_Entries()
    {
        var q = new InMemoryQueueService();
        await q.AddAsync(E("A1"));
        await q.AddAsync(E("A2"));

        var s = NewScreen(q);
        s.OnActivated(new NullNavigator());
        await WaitForLoad(s);

        Assert.Equal(2, s.Entries.Count);
        Assert.Equal(new[] { "A1", "A2" }, s.Entries.Select(e => e.Asin).ToArray());
    }

    [Fact]
    public async Task ShiftDown_Moves_Cursor_Entry_Down()
    {
        var q = new InMemoryQueueService();
        await q.AddAsync(E("A1"));
        await q.AddAsync(E("A2"));
        await q.AddAsync(E("A3"));

        var s = NewScreen(q);
        s.OnActivated(new NullNavigator());
        await WaitForLoad(s);

        Assert.True(s.HandleKey(Key('\0', ConsoleKey.DownArrow, ConsoleModifiers.Shift)));
        for (var i = 0; i < 50 && s.NeedsTimedRefresh; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(new[] { "A2", "A1", "A3" }, (await q.ListAsync()).Select(e => e.Asin).ToArray());
        Assert.Equal(1, s.Cursor);
    }

    [Fact]
    public async Task X_Removes_Selected_Entry()
    {
        var q = new InMemoryQueueService();
        await q.AddAsync(E("A1"));
        await q.AddAsync(E("A2"));

        var s = NewScreen(q);
        s.OnActivated(new NullNavigator());
        await WaitForLoad(s);

        Assert.True(s.HandleKey(Key('x', ConsoleKey.X)));
        for (var i = 0; i < 50 && s.NeedsTimedRefresh; i++)
        {
            await Task.Delay(20);
        }

        Assert.Single(s.Entries);
        Assert.Equal("A2", s.Entries[0].Asin);
    }

    [Fact]
    public async Task Enter_Submits_And_Removes_Then_Switches_To_Jobs()
    {
        var q = new InMemoryQueueService();
        await q.AddAsync(E("A1"));
        var fakeJob = new FakeJobService();
        var nav = new NullNavigator();

        var s = NewScreen(q, fakeJob);
        s.OnActivated(nav);
        await WaitForLoad(s);

        Assert.True(s.HandleKey(Key('\r', ConsoleKey.Enter)));
        for (var i = 0; i < 50 && s.NeedsTimedRefresh; i++)
        {
            await Task.Delay(20);
        }

        Assert.Single(fakeJob.Submitted);
        Assert.Equal("A1", fakeJob.Submitted[0].Asin);
        Assert.Empty(await q.ListAsync());
        Assert.Equal('4', nav.LastSwitch);
    }

    private static ConsoleKeyInfo Key(char ch, ConsoleKey k, ConsoleModifiers mod = 0) =>
        new(ch, k, shift: (mod & ConsoleModifiers.Shift) != 0, alt: (mod & ConsoleModifiers.Alt) != 0, control: (mod & ConsoleModifiers.Control) != 0);
}

public class JobsScreenTests
{
    [Fact]
    public void Seeds_From_ListActive_On_Activation()
    {
        var fake = new FakeJobService();
        fake.SeedActive(new JobSnapshot
        {
            JobId = "j1",
            Asin = "A1",
            Title = "Hello",
            Phase = JobPhase.Downloading,
            Progress = 0.42,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var s = new JobsScreen(() => fake);
        s.OnActivated(new NullNavigator());
        Assert.Single(s.Snapshots);
        Assert.Equal("Hello", s.Snapshots[0].Title);
        s.OnDeactivated();
    }

    [Fact]
    public void Cancel_Key_Calls_JobService_Cancel()
    {
        var fake = new FakeJobService();
        fake.SeedActive(new JobSnapshot
        {
            JobId = "j1",
            Asin = "A1",
            Title = "Hello",
            Phase = JobPhase.Downloading,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var s = new JobsScreen(() => fake);
        s.OnActivated(new NullNavigator());
        s.HandleKey(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false));
        Assert.Equal(new[] { "j1" }, fake.Canceled.ToArray());
        s.OnDeactivated();
    }

    [Fact]
    public void Terminal_Sequence_Is_Clear_When_No_Active()
    {
        var s = new JobsScreen(() => new FakeJobService());
        s.OnActivated(new NullNavigator());
        Assert.Equal(AppShell.TerminalProgressClearSequence, s.GetTerminalProgressSequence());
        s.OnDeactivated();
    }

    [Fact]
    public void Terminal_Sequence_Reports_Aggregate_Progress()
    {
        var fake = new FakeJobService();
        fake.SeedActive(new JobSnapshot
        {
            JobId = "a",
            Asin = "A1",
            Title = "T1",
            Phase = JobPhase.Downloading,
            Progress = 0.5,
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        var s = new JobsScreen(() => fake);
        s.OnActivated(new NullNavigator());

        var seq = s.GetTerminalProgressSequence();
        Assert.StartsWith("\u001b]9;4;1;", seq);
        Assert.EndsWith("\u001b\\", seq);
        s.OnDeactivated();
    }
}

public class HistoryScreenTests
{
    [Fact]
    public async Task OnActivated_Loads_Records_Newest_First()
    {
        var older = new JobRecord
        {
            Id = "j1",
            Asin = "A1",
            Title = "Old",
            TerminalPhase = JobPhase.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt = DateTimeOffset.UtcNow.AddHours(-2),
        };
        var newer = older with { Id = "j2", Asin = "A2", Title = "New", CompletedAt = DateTimeOffset.UtcNow };

        var fake = new FakeJobService();
        fake.SeedHistory(older, newer);

        var s = new HistoryScreen(() => fake);
        s.OnActivated(new NullNavigator());

        for (var i = 0; i < 50; i++)
        {
            s.Render(120, 30);
            if (!s.IsLoading)
            {
                break;
            }
            await Task.Delay(20);
        }

        Assert.Equal(2, s.Records.Count);
        Assert.Equal("New", s.Records[0].Title);
        Assert.Equal("Old", s.Records[1].Title);
    }

    [Fact]
    public async Task R_Resubmits_Selected_Record()
    {
        var rec = new JobRecord
        {
            Id = "j1",
            Asin = "A1",
            Title = "Book",
            TerminalPhase = JobPhase.Failed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Quality = DownloadQuality.High,
        };
        var fake = new FakeJobService();
        fake.SeedHistory(rec);
        var nav = new NullNavigator();

        var s = new HistoryScreen(fake.AsFactory());
        s.OnActivated(nav);
        for (var i = 0; i < 50; i++)
        {
            s.Render(120, 30);
            if (!s.IsLoading)
            {
                break;
            }
            await Task.Delay(20);
        }

        Assert.True(s.HandleKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false)));
        for (var i = 0; i < 50 && fake.Submitted.Count == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.Single(fake.Submitted);
        Assert.Equal("A1", fake.Submitted[0].Asin);
        Assert.Equal('4', nav.LastSwitch);
    }
}

[Collection("EnvVarSerial")]
public class AppShellLifecycleTests : IDisposable
{
    public AppShellLifecycleTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    [Fact]
    public void Switching_Tabs_Calls_OnDeactivated_And_OnActivated()
    {
        var t1 = new LifecycleTabScreen("One", '1');
        var t2 = new LifecycleTabScreen("Two", '2');
        var c = new TestConsole { EmitAnsiSequences = false };
        var shell = new AppShell(c, new AppShellOptions { Tabs = new ITabScreen[] { t1, t2 } });

        shell.SwitchToTab('2');
        Assert.Equal(1, t1.DeactivatedCount);
        Assert.Equal(1, t2.ActivatedCount);

        shell.SwitchToTab('1');
        Assert.Equal(1, t2.DeactivatedCount);
        Assert.Equal(1, t1.ActivatedCount);
    }

    [Fact]
    public void ShowToast_Sets_Toast_Message()
    {
        var c = new TestConsole { EmitAnsiSequences = false };
        var t = new LifecycleTabScreen("One", '1');
        var shell = new AppShell(c, new AppShellOptions { Tabs = new ITabScreen[] { t } });
        // No throw, no public surface — just exercise the call path.
        shell.ShowToast("hello");
    }
}

internal sealed class NullNavigator : IAppShellNavigator
{
    public char? LastSwitch { get; private set; }
    public IModal? LastModal { get; private set; }
    public string? LastToast { get; private set; }
    public bool DismissCalled { get; private set; }
    public Oahu.Cli.Tui.Auth.TuiCallbackBroker? LastBroker { get; private set; }

    public IModal? ActiveModal => LastModal;
    public void SwitchToTab(char numberKey) => LastSwitch = numberKey;
    public void ShowModal(IModal modal) => LastModal = modal;
    public void ShowToast(string message) => LastToast = message;
    public void DismissModal() { DismissCalled = true; LastModal = null; }
    public void SetBroker(Oahu.Cli.Tui.Auth.TuiCallbackBroker? broker) => LastBroker = broker;
}

internal sealed class LifecycleTabScreen : ITabScreen
{
    public LifecycleTabScreen(string title, char numberKey)
    {
        Title = title;
        NumberKey = numberKey;
    }

    public string Title { get; }

    public char NumberKey { get; }

    public bool IsLoading => false;

    public IEnumerable<KeyValuePair<string, string?>> Hints => Array.Empty<KeyValuePair<string, string?>>();

    public int ActivatedCount { get; private set; }

    public int DeactivatedCount { get; private set; }

    public int ShutdownCount { get; private set; }

    public IRenderable Render(int width, int height) => new Markup(Title);

    public bool HandleKey(ConsoleKeyInfo key) => false;

    public void OnActivated(IAppShellNavigator navigator) => ActivatedCount++;

    public void OnDeactivated() => DeactivatedCount++;

    public void OnShutdown() => ShutdownCount++;
}

internal sealed class FakeJobService : IJobService
{
    private readonly List<JobSnapshot> active = new();
    private readonly List<JobRecord> history = new();
    public List<JobRequest> Submitted { get; } = new();
    public List<string> Canceled { get; } = new();

    public void SeedActive(params JobSnapshot[] s) => active.AddRange(s);

    public void SeedHistory(params JobRecord[] r) => history.AddRange(r);

    public Func<IJobService> AsFactory() => () => this;

    public Task SubmitAsync(JobRequest request, System.Threading.CancellationToken ct = default)
    {
        Submitted.Add(request);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<JobUpdate> ObserveAll([System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<JobUpdate> ObserveAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public bool Cancel(string jobId)
    {
        Canceled.Add(jobId);
        return true;
    }

    public JobSnapshot? GetSnapshot(string jobId) => active.FirstOrDefault(s => s.JobId == jobId);

    public IReadOnlyList<JobSnapshot> ListActive() => active.ToArray();

    public async IAsyncEnumerable<JobRecord> ReadHistoryAsync([System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
    {
        foreach (var r in history)
        {
            yield return r;
        }
        await Task.CompletedTask;
    }
}
