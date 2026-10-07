using System.Windows;
using ShellDeck.Shells;

namespace ShellDeck.ViewModels;

/// <summary>
/// One shell inside a tab. The tab's main pane drives the tab's title, status and notifications;
/// the other panes are side shells, e.g. to run a command while Claude Code works in the main one.
/// </summary>
public sealed class TerminalPane : ObservableObject, IPaneNode
{
    private readonly TerminalTab _owner;
    private bool _isActive;
    private Rect _bounds = new(0, 0, 1, 1);
    private Func<bool> _hasRunningCommand = () => false;

    internal TerminalPane(TerminalTab owner, ShellProfile profile)
    {
        _owner = owner;
        Profile = profile;
    }

    public ShellProfile Profile { get; }

    public bool IsMain => _owner.MainPane == this;

    /// <summary>The pane that has the keyboard when its tab is shown.</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetField(ref _isActive, value);
    }

    /// <summary>Position in the tab's terminal area, in fractions of its size.</summary>
    public Rect Bounds
    {
        get => _bounds;
        private set => SetField(ref _bounds, value);
    }

    public bool IsExited { get; private set; }

    public int ExitCode { get; private set; }

    /// <summary>The last title the shell set, kept so a side pane can take over as main pane.</summary>
    internal string TerminalTitle { get; private set; } = string.Empty;

    /// <summary>True when closing the pane would interrupt something; Claude Code only counts in the main pane.</summary>
    public bool HasActivity => !IsExited && (_hasRunningCommand() || IsMain && _owner.IsClaudeSession);

    public void TrackRunningCommands(Func<bool> hasRunningCommand) => _hasRunningCommand = hasRunningCommand;

    public void ApplyTerminalTitle(string title)
    {
        TerminalTitle = title;
        if (IsMain)
        {
            _owner.ApplyTerminalTitle(title);
        }
    }

    public void ReceiveBell()
    {
        if (IsMain)
        {
            _owner.ReceiveBell();
        }
    }

    public void ReceiveNotification(string message)
    {
        if (IsMain)
        {
            _owner.ReceiveNotification(message);
        }
    }

    public void MarkExited(int exitCode, TimeSpan lifetime)
    {
        IsExited = true;
        ExitCode = exitCode;
        _owner.OnPaneExited(this, exitCode, lifetime);
    }

    /// <summary>The user clicked into the pane.</summary>
    public void RequestActivation() => _owner.Activate(this);

    void IPaneNode.Arrange(Rect area) => Bounds = area;
}
