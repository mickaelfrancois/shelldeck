namespace ShellDeck.Session;

/// <summary>A one-shot timer that restarts each time it is asked to; the owner decides which thread it ticks on.</summary>
public interface IDebounceTimer
{
    /// <summary>(Re)starts the countdown: a pending tick is pushed back.</summary>
    void Restart();

    void Stop();
}

/// <summary>
/// Saves the layout once the changes have calmed down: many <see cref="Request"/> in a row give one save. The timer is
/// injected, so the class has no WPF in it and the delay is testable.
/// </summary>
public sealed class LayoutSaver
{
    private readonly Action _save;
    private readonly IDebounceTimer _timer;
    private bool _pending;
    private bool _stopped;

    /// <param name="save">Writes the current layout.</param>
    /// <param name="createTimer">Builds the timer, given the callback to run when the delay has elapsed.</param>
    public LayoutSaver(Action save, Func<Action, IDebounceTimer> createTimer)
    {
        _save = save;
        _timer = createTimer(OnElapsed);
    }

    /// <summary>The layout changed: it will be saved once the delay passes without another request.</summary>
    public void Request()
    {
        if (_stopped)
        {
            return;
        }

        _pending = true;
        _timer.Restart();
    }

    /// <summary>Saves now, whatever is pending.</summary>
    public void Flush()
    {
        if (_stopped)
        {
            return;
        }

        _timer.Stop();
        _pending = false;
        _save();
    }

    /// <summary>Nothing is saved after this, whatever happens to the layout (the window is closing and tears the tabs down).</summary>
    public void Stop()
    {
        _stopped = true;
        _pending = false;
        _timer.Stop();
    }

    private void OnElapsed()
    {
        _timer.Stop();
        if (_stopped || !_pending)
        {
            return;
        }

        _pending = false;
        _save();
    }
}
