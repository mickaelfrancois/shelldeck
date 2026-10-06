using System.IO;
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

public sealed class TerminalTab(ShellProfile profile) : ObservableObject
{
    private const string WorkingSubtitle = "Claude Code travaille…";
    private const int SuccessExitCode = 0;
    private static readonly TimeSpan StartupFailureWindow = TimeSpan.FromSeconds(3);
    private const string ClaudeWaitingMessage = "Claude attend votre réponse";
    private const string BellMessage = "Signal du terminal";

    private string _title = profile.Name;
    private bool _isSelected;
    private bool _isClaudeSession;
    private TerminalStatus _status = TerminalStatus.Running;
    private string? _notification;
    private int _exitCode;
    private Func<bool> _hasRunningCommand = () => false;

    public ShellProfile Profile { get; } = profile;

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
            if (SetField(ref _isSelected, value) && value)
            {
                Acknowledge();
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
    /// or a command started from the shell.
    /// </summary>
    public bool HasActivity => Status is not TerminalStatus.Exited && (IsClaudeSession || _hasRunningCommand());

    /// <summary>Raised when a notification arrives while the user is looking at another tab.</summary>
    public event EventHandler? AttentionRequested;

    /// <summary>Raised when the shell ends after being used (e.g. <c>exit</c>): its tab has nothing left to show.</summary>
    public event EventHandler? Ended;

    public void TrackRunningCommands(Func<bool> hasRunningCommand) => _hasRunningCommand = hasRunningCommand;

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
        if (IsSelected || Status is TerminalStatus.Exited)
        {
            return;
        }

        Notification = message;
        Status = TerminalStatus.Attention;
        AttentionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A shell that fails right after starting (unknown distribution, refused SSH connection) keeps its tab open
    /// so the user can read why. Any other exit closes it: <c>exit</c> returns the status of the last command,
    /// so a non-zero code alone does not mean the shell failed.
    /// </summary>
    public void MarkExited(int exitCode, TimeSpan lifetime)
    {
        _exitCode = exitCode;
        Notification = null;
        Status = TerminalStatus.Exited;

        var failedAtStartup = exitCode != SuccessExitCode && lifetime < StartupFailureWindow;
        if (!failedAtStartup)
        {
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsExecutablePath(string title) =>
        Path.IsPathFullyQualified(title) && title.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private void Acknowledge()
    {
        if (Status is not TerminalStatus.Attention)
        {
            return;
        }

        Notification = null;
        Status = ClaudeCodeTitle.IsWorking(Title) ? TerminalStatus.Working : TerminalStatus.Running;
    }
}
