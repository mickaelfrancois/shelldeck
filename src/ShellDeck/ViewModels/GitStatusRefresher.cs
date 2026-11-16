using System.Collections.Concurrent;
using ShellDeck.Git;
using ShellDeck.Shells;

namespace ShellDeck.ViewModels;

/// <summary>
/// Keeps the git state of the tabs up to date. One cycle at a time: it captures what to probe on the calling (UI) thread, then
/// resolves roots, groups the tabs by repository and probes each repository once, all off that thread. Results are applied back
/// on the calling context, unless the tab moved on meanwhile. Does nothing while the window is not active.
/// </summary>
public sealed class GitStatusRefresher
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>A full refresh asked on window activation is skipped when the last one ended less than this ago.</summary>
    private static readonly TimeSpan ActivationCooldown = TimeSpan.FromSeconds(5);

    private readonly IGitStatusProbe _probe;
    private readonly Func<bool> _isActive;
    private readonly Func<string, string?> _findWindowsRoot;
    private readonly Func<TerminalTab, bool> _isOpen;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<(string Distro, string Directory), string> _wslRoots = new();
    private readonly object _gate = new();
    private readonly HashSet<TerminalTab> _pending = [];
    private bool _pendingFull;
    private bool _running;
    private Task _current = Task.CompletedTask;
    private DateTimeOffset? _lastFullCycleEnd;

    public GitStatusRefresher(
        IGitStatusProbe probe,
        Func<bool> isActive,
        Func<string, string?> findWindowsRoot,
        TimeSpan? timeout = null,
        Func<TerminalTab, bool>? isOpen = null,
        TimeProvider? time = null)
    {
        _probe = probe;
        _isActive = isActive;
        _findWindowsRoot = findWindowsRoot;
        _timeout = timeout ?? DefaultTimeout;
        _isOpen = isOpen ?? (_ => true);
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Completes when no cycle is running and none is waiting.</summary>
    public Task WhenIdle() => _current;

    /// <summary>Refreshes these tabs (a directory changed, a command ended).</summary>
    public void Request(IEnumerable<TerminalTab> tabs) => Enqueue(tabs, full: false);

    /// <summary>Refreshes every tab given (the periodic tick).</summary>
    public void RequestAll(IEnumerable<TerminalTab> tabs) => Enqueue(tabs, full: true);

    /// <summary>The window became active: catches up on what was missed, unless a full refresh just ended.</summary>
    public void RequestOnActivation(IEnumerable<TerminalTab> tabs)
    {
        lock (_gate)
        {
            if (_lastFullCycleEnd is { } end && _time.GetUtcNow() - end < ActivationCooldown)
            {
                return;
            }
        }

        Enqueue(tabs, full: true);
    }

    private void Enqueue(IEnumerable<TerminalTab> tabs, bool full)
    {
        if (!_isActive())
        {
            return;
        }

        lock (_gate)
        {
            _pending.UnionWith(tabs);
            _pendingFull |= full;
            if (_running)
            {
                return;
            }

            _running = true;
        }

        _current = RunLoopAsync();
    }

    private async Task RunLoopAsync()
    {
        try
        {
            await RunCyclesAsync();
        }
        catch (Exception)
        {
            // Unexpected: keep the refresher alive, the next request starts a new cycle.
        }
        finally
        {
            lock (_gate)
            {
                _running = false;
            }
        }
    }

    private async Task RunCyclesAsync()
    {
        while (true)
        {
            List<Capture> captures;
            bool full;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _running = false;
                    return;
                }

                var tabs = _pending.ToList();
                _pending.Clear();
                full = _pendingFull;
                _pendingFull = false;

                // Only state is read here (UI thread): no disk, no process.
                captures = CaptureState(tabs);
            }

            List<(List<Capture> Members, GitProbeResult Result)> results;
            try
            {
                results = await Task.Run(() => ProbeAllAsync(captures));
            }
            catch (Exception)
            {
                results = [];
            }

            foreach (var (members, result) in results)
            {
                Apply(members, result);
            }

            if (full)
            {
                lock (_gate)
                {
                    _lastFullCycleEnd = _time.GetUtcNow();
                }
            }
        }
    }

    private static List<Capture> CaptureState(IEnumerable<TerminalTab> tabs)
    {
        var captures = new List<Capture>();
        foreach (var tab in tabs)
        {
            try
            {
                var pane = tab.MainPane;
                var profile = pane.Profile;
                if (profile.Kind is ShellKind.Ssh || pane.WorkingDirectory is not { } directory
                    || !TerminalPane.IsUsableDirectory(directory, profile))
                {
                    continue;
                }

                captures.Add(new Capture(tab, pane, profile, directory));
            }
            catch (Exception)
            {
                // One broken tab must not stop the others.
            }
        }

        return captures;
    }

    private async Task<List<(List<Capture> Members, GitProbeResult Result)>> ProbeAllAsync(List<Capture> captures)
    {
        var results = new List<(List<Capture>, GitProbeResult)>();
        // Ordinal: Linux paths are case-sensitive. Windows roots are normalized in their key below.
        var groups = new Dictionary<string, (GitTarget Target, List<Capture> Members)>(StringComparer.Ordinal);
        var windowsRoots = await ResolveWindowsRootsAsync(captures);

        foreach (var capture in captures)
        {
            string key;
            GitTarget target;
            if (capture.Profile.Kind is ShellKind.Wsl)
            {
                var distro = capture.Profile.Name;
                key = _wslRoots.TryGetValue((distro, capture.Directory), out var known)
                    ? $"wsl\n{distro}\nroot\n{known}"
                    : $"wsl\n{distro}\ndir\n{capture.Directory}";
                target = new GitTarget(capture.Profile, capture.Directory, null);
            }
            else
            {
                // Resolution failed or timed out: keep the known state.
                if (!windowsRoots.TryGetValue(capture, out var root))
                {
                    continue;
                }

                if (root is null)
                {
                    results.Add(([capture], new GitProbeResult.NotARepo()));
                    continue;
                }

                key = $"win\n{root.ToUpperInvariant()}";
                target = new GitTarget(capture.Profile, root, root);
            }

            if (groups.TryGetValue(key, out var group))
            {
                group.Members.Add(capture);
            }
            else
            {
                groups[key] = (target, [capture]);
            }
        }

        var probes = groups.Values.Select(async group => (group.Members, Result: await ProbeAsync(group.Target))).ToList();
        foreach (var (members, result) in await Task.WhenAll(probes))
        {
            LearnRoots(members, result);
            results.Add((members, result));
        }

        return results;
    }

    /// <summary>Resolves the root of every Windows capture in parallel, bounded by the timeout. A capture that failed or timed out is absent.</summary>
    private async Task<Dictionary<Capture, string?>> ResolveWindowsRootsAsync(List<Capture> captures)
    {
        var resolutions = captures
            .Where(capture => capture.Profile.Kind is not ShellKind.Wsl)
            .Select(async capture =>
            {
                try
                {
                    var root = await Task.Run(() => _findWindowsRoot(capture.Directory)).WaitAsync(_timeout);
                    return (Capture: capture, Resolved: true, Root: root);
                }
                catch (Exception)
                {
                    return (Capture: capture, Resolved: false, Root: (string?)null);
                }
            })
            .ToList();

        var roots = new Dictionary<Capture, string?>();
        foreach (var (capture, resolved, root) in await Task.WhenAll(resolutions))
        {
            if (resolved)
            {
                roots[capture] = root;
            }
        }

        return roots;
    }

    private async Task<GitProbeResult> ProbeAsync(GitTarget target)
    {
        using var cancellation = new CancellationTokenSource(_timeout);
        try
        {
            // WaitAsync also frees us from a probe that ignores its token.
            return await _probe.ProbeAsync(target, cancellation.Token).WaitAsync(cancellation.Token);
        }
        catch (Exception)
        {
            return new GitProbeResult.Failed();
        }
    }

    private void LearnRoots(List<Capture> members, GitProbeResult result)
    {
        foreach (var member in members.Where(member => member.Profile.Kind is ShellKind.Wsl))
        {
            var key = (member.Profile.Name, member.Directory);
            switch (result)
            {
                case GitProbeResult.Ok ok:
                    _wslRoots[key] = ok.TopLevel;
                    break;
                case GitProbeResult.NotARepo:
                    _wslRoots.TryRemove(key, out _);
                    break;
            }
        }
    }

    private void Apply(List<Capture> members, GitProbeResult result)
    {
        foreach (var member in members)
        {
            // The tab may have been closed, its main pane changed or the shell moved while the probe ran.
            if (!_isOpen(member.Tab) || member.Tab.MainPane != member.Pane || member.Pane.WorkingDirectory != member.Directory)
            {
                continue;
            }

            try
            {
                switch (result)
                {
                    case GitProbeResult.Ok ok:
                        member.Tab.ApplyGitStatus(ok.Status);
                        break;
                    case GitProbeResult.NotARepo:
                        member.Tab.ApplyGitStatus(null);
                        break;
                }
            }
            catch (Exception)
            {
                // A faulty PropertyChanged handler must not freeze the refresher.
            }
        }
    }

    private sealed record Capture(TerminalTab Tab, TerminalPane Pane, ShellProfile Profile, string Directory);
}
