using ShellDeck.ViewModels;

namespace ShellDeck.TabLines;

/// <summary>
/// Keeps the fields the tab command provides up to date. Per tab one run at a time: a request that arrives during a run
/// asks for one more run after it. At most a few commands run together, each off the UI thread; the tab's data is
/// captured on the calling (UI) thread and the result applied back on it. Nothing runs while the window is not active.
/// </summary>
public sealed class TabFieldsRefresher
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private const int MaxConcurrentRuns = 3;

    private readonly ITabCommandRunner _runner;
    private readonly Func<IReadOnlyList<string>?> _command;
    private readonly Func<bool> _isActive;
    private readonly Func<TerminalTab, bool> _isOpen;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentRuns);
    private readonly object _gate = new();
    private readonly Dictionary<TerminalTab, bool> _running = [];
    private readonly List<Task> _runs = [];

    public TabFieldsRefresher(
        ITabCommandRunner runner,
        Func<IReadOnlyList<string>?> command,
        Func<bool> isActive,
        Func<TerminalTab, bool>? isOpen = null,
        TimeSpan? timeout = null)
    {
        _runner = runner;
        _command = command;
        _isActive = isActive;
        _isOpen = isOpen ?? (_ => true);
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>Completes when no run is going.</summary>
    public async Task WhenIdle()
    {
        while (true)
        {
            Task[] runs;
            lock (_gate)
            {
                _runs.RemoveAll(run => run.IsCompleted);
                runs = [.. _runs];
            }

            if (runs.Length == 0)
            {
                return;
            }

            await Task.WhenAll(runs);
        }
    }

    public void Request(IEnumerable<TerminalTab> tabs)
    {
        if (_command() is not { Count: > 0 })
        {
            foreach (var tab in tabs)
            {
                tab.SetExtraFields(TabLineContextFields.None);
            }

            return;
        }

        if (!_isActive())
        {
            return;
        }

        foreach (var tab in tabs)
        {
            lock (_gate)
            {
                if (_running.ContainsKey(tab))
                {
                    _running[tab] = true;
                    continue;
                }

                _running[tab] = false;
                _runs.Add(RunAsync(tab));
            }
        }
    }

    private async Task RunAsync(TerminalTab tab)
    {
        try
        {
            while (true)
            {
                var command = _command();
                if (command is not { Count: > 0 } || !_isOpen(tab))
                {
                    return;
                }

                // Only state is read here: no disk, no process.
                var input = TabCommandInput.Build(tab);
                string? output;
                await _slots.WaitAsync();
                try
                {
                    output = await Task.Run(() => _runner.RunAsync(command, input, _timeout, CancellationToken.None));
                }
                finally
                {
                    _slots.Release();
                }

                // A failed run keeps what the tab showed: a slow script must not make the lines flicker.
                if (_isOpen(tab) && ReferenceEquals(command, _command()) && TabCommandOutput.Parse(output) is { } fields)
                {
                    tab.SetExtraFields(fields);
                }

                lock (_gate)
                {
                    if (!_running[tab])
                    {
                        _running.Remove(tab);
                        return;
                    }

                    _running[tab] = false;
                }
            }
        }
        catch (Exception)
        {
            // Unexpected: let the next request start over.
            lock (_gate)
            {
                _running.Remove(tab);
            }
        }
    }
}

public static class TabLineContextFields
{
    public static IReadOnlyDictionary<string, TabSegment> None { get; } = new Dictionary<string, TabSegment>();
}
