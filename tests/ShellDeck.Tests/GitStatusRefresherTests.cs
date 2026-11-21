using System.Collections.Concurrent;
using ShellDeck.Git;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class GitStatusRefresherTests
{
    private const string WindowsRoot = @"C:\src\repo";

    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly ShellProfile PowerShell = new("PowerShell 7", "pwsh.exe", ShellKind.PowerShell, @"C:\Users\me");
    private static readonly GitStatus Known = new("known", "main", false, false, 0, 0);
    private static readonly GitStatus Fresh = new("repo", "dev", false, true, 1, 0);

    private static TerminalTab TabIn(ShellProfile profile, string directory)
    {
        var tab = new TerminalTab(profile);
        tab.MainPane.WorkingDirectory = directory;
        return tab;
    }

    private static GitStatusRefresher Refresher(
        FakeProbe probe,
        Func<string, string?>? findRoot = null,
        Func<bool>? isActive = null,
        TimeSpan? timeout = null,
        Func<TerminalTab, bool>? isOpen = null,
        TimeProvider? time = null) =>
        new(probe, isActive ?? (() => true), findRoot ?? (_ => WindowsRoot), timeout ?? TimeSpan.FromSeconds(30), isOpen, time);

    [Fact]
    public async Task Two_tabs_in_the_same_directory_run_one_probe()
    {
        var probe = new FakeProbe();
        var first = TabIn(Ubuntu, "/home/u/repo");
        var second = TabIn(Ubuntu, "/home/u/repo");

        var refresher = Refresher(probe);
        refresher.Request([first, second]);
        await refresher.WhenIdle();

        Assert.Equal((1, Fresh, Fresh), (probe.CallCount, first.ShownGit, second.ShownGit));
    }

    [Fact]
    public async Task Two_folders_of_a_known_wsl_repo_run_one_probe_on_the_second_cycle()
    {
        var probe = new FakeProbe();
        var first = TabIn(Ubuntu, "/home/u/repo/a");
        var second = TabIn(Ubuntu, "/home/u/repo/b");
        var refresher = Refresher(probe);
        refresher.Request([first, second]);
        await refresher.WhenIdle();
        var afterFirstCycle = probe.CallCount;

        refresher.Request([first, second]);
        await refresher.WhenIdle();

        Assert.Equal((2, 3), (afterFirstCycle, probe.CallCount));
    }

    [Fact]
    public async Task Two_windows_folders_of_one_repo_run_one_probe()
    {
        var probe = new FakeProbe();
        var first = TabIn(PowerShell, @"C:\src\repo\a");
        var second = TabIn(PowerShell, @"C:\src\repo\b");

        var refresher = Refresher(probe);
        refresher.Request([first, second]);
        await refresher.WhenIdle();

        Assert.Equal((1, WindowsRoot, WindowsRoot), (probe.CallCount, probe.Targets[0].KnownTopLevel, probe.Targets[0].Directory));
    }

    [Fact]
    public async Task A_windows_folder_outside_any_repo_runs_no_probe_and_clears_the_state()
    {
        var probe = new FakeProbe();
        var tab = TabIn(PowerShell, @"C:\Users\me");
        tab.ApplyGitStatus(Known);

        var refresher = Refresher(probe, findRoot: _ => null);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal((0, (GitStatus?)null, "Users\\me"), (probe.CallCount, tab.ShownGit, TabText.Of(tab)[1]));
    }

    [Fact]
    public async Task Requests_during_a_cycle_do_not_start_a_second_probe()
    {
        var gates = new[] { new TaskCompletionSource<GitProbeResult>(), new TaskCompletionSource<GitProbeResult>() };
        var probe = new FakeProbe { Respond = call => gates[call.Index].Task };
        var tab = TabIn(Ubuntu, "/home/u/repo");
        var refresher = Refresher(probe);
        refresher.Request([tab]);
        await probe.WaitForCallsAsync(1);

        refresher.Request([tab]);
        refresher.Request([tab]);
        var duringCycle = probe.CallCount;
        gates[0].SetResult(new GitProbeResult.Ok(Known, "/home/u/repo"));
        await probe.WaitForCallsAsync(2);
        gates[1].SetResult(new GitProbeResult.Ok(Fresh, "/home/u/repo"));
        await refresher.WhenIdle();

        Assert.Equal((1, 2, Fresh), (duringCycle, probe.CallCount, tab.ShownGit));
    }

    [Fact]
    public async Task An_inactive_refresher_runs_no_probe()
    {
        var probe = new FakeProbe();
        var tab = TabIn(Ubuntu, "/home/u/repo");
        var refresher = Refresher(probe, isActive: () => false);

        refresher.Request([tab]);
        refresher.RequestAll([tab]);
        refresher.RequestOnActivation([tab]);
        await refresher.WhenIdle();

        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task An_inactive_window_runs_no_probe_until_it_becomes_active()
    {
        var probe = new FakeProbe();
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings(), _ => { }, gitProbe: probe);
        viewModel.Open(Ubuntu);
        var tab = viewModel.Tabs.Single();

        tab.MainPane.WorkingDirectory = "/home/u/repo";
        tab.MainPane.ReportCommandEnded();
        viewModel.RefreshGit();
        var whileInactive = probe.CallCount;
        viewModel.IsWindowActive = true;
        await probe.WaitForCallsAsync(1);

        Assert.Equal((0, 1), (whileInactive, probe.CallCount));
    }

    [Fact]
    public async Task Probes_and_root_lookups_run_off_the_calling_thread()
    {
        var gate = new TaskCompletionSource<GitProbeResult>();
        var probe = new FakeProbe { Respond = _ => gate.Task };
        var rootThreads = new List<int>();
        var tab = TabIn(PowerShell, @"C:\src\repo\a");
        var refresher = Refresher(probe, findRoot: _ =>
        {
            lock (rootThreads)
            {
                rootThreads.Add(Environment.CurrentManagedThreadId);
            }

            return WindowsRoot;
        });
        // A dedicated thread, blocked until the end: unlike a pool thread, it cannot pick up the work the refresher queues.
        var callingThread = 0;
        var returnedBeforeTheEnd = false;
        using var requested = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var caller = new Thread(() =>
        {
            callingThread = Environment.CurrentManagedThreadId;
            refresher.Request([tab]);
            returnedBeforeTheEnd = !refresher.WhenIdle().IsCompleted;
            requested.Set();
            release.Wait(TimeSpan.FromSeconds(30));
        })
        { IsBackground = true };
        caller.Start();

        requested.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await probe.WaitForCallsAsync(1);
        gate.SetResult(new GitProbeResult.Failed());
        await refresher.WhenIdle();
        release.Set();
        caller.Join();

        Assert.Equal((true, false, false), (returnedBeforeTheEnd, probe.ThreadIds.Contains(callingThread), rootThreads.Contains(callingThread)));
    }

    [Fact]
    public async Task A_timeout_keeps_the_last_known_state()
    {
        var probe = new FakeProbe { Respond = _ => new TaskCompletionSource<GitProbeResult>().Task };
        var tab = TabIn(Ubuntu, "/home/u/repo");
        tab.ApplyGitStatus(Known);

        var refresher = Refresher(probe, timeout: TimeSpan.FromMilliseconds(50));
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal(Known, tab.ShownGit);
    }

    [Fact]
    public async Task A_failed_probe_keeps_the_last_known_state()
    {
        var probe = new FakeProbe { Respond = _ => Task.FromResult<GitProbeResult>(new GitProbeResult.Failed()) };
        var tab = TabIn(Ubuntu, "/home/u/repo");
        tab.ApplyGitStatus(Known);

        var refresher = Refresher(probe);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal(Known, tab.ShownGit);
    }

    [Fact]
    public async Task A_probe_that_throws_keeps_the_last_known_state()
    {
        var probe = new FakeProbe { Respond = _ => throw new InvalidOperationException("boom") };
        var tab = TabIn(Ubuntu, "/home/u/repo");
        tab.ApplyGitStatus(Known);

        var refresher = Refresher(probe);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal(Known, tab.ShownGit);
    }

    [Fact]
    public async Task A_timeout_cancels_the_probe_token()
    {
        var probe = new FakeProbe { Respond = call => Task.Delay(Timeout.Infinite, call.Token).ContinueWith<GitProbeResult>(_ => new GitProbeResult.Failed()) };
        var tab = TabIn(Ubuntu, "/home/u/repo");

        var refresher = Refresher(probe, timeout: TimeSpan.FromMilliseconds(50));
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.True(probe.Tokens.Single().IsCancellationRequested);
    }

    [Fact]
    public async Task A_failure_without_known_state_shows_the_current_directory()
    {
        var probe = new FakeProbe { Respond = _ => Task.FromResult<GitProbeResult>(new GitProbeResult.Failed()) };
        var tab = TabIn(Ubuntu, "/home/u/repo");

        var refresher = Refresher(probe);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal("u/repo", TabText.Of(tab)[1]);
    }

    [Fact]
    public async Task Not_a_repo_clears_the_state()
    {
        var probe = new FakeProbe { Respond = _ => Task.FromResult<GitProbeResult>(new GitProbeResult.NotARepo()) };
        var tab = TabIn(Ubuntu, "/home/u/repo");
        tab.ApplyGitStatus(Known);

        var refresher = Refresher(probe);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Null(tab.ShownGit);
    }

    [Fact]
    public async Task A_result_for_an_old_directory_is_dropped()
    {
        var gate = new TaskCompletionSource<GitProbeResult>();
        var probe = new FakeProbe { Respond = _ => gate.Task };
        var tab = TabIn(Ubuntu, "/home/u/repo");
        var refresher = Refresher(probe);
        refresher.Request([tab]);
        await probe.WaitForCallsAsync(1);

        tab.MainPane.WorkingDirectory = "/home/u/elsewhere";
        gate.SetResult(new GitProbeResult.Ok(Fresh, "/home/u/repo"));
        await refresher.WhenIdle();

        Assert.Null(tab.ShownGit);
    }

    [Fact]
    public async Task A_result_for_a_closed_tab_is_dropped()
    {
        var probe = new FakeProbe();
        var tab = TabIn(Ubuntu, "/home/u/repo");

        var refresher = Refresher(probe, isOpen: _ => false);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Null(tab.ShownGit);
    }

    [Fact]
    public async Task A_directory_change_keeps_the_shown_state_until_the_new_result()
    {
        var gate = new TaskCompletionSource<GitProbeResult>();
        var probe = new FakeProbe { Respond = _ => gate.Task };
        var tab = TabIn(Ubuntu, "/home/u/repo");
        tab.ApplyGitStatus(Known);
        var refresher = Refresher(probe);

        tab.MainPane.WorkingDirectory = "/home/u/other";
        refresher.Request([tab]);
        await probe.WaitForCallsAsync(1);
        var whileProbing = tab.ShownGit;
        gate.SetResult(new GitProbeResult.Ok(Fresh, "/home/u/other"));
        await refresher.WhenIdle();

        Assert.Equal((Known, Fresh), (whileProbing, tab.ShownGit));
    }

    [Fact]
    public async Task Activation_is_skipped_within_five_seconds_of_a_full_cycle()
    {
        var probe = new FakeProbe();
        var time = new ManualTime();
        var tab = TabIn(Ubuntu, "/home/u/repo");
        var refresher = Refresher(probe, time: time);
        refresher.RequestAll([tab]);
        await refresher.WhenIdle();

        time.Advance(TimeSpan.FromSeconds(4));
        refresher.RequestOnActivation([tab]);
        await refresher.WhenIdle();
        var within = probe.CallCount;

        time.Advance(TimeSpan.FromSeconds(2));
        refresher.RequestOnActivation([tab]);
        await refresher.WhenIdle();

        Assert.Equal((1, 2), (within, probe.CallCount));
    }

    [Fact]
    public async Task A_partial_cycle_does_not_delay_the_activation_catch_up()
    {
        var probe = new FakeProbe();
        var tab = TabIn(Ubuntu, "/home/u/repo");
        var refresher = Refresher(probe, time: new ManualTime());
        refresher.Request([tab]);
        await refresher.WhenIdle();

        refresher.RequestOnActivation([tab]);
        await refresher.WhenIdle();

        Assert.Equal(2, probe.CallCount);
    }

    [Fact]
    public async Task A_throwing_property_changed_handler_does_not_stop_the_next_cycle()
    {
        var probe = new FakeProbe();
        var tab = TabIn(Ubuntu, "/home/u/repo");
        tab.PropertyChanged += (_, _) => throw new InvalidOperationException("boom");
        var refresher = Refresher(probe);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal(2, probe.CallCount);
    }

    [Fact]
    public async Task A_throwing_root_resolver_does_not_stop_the_next_cycle()
    {
        var probe = new FakeProbe();
        var broken = TabIn(PowerShell, @"C:\broken");
        var good = TabIn(PowerShell, @"C:\src\repo");
        var refresher = Refresher(probe, findRoot: dir => dir == @"C:\broken" ? throw new InvalidOperationException() : WindowsRoot);

        refresher.Request([broken, good]);
        await refresher.WhenIdle();
        refresher.Request([good]);
        await refresher.WhenIdle();

        Assert.Equal((2, (GitStatus?)null, Fresh), (probe.CallCount, broken.ShownGit, good.ShownGit));
    }

    [Fact]
    public async Task A_root_resolver_that_never_returns_is_cut_by_the_timeout_and_the_cycle_ends()
    {
        var probe = new FakeProbe();
        var hang = new ManualResetEventSlim();
        var stuck = TabIn(PowerShell, @"C:\stuck");
        var good = TabIn(Ubuntu, "/home/u/repo");
        var refresher = Refresher(
            probe,
            findRoot: _ =>
            {
                hang.Wait(TimeSpan.FromSeconds(20));
                return WindowsRoot;
            },
            timeout: TimeSpan.FromMilliseconds(200));

        refresher.Request([stuck, good]);
        var finished = await Task.WhenAny(refresher.WhenIdle(), Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        hang.Set();

        Assert.Equal((refresher.WhenIdle(), Fresh, (GitStatus?)null), (finished, good.ShownGit, stuck.ShownGit));
    }

    [Fact]
    public async Task Wsl_directories_differing_only_by_case_are_probed_separately()
    {
        var probe = new FakeProbe();
        var upper = TabIn(Ubuntu, "/home/u/Repo");
        var lower = TabIn(Ubuntu, "/home/u/repo");

        var refresher = Refresher(probe);
        refresher.Request([upper, lower]);
        await refresher.WhenIdle();

        Assert.Equal(2, probe.CallCount);
    }

    [Fact]
    public async Task Windows_directories_differing_only_by_case_share_one_probe()
    {
        var probe = new FakeProbe();
        var first = TabIn(PowerShell, @"C:\src\repo");
        var second = TabIn(PowerShell, @"c:\SRC\repo");

        var refresher = Refresher(probe, findRoot: dir => dir);
        refresher.Request([first, second]);
        await refresher.WhenIdle();

        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task A_request_during_loop_exit_does_not_let_a_third_request_start_a_concurrent_loop()
    {
        var gate = new TaskCompletionSource<GitProbeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new FakeProbe
        {
            Respond = call => call.Index == 0 ? Task.FromResult<GitProbeResult>(new GitProbeResult.Ok(Known, "/home/u/repo")) : gate.Task,
        };
        var tab = TabIn(Ubuntu, "/home/u/repo");
        var refresher = Refresher(probe);
        var context = new QueueContext();

        // Loop A runs one cycle; its end is posted to the queue and the pump leaves it out of the context,
        // so the continuation of RunLoopAsync (its exit) is queued instead of running inline.
        RequestUnder(context, refresher, tab);
        await probe.WaitForCallsAsync(1);
        await context.RunNextAsync();

        // Loop B starts while A has left RunCyclesAsync but not yet finished RunLoopAsync.
        RequestUnder(context, refresher, tab);
        await probe.WaitForCallsAsync(1);
        await context.RunNextAsync();

        // B is still probing: a third request must only queue its tab.
        RequestUnder(context, refresher, tab);
        var concurrent = await probe.TryWaitForCallAsync(TimeSpan.FromSeconds(1));

        gate.SetResult(new GitProbeResult.Ok(Fresh, "/home/u/repo"));
        await context.DrainAsync(() => refresher.WhenIdle().IsCompleted);

        Assert.Equal((false, Fresh), (concurrent, tab.ShownGit));
    }

    [Fact]
    public async Task An_exception_escaping_a_cycle_does_not_stop_the_next_request()
    {
        var probe = new FakeProbe();
        var tab = TabIn(Ubuntu, "/home/u/repo");
        var refresher = Refresher(probe, time: new ThrowingTime());
        refresher.RequestAll([tab]);
        await refresher.WhenIdle();

        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal(2, probe.CallCount);
    }

    private static void RequestUnder(SynchronizationContext context, GitStatusRefresher refresher, TerminalTab tab)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            refresher.Request([tab]);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed record Call(int Index, GitTarget Target, CancellationToken Token);

    /// <summary>
    /// Queues every post and runs it on demand, on the test thread, without installing itself as the current context.
    /// A task that completes inside a queued callback then posts the continuations bound to this context instead of running them inline.
    /// </summary>
    private sealed class QueueContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly SemaphoreSlim _posted = new(0);

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Enqueue((d, state));
            _posted.Release();
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public async Task RunNextAsync()
        {
            if (!await _posted.WaitAsync(TimeSpan.FromSeconds(10)) || !_queue.TryDequeue(out var item))
            {
                throw new TimeoutException("Nothing was posted to the context.");
            }

            item.Callback(item.State);
        }

        public async Task DrainAsync(Func<bool> done)
        {
            while (!done())
            {
                await RunNextAsync();
            }
        }
    }

    private sealed class ThrowingTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("clock");
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan span) => _now += span;
    }

    private sealed class FakeProbe : IGitStatusProbe
    {
        private readonly object _lock = new();
        private readonly SemaphoreSlim _arrivals = new(0);
        private readonly List<GitTarget> _targets = [];
        private readonly List<int> _threadIds = [];
        private readonly List<CancellationToken> _tokens = [];

        public Func<Call, Task<GitProbeResult>> Respond { get; init; } =
            call => Task.FromResult<GitProbeResult>(new GitProbeResult.Ok(Fresh, TopLevelOf(call.Target)));

        public int CallCount => Snapshot(_targets).Count;

        public IReadOnlyList<GitTarget> Targets => Snapshot(_targets);

        public IReadOnlyList<int> ThreadIds => Snapshot(_threadIds);

        public IReadOnlyList<CancellationToken> Tokens => Snapshot(_tokens);

        public Task<GitProbeResult> ProbeAsync(GitTarget target, CancellationToken ct)
        {
            int index;
            lock (_lock)
            {
                index = _targets.Count;
                _targets.Add(target);
                _threadIds.Add(Environment.CurrentManagedThreadId);
                _tokens.Add(ct);
            }

            _arrivals.Release();
            return Respond(new Call(index, target, ct));
        }

        public async Task WaitForCallsAsync(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await _arrivals.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        public Task<bool> TryWaitForCallAsync(TimeSpan timeout) => _arrivals.WaitAsync(timeout);

        private static string TopLevelOf(GitTarget target) => target.KnownTopLevel ?? "/home/u/repo";

        private List<T> Snapshot<T>(List<T> source)
        {
            lock (_lock)
            {
                return [.. source];
            }
        }
    }
}
