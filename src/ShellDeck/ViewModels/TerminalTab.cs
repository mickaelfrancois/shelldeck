using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using ShellDeck.Session;
using ShellDeck.Shells;
using ShellDeck.Terminal;

namespace ShellDeck.ViewModels;

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

    private static readonly Regex HexColor = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private string _title;
    private bool _isSelected;
    private bool _isDragged;
    private bool _isClaudeSession;
    private TabColor _color = TabColor.None;
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

    /// <summary>Held by the mouse in the tab list, while it is being moved to a new place.</summary>
    public bool IsDragged
    {
        get => _isDragged;
        set => SetField(ref _isDragged, value);
    }

    /// <summary>Chosen by the user in the tab list; lives as long as the tab.</summary>
    public TabColor Color
    {
        get => _color;
        set
        {
            if (SetField(ref _color, value))
            {
                LayoutChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Raised when something session.json keeps changes: panes, splits, ratios, color, active or main pane, a pane's directory.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Raised when Claude Code finishes a turn in one of the panes (main or not).</summary>
    public event EventHandler<TerminalPane>? ClaudeTurnEnded;

    internal void OnClaudeTurnEnded(TerminalPane pane) => ClaudeTurnEnded?.Invoke(this, pane);

    internal void OnPaneLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

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

    /// <summary>
    /// Splits the active pane; the new pane runs <paramref name="profile"/> (the active pane's own profile when null)
    /// and takes the keyboard. The tab header keeps following the main pane.
    /// </summary>
    public void Split(SplitDirection direction, ShellProfile? profile = null)
    {
        var pane = AddSplit(ActivePane, direction, profile ?? ActivePane.Profile, out _);
        ActivateCore(pane);
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Splits <paramref name="target"/>: the new pane takes the second place. Neither is activated, nothing is raised.</summary>
    private TerminalPane AddSplit(TerminalPane target, SplitDirection direction, ShellProfile profile, out PaneSplit split)
    {
        var pane = new TerminalPane(this, profile);
        var created = new PaneSplit(direction, target, pane);
        created.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaneSplit.Ratio))
            {
                Arrange();
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
        };

        _root = Replace(_root, target, created);
        Arrange();
        Splits.Add(created);
        Panes.Add(pane);
        OnPropertyChanged(nameof(IsSplit));
        split = created;
        return pane;
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
        pane.EndSession();
        Arrange();
        OnPropertyChanged(nameof(IsSplit));

        if (pane == MainPane)
        {
            PromoteToMain(Panes[0]);
        }

        if (pane == ActivePane)
        {
            ActivateCore(successor);
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The tab is closed: all its shells end.</summary>
    internal void EndSessions()
    {
        foreach (var pane in Panes)
        {
            pane.EndSession();
        }
    }

    public void Activate(TerminalPane pane)
    {
        if (ActivateCore(pane))
        {
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool ActivateCore(TerminalPane pane)
    {
        if (pane == _activePane || !Panes.Contains(pane))
        {
            return false;
        }

        _activePane.IsActive = false;
        _activePane = pane;
        pane.IsActive = true;
        OnPropertyChanged(nameof(ActivePane));
        AcknowledgeIfWatched();
        return true;
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

    /// <summary>A non-zero exit right after the start: the shell could not start (unknown distribution, missing directory).</summary>
    public static bool IsStartupFailure(int exitCode, TimeSpan lifetime) =>
        exitCode != SuccessExitCode && lifetime < StartupFailureWindow;

    internal void OnPaneExited(TerminalPane pane, int exitCode, TimeSpan lifetime)
    {
        // A shell that fails right after starting (unknown distribution, refused SSH connection) stays open
        // so the user can read why. Any other exit closes it: `exit` returns the status of the last command,
        // so a non-zero code alone does not mean the shell failed.
        var failedAtStartup = IsStartupFailure(exitCode, lifetime);

        // The exit may be reported just after the pane was closed, before its view is unloaded.
        if (!Panes.Contains(pane))
        {
            return;
        }

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

    /// <summary>The tab as saved in session.json; pane indices follow <see cref="Leaves"/>.</summary>
    public TabLayout ToLayout()
    {
        var leaves = Leaves(_root).ToList();
        return new TabLayout(
            NodeLayout(_root),
            Color.Hex is null ? null : Color.Name,
            Color.Hex,
            Math.Max(0, leaves.IndexOf(MainPane)),
            Math.Max(0, leaves.IndexOf(ActivePane)));
    }

    /// <summary>
    /// Rebuilds a tab from a saved layout. A pane whose profile <paramref name="resolve"/> does not know takes
    /// <paramref name="fallback"/>; with no fallback it is left out, and so is a tab with no pane left (null).
    /// Out-of-range indices and ratios are brought back to something valid.
    /// </summary>
    public static TerminalTab? Restore(TabLayout layout, Func<string, ShellProfile?> resolve, ShellProfile? fallback)
    {
        var index = 0;
        if (Resolve(layout.Root, resolve, fallback, ref index) is not { } root)
        {
            return null;
        }

        var tab = new TerminalTab(FirstLeaf(root).Profile);
        var leaves = new List<(TerminalPane Pane, int Index)>();
        tab.Populate(root, tab.MainPane, leaves);

        var main = leaves.FirstOrDefault(leaf => leaf.Index == layout.MainPane, leaves[0]).Pane;
        var active = leaves.FirstOrDefault(leaf => leaf.Index == layout.ActivePane, leaves[0]).Pane;
        if (main != tab.MainPane)
        {
            tab.PromoteToMain(main);
        }

        tab.ActivateCore(active);
        tab.Color = ColorOf(layout);
        return tab;
    }

    private static TabColor ColorOf(TabLayout layout)
    {
        if (layout.ColorHex is not { } hex || !HexColor.IsMatch(hex))
        {
            return TabColor.None;
        }

        return TabColor.Palette.FirstOrDefault(color => string.Equals(color.Hex, hex, StringComparison.OrdinalIgnoreCase))
            ?? new TabColor(string.IsNullOrWhiteSpace(layout.ColorName) ? TabColor.Custom(hex).Name : layout.ColorName, hex);
    }

    private void Populate(RestoredNode node, TerminalPane pane, List<(TerminalPane, int)> leaves)
    {
        if (node is RestoredLeaf leaf)
        {
            pane.Restore(leaf.Layout);
            leaves.Add((pane, leaf.Index));
            return;
        }

        var split = (RestoredSplit)node;
        var second = AddSplit(pane, split.Layout.Direction, FirstLeaf(split.Second).Profile, out var created);
        created.Ratio = double.IsFinite(split.Layout.Ratio) ? split.Layout.Ratio : 0.5;
        Populate(split.First, pane, leaves);
        Populate(split.Second, second, leaves);
    }

    private static RestoredNode? Resolve(PaneNodeLayout? node, Func<string, ShellProfile?> resolve, ShellProfile? fallback, ref int index)
    {
        if (node?.Pane is { } paneLayout)
        {
            var leafIndex = index++;
            var profile = (paneLayout.ProfileId is { } id ? resolve(id) : null) ?? fallback;
            return profile is null ? null : new RestoredLeaf(paneLayout, profile, leafIndex);
        }

        if (node?.Split is not { } splitLayout)
        {
            return null;
        }

        var first = Resolve(splitLayout.First, resolve, fallback, ref index);
        var second = Resolve(splitLayout.Second, resolve, fallback, ref index);
        return first is null || second is null ? first ?? second : new RestoredSplit(splitLayout, first, second);
    }

    private static RestoredLeaf FirstLeaf(RestoredNode node) => node switch
    {
        RestoredSplit split => FirstLeaf(split.First),
        _ => (RestoredLeaf)node,
    };

    private abstract record RestoredNode;

    private sealed record RestoredLeaf(PaneLayout Layout, ShellProfile Profile, int Index) : RestoredNode;

    private sealed record RestoredSplit(SplitLayout Layout, RestoredNode First, RestoredNode Second) : RestoredNode;

    /// <summary>The panes of the tree, left to right and depth first.</summary>
    private static IEnumerable<TerminalPane> Leaves(IPaneNode node) => node switch
    {
        PaneSplit split => Leaves(split.First).Concat(Leaves(split.Second)),
        _ => [(TerminalPane)node],
    };

    private static PaneNodeLayout NodeLayout(IPaneNode node) => node switch
    {
        PaneSplit split => new PaneNodeLayout(Split: new SplitLayout(split.Direction, split.Ratio, NodeLayout(split.First), NodeLayout(split.Second))),
        _ => new PaneNodeLayout(Pane: ((TerminalPane)node).ToLayout()),
    };

    private void PromoteToMain(TerminalPane pane)
    {
        MainPane = pane;
        _exitCode = pane.ExitCode;
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
