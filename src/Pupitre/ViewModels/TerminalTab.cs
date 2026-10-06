using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Pupitre.Shells;
using Pupitre.Terminal;

namespace Pupitre.ViewModels;

public enum TerminalStatus
{
    Running,
    Working,
    Attention,
    Exited,
}

/// <summary>
/// A tab: one or more panes laid out by splits. Its title, status and notifications follow the main pane
/// (typically Claude Code); the other panes are side shells.
/// </summary>
public sealed class TerminalTab : ObservableObject
{
    private const string WorkingSubtitle = "Claude Code travaille…";
    private const int SuccessExitCode = 0;
    private static readonly TimeSpan StartupFailureWindow = TimeSpan.FromSeconds(3);
    private const string ClaudeWaitingMessage = "Claude attend votre réponse";
    private const string BellMessage = "Signal du terminal";
    private static readonly Rect WholeArea = new(0, 0, 1, 1);

    // Share of a split a divider moves per keystroke.
    private const double DividerStep = 0.05;

    // Bounds are sums of fractions: edges that meet can differ by rounding.
    private const double EdgeTolerance = 1e-9;

    private string _title;
    private bool _isSelected;
    private bool _isClaudeSession;
    private TerminalStatus _status = TerminalStatus.Running;
    private string? _notification;
    private int _exitCode;
    private IPaneNode _root;
    private TerminalPane _mainPane;
    private TerminalPane _activePane;

    public TerminalTab(ShellProfile profile)
    {
        _title = profile.Name;
        _mainPane = _activePane = new TerminalPane(this, profile) { IsActive = true };
        _root = _mainPane;
        Panes.Add(_mainPane);
    }

    public ObservableCollection<TerminalPane> Panes { get; } = [];

    public ObservableCollection<PaneSplit> Splits { get; } = [];

    public bool IsSplit => Panes.Count > 1;

    public TerminalPane MainPane
    {
        get => _mainPane;
        private set
        {
            if (SetField(ref _mainPane, value))
            {
                OnPropertyChanged(nameof(Profile));
            }
        }
    }

    public TerminalPane ActivePane => _activePane;

