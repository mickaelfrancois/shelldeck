using System.IO;
using System.Windows;
using ShellDeck.Session;
using ShellDeck.Shells;
using ShellDeck.Terminal;

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
    private string? _workingDirectory;
    private string? _claudeSessionId;

    internal TerminalPane(TerminalTab owner, ShellProfile profile)
    {
        _owner = owner;
        Profile = profile;
    }

    public ShellProfile Profile { get; }

    /// <summary>The directory the shell last reported (OSC 7 / 9;9), saved so the pane reopens there.</summary>
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set
        {
            if (SetField(ref _workingDirectory, value))
            {
                _owner.OnPaneLayoutChanged();
            }
        }
    }

    /// <summary>The Claude Code conversation running in the pane, when known.</summary>
    public string? ClaudeSessionId
    {
        get => _claudeSessionId;
        set
        {
            if (SetField(ref _claudeSessionId, value))
            {
                _owner.OnPaneLayoutChanged();
            }
        }
    }

    /// <summary>True while this pane's own title says Claude Code runs in it, main pane or not.</summary>
    public bool IsClaudeRunning => ClaudeCodeTitle.IsClaudeCode(TerminalTitle);

    /// <summary>When Claude Code was first seen in the pane (UTC): its transcript cannot be older than that.</summary>
    internal DateTime? ClaudeDetectedUtc { get; private set; }

    /// <summary>When the conversation was last resolved (UTC): the known conversation must have been written since then to be kept.</summary>
    internal DateTime? ClaudeResolvedUtc { get; set; }

    /// <summary>
    /// True from a restore that resumes Claude Code until Claude is seen in the pane: its title has not arrived yet, so a save
    /// must not record the pane as a plain shell and lose the resume.
    /// </summary>
    private bool _claudeResumePending;

    /// <summary>The directory Claude Code was started in, read from its transcript; null when unknown.</summary>
    internal string? ClaudeDirectory { get; set; }

    /// <summary>Typed into the shell once it has started (the command that resumes Claude Code); null when there is none.</summary>
    public string? StartupInput { get; private set; }

    /// <summary>Hands the startup input to the view, once: a restarted shell does not get it again.</summary>
    public string? TakeStartupInput()
    {
        var input = StartupInput;
        StartupInput = null;
        return input;
    }

    /// <summary>Where the pane starts when it comes from a saved session; null for a pane opened as usual.</summary>
    public string? RestoredDirectory { get; set; }

    /// <summary>
    /// The profile the pane's shell is launched with: the profile itself, started in <see cref="RestoredDirectory"/>
    /// when there is one. A Windows directory that vanished falls back to the user folder in <see cref="ShellProfile.WorkingDirectory"/>.
    /// </summary>
    public ShellProfile LaunchProfile =>
        RestoredDirectory is { Length: > 0 } directory && Profile.HasStartingDirectory
            ? Profile with { Location = directory }
            : Profile;

    private bool _restartedWithoutDirectory;

    /// <summary>
    /// A pane restored in a directory whose shell fails right at startup (a WSL directory that was deleted) gets one new
    /// start in its profile's own directory. Returns true when the caller must start the shell again; false for any
    /// other exit, and for a second failure.
    /// </summary>
    public bool TryRestartWithoutRestoredDirectory(int exitCode, TimeSpan lifetime)
    {
        if (_restartedWithoutDirectory || RestoredDirectory is not { Length: > 0 } || !TerminalTab.IsStartupFailure(exitCode, lifetime))
        {
            return false;
        }

        _restartedWithoutDirectory = true;
        RestoredDirectory = null;
        return true;
    }

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

    /// <summary>The running shell; kept here so it outlives the views WPF unloads and rebuilds.</summary>
    internal ShellSession? Session { get; set; }

    public bool IsExited { get; private set; }

    public int ExitCode { get; private set; }

    /// <summary>The last title the shell set, kept so a side pane can take over as main pane.</summary>
    internal string TerminalTitle { get; private set; } = string.Empty;

    /// <summary>True when closing the pane would interrupt something; Claude Code only counts in the main pane.</summary>
    public bool HasActivity => !IsExited && (_hasRunningCommand() || IsMain && _owner.IsClaudeSession);

    public void TrackRunningCommands(Func<bool> hasRunningCommand) => _hasRunningCommand = hasRunningCommand;

    public void ApplyTerminalTitle(string title)
    {
        var wasClaude = IsClaudeRunning;
        var wasWorking = wasClaude && ClaudeCodeTitle.IsWorking(TerminalTitle);
        TerminalTitle = title;
        TrackClaude(wasClaude, wasWorking);
        if (IsMain)
        {
            _owner.ApplyTerminalTitle(title);
        }
    }

    private void TrackClaude(bool wasClaude, bool wasWorking)
    {
        var isClaude = IsClaudeRunning;
        if (isClaude == wasClaude)
        {
            // A turn just ended: the transcript has been written, this is when to read it.
            if (isClaude && wasWorking && !ClaudeCodeTitle.IsWorking(TerminalTitle))
            {
                _owner.OnClaudeTurnEnded(this);
            }

            return;
        }

        OnPropertyChanged(nameof(IsClaudeRunning));
        if (isClaude)
        {
            _claudeResumePending = false;
            ClaudeDetectedUtc = DateTime.UtcNow;
            return;
        }

        // Claude left: its conversation is no longer the pane's, and no other pane may be refused it.
        ClaudeDetectedUtc = null;
        ClaudeResolvedUtc = null;
        ClaudeDirectory = null;
        ClaudeSessionId = null;
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

    /// <summary>The pane as saved in session.json.</summary>
    internal PaneLayout ToLayout()
    {
        var running = IsClaudeRunning || _claudeResumePending;
        // Claude must restart where it ran: its own directory first, then the shell's.
        var directory = running ? ClaudeDirectory ?? WorkingDirectory : WorkingDirectory;
        return new PaneLayout(Profile.Id, directory, ClaudeSessionId, running);
    }

    /// <summary>Takes back what a saved session knew of the pane. A directory that could break the launch command line is dropped.</summary>
    internal void Restore(PaneLayout layout)
    {
        _claudeSessionId = layout.ClaudeSessionId;
        if (IsUsableDirectory(layout.WorkingDirectory, Profile))
        {
            _workingDirectory = RestoredDirectory = layout.WorkingDirectory;
        }

        // WSL has no id (its transcripts live in the distro): the last conversation of the directory is resumed.
        if (layout.ClaudeRunning && Profile.HasStartingDirectory)
        {
            StartupInput = ResumeCommand.For(Profile.Kind is ShellKind.Wsl ? null : _claudeSessionId);
            _claudeResumePending = true;
        }
    }

    /// <summary>
    /// session.json can be edited by hand and the directory ends up in a command line: no quote or control character,
    /// a rooted path (Linux for WSL, fully qualified for Windows).
    /// </summary>
    internal static bool IsUsableDirectory(string? directory, ShellProfile profile)
    {
        if (string.IsNullOrWhiteSpace(directory) || !profile.HasStartingDirectory
            || directory.Any(character => character == '"' || char.IsControl(character)))
        {
            return false;
        }

        return profile.Kind is ShellKind.Wsl
            ? directory.StartsWith('/') || directory.StartsWith('~')
            : Path.IsPathFullyQualified(directory);
    }

    /// <summary>The pane is closed: its shell ends with it.</summary>
    internal void EndSession()
    {
        Session?.Dispose();
        Session = null;
    }

    /// <summary>The user clicked into the pane.</summary>
    public void RequestActivation() => _owner.Activate(this);

    /// <summary>Asks the pane's view to open its search bar.</summary>
    public event EventHandler? SearchRequested;

    public void RequestSearch() => SearchRequested?.Invoke(this, EventArgs.Empty);

    void IPaneNode.Arrange(Rect area) => Bounds = area;
}