    public ShellProfile Profile => MainPane.Profile;

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                AcknowledgeIfWatched();
            }
        }
    }

    public bool IsClaudeSession
    {
        get => _isClaudeSession;
        private set => SetField(ref _isClaudeSession, value);
    }

    public TerminalStatus Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    public string? Notification
    {
        get => _notification;
        private set
        {
            if (SetField(ref _notification, value))
            {
                OnPropertyChanged(nameof(Subtitle));
            }
        }
    }

    public string Subtitle => Status switch
    {
        TerminalStatus.Attention => Notification ?? string.Empty,
        TerminalStatus.Working => WorkingSubtitle,
        TerminalStatus.Exited => $"Processus terminé (code {_exitCode})",
        _ when Profile.Kind is ShellKind.Ssh => $"SSH · {Profile.Location}",
        _ => $"{Profile.Name} · {Profile.Location}",
    };

    /// <summary>
    /// True when closing the tab would interrupt something: Claude Code (even idle, its conversation would be lost)
    /// or a command started from one of its shells.
    /// </summary>
    public bool HasActivity => Panes.Any(pane => pane.HasActivity);

    /// <summary>
    /// The user sees the main pane only when its tab is shown and it has the keyboard: while they type in a side pane,
    /// Claude Code asking for them still deserves a notification.
    /// </summary>
    private bool IsMainPaneWatched => IsSelected && ActivePane == MainPane;

    /// <summary>Raised when the main pane needs the user while they are not looking at it.</summary>
    public event EventHandler? AttentionRequested;

    /// <summary>Raised when the last shell of the tab ends after being used (e.g. <c>exit</c>): nothing is left to show.</summary>
    public event EventHandler? Ended;

    public void TrackRunningCommands(Func<bool> hasRunningCommand) => MainPane.TrackRunningCommands(hasRunningCommand);

    public void ApplyTerminalTitle(string title)
    {
        // ConPTY's default title is the executable path, which says less than the profile name.
        Title = string.IsNullOrWhiteSpace(title) || IsExecutablePath(title) ? Profile.Name : title;
        IsClaudeSession = ClaudeCodeTitle.IsClaudeCode(title);

        if (Status is TerminalStatus.Exited or TerminalStatus.Attention)
        {
            return;
        }

        var wasWorking = Status is TerminalStatus.Working;
        Status = ClaudeCodeTitle.IsWorking(title) ? TerminalStatus.Working : TerminalStatus.Running;

        // Claude Code stops working either because it finished or because it needs an answer: both need the user.
        if (wasWorking && Status is TerminalStatus.Running)
        {
            ReceiveNotification(ClaudeWaitingMessage);
        }
    }

    /// <summary>A BEL from the terminal: Claude Code rings it when it waits for the user (terminal_bell channel).</summary>
    public void ReceiveBell() =>
        ReceiveNotification(IsClaudeSession ? ClaudeWaitingMessage : BellMessage);

    public void ReceiveNotification(string message)
    {
        if (IsMainPaneWatched || Status is TerminalStatus.Exited)
        {
            return;
        }

        Notification = message;
        Status = TerminalStatus.Attention;
        AttentionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The main pane's shell ended.</summary>
    public void MarkExited(int exitCode, TimeSpan lifetime) => MainPane.MarkExited(exitCode, lifetime);

    /// <summary>Splits the active pane; the new pane runs the same profile and takes the keyboard.</summary>
    public void Split(SplitDirection direction)
    {
        var pane = new TerminalPane(this, ActivePane.Profile);
        var split = new PaneSplit(direction, ActivePane, pane);
        split.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaneSplit.Ratio))
            {
                Arrange();
            }
        };

        _root = Replace(_root, ActivePane, split);
        Arrange();
        Splits.Add(split);
        Panes.Add(pane);
        OnPropertyChanged(nameof(IsSplit));
        Activate(pane);
    }

    /// <summary>Closes one pane of a split tab; its neighbor takes its place. A side pane becomes main if needed.</summary>
    public void ClosePane(TerminalPane pane)
    {
        if (!IsSplit || !Panes.Contains(pane))
        {
            return;
        }

        var successor = FirstPane(Sibling(_root, pane) ?? _root);
        _root = Remove(_root, pane) ?? _root;
        Panes.Remove(pane);
        Arrange();
        OnPropertyChanged(nameof(IsSplit));

        if (pane == MainPane)
        {
            PromoteToMain(Panes[0]);
        }

        if (pane == ActivePane)
        {
            Activate(successor);
        }
    }

    public void Activate(TerminalPane pane)
    {
        if (pane == _activePane || !Panes.Contains(pane))
        {
            return;
        }

        _activePane.IsActive = false;
        _activePane = pane;
        pane.IsActive = true;
        OnPropertyChanged(nameof(ActivePane));
        AcknowledgeIfWatched();
    }

    public void MoveFocus(PaneDirection direction)
    {
        if (FindNeighbor(ActivePane, direction) is { } neighbor)
        {
            Activate(neighbor);
        }
    }

    /// <summary>
    /// Moves the divider of the active pane in <paramref name="direction"/>, as Windows Terminal's Alt+Shift+arrows:
    /// the closest split across that axis, so the pane grows or shrinks depending on which side of it the divider is.
    /// </summary>
    public void MoveDivider(PaneDirection direction)
    {
        var axis = direction is PaneDirection.Left or PaneDirection.Right ? SplitDirection.Right : SplitDirection.Down;
        if (InnermostSplit(_root, ActivePane, axis) is { } split)
        {
            split.Ratio += direction is PaneDirection.Left or PaneDirection.Up ? -DividerStep : DividerStep;
        }
    }

    /// <summary>
    /// The pane next to <paramref name="from"/> in <paramref name="direction"/>, or null at the edge of the tab.
    /// Each pane's <see cref="TerminalPane.Bounds"/> gives its place, in fractions of the terminal area.
    /// </summary>
    internal TerminalPane? FindNeighbor(TerminalPane from, PaneDirection direction)
    {
        // A neighbor touches the edge of the pane on that side and runs alongside it; among several
        // (e.g. two panes stacked on the right), the one sharing the longest edge wins, as in Windows Terminal.
        var origin = from.Bounds;
        return Panes
            .Where(pane => pane != from)
            .Select(pane => (Pane: pane, Overlap: SharedEdge(origin, pane.Bounds, direction)))
            .Where(candidate => candidate.Overlap > EdgeTolerance)
            .OrderByDescending(candidate => candidate.Overlap)
            .Select(candidate => candidate.Pane)
            .FirstOrDefault();
    }

    /// <summary>Length of the edge <paramref name="other"/> shares with <paramref name="origin"/> on that side, or 0.</summary>
    private static double SharedEdge(Rect origin, Rect other, PaneDirection direction)
    {
        var touches = direction switch
        {
            PaneDirection.Left => IsClose(other.Right, origin.Left),
            PaneDirection.Right => IsClose(other.Left, origin.Right),
            PaneDirection.Up => IsClose(other.Bottom, origin.Top),
            _ => IsClose(other.Top, origin.Bottom),
        };
        if (!touches)
        {
            return 0;
        }

        return direction is PaneDirection.Left or PaneDirection.Right
            ? Math.Min(origin.Bottom, other.Bottom) - Math.Max(origin.Top, other.Top)
            : Math.Min(origin.Right, other.Right) - Math.Max(origin.Left, other.Left);
    }

    private static bool IsClose(double a, double b) => Math.Abs(a - b) < EdgeTolerance;

    internal void OnPaneExited(TerminalPane pane, int exitCode, TimeSpan lifetime)
    {
        // A shell that fails right after starting (unknown distribution, refused SSH connection) stays open
        // so the user can read why. Any other exit closes it: `exit` returns the status of the last command,
        // so a non-zero code alone does not mean the shell failed.
        var failedAtStartup = exitCode != SuccessExitCode && lifetime < StartupFailureWindow;

        if (pane == MainPane)
        {
            _exitCode = exitCode;
            Notification = null;
            Status = TerminalStatus.Exited;
        }

        if (failedAtStartup)
        {
            return;
        }

        if (IsSplit)
        {
            ClosePane(pane);
        }
        else
        {
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PromoteToMain(TerminalPane pane)
    {
        MainPane = pane;
        _exitCode = 0;
        Notification = null;
        IsClaudeSession = false;
        Status = TerminalStatus.Running;
        ApplyTerminalTitle(pane.TerminalTitle);
        if (pane.IsExited)
        {
            Status = TerminalStatus.Exited;
        }
    }

    private void Arrange() => _root.Arrange(WholeArea);

    private void AcknowledgeIfWatched()
    {
        if (!IsMainPaneWatched || Status is not TerminalStatus.Attention)
        {
            return;
        }

        Notification = null;
        Status = ClaudeCodeTitle.IsWorking(Title) ? TerminalStatus.Working : TerminalStatus.Running;
    }

    private static bool IsExecutablePath(string title) =>
        Path.IsPathFullyQualified(title) && title.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>The deepest split along <paramref name="axis"/> that holds <paramref name="pane"/>.</summary>
    private static PaneSplit? InnermostSplit(IPaneNode node, TerminalPane pane, SplitDirection axis)
    {
        if (node is not PaneSplit split)
        {
            return null;
        }

        var side = Contains(split.First, pane) ? split.First : Contains(split.Second, pane) ? split.Second : null;
        if (side is null)
        {
            return null;
        }

        return InnermostSplit(side, pane, axis) ?? (split.Direction == axis ? split : null);
    }

    private static bool Contains(IPaneNode node, TerminalPane pane) =>
        node == pane || node is PaneSplit split && (Contains(split.First, pane) || Contains(split.Second, pane));

    private static TerminalPane FirstPane(IPaneNode node) => node switch
    {
        PaneSplit split => FirstPane(split.First),
        _ => (TerminalPane)node,
    };

    /// <summary>The node sharing a split with <paramref name="pane"/>: it fills the space the pane leaves.</summary>
    private static IPaneNode? Sibling(IPaneNode node, TerminalPane pane) => node switch
    {
        PaneSplit split when split.First == pane => split.Second,
        PaneSplit split when split.Second == pane => split.First,
        PaneSplit split => Sibling(split.First, pane) ?? Sibling(split.Second, pane),
        _ => null,
    };

    private static IPaneNode Replace(IPaneNode node, TerminalPane target, IPaneNode replacement)
    {
        if (node == target)
        {
            return replacement;
        }

        if (node is PaneSplit split)
        {
            split.First = Replace(split.First, target, replacement);
            split.Second = Replace(split.Second, target, replacement);
        }

        return node;
    }

    private IPaneNode? Remove(IPaneNode node, TerminalPane pane)
    {
        if (node == pane)
        {
            return null;
        }

        if (node is not PaneSplit split)
        {
            return node;
        }

        var first = Remove(split.First, pane);
        var second = Remove(split.Second, pane);
        if (first is null || second is null)
        {
            // The split lost a side: the other side takes its whole area.
            Splits.Remove(split);
            return first ?? second;
        }

        (split.First, split.Second) = (first, second);
        return split;
    }
}
