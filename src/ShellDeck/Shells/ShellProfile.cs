using System.IO;

namespace ShellDeck.Shells;

public enum ShellKind
{
    PowerShell,
    WindowsPowerShell,
    Cmd,
    Wsl,
    Ssh,
    Custom,
}

/// <summary>Something a tab can run: a local shell, an SSH connection or a command the user added.</summary>
/// <param name="Name">Label shown to the user (shell name or SSH alias).</param>
/// <param name="Location">Where the session starts: a directory (a Linux path for WSL), or <c>user@host</c> for SSH.</param>
public sealed record ShellProfile(string Name, string CommandLine, ShellKind Kind, string Location)
{
    private const string HomeDirectory = "~";

    private readonly string? _id;

    /// <summary>Key the settings refer to: derived from what discovery finds, or a GUID for a terminal the user added.</summary>
    public string Id
    {
        get => _id ?? Kind switch
        {
            ShellKind.PowerShell => "pwsh",
            ShellKind.WindowsPowerShell => "powershell",
            ShellKind.Cmd => "cmd",
            ShellKind.Wsl => $"wsl:{Name}",
            ShellKind.Ssh => $"ssh:{Name}",
            _ => Name,
        };
        init => _id = value;
    }

    public bool IsCustom => Kind is ShellKind.Custom;

    /// <summary>An SSH session starts on the remote host, in the directory the server chooses.</summary>
    public bool HasStartingDirectory => Kind is not ShellKind.Ssh;

    /// <summary>Short mark that identifies the kind of terminal at a glance.</summary>
    public string Glyph => Kind switch
    {
        ShellKind.PowerShell => ">_",
        ShellKind.WindowsPowerShell => "PS",
        ShellKind.Cmd => "C:",
        ShellKind.Wsl or ShellKind.Custom => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?",
        ShellKind.Ssh => "ssh",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };

    /// <summary>The command run in the pane: WSL is told where to start, a Linux path Windows cannot start a process in.</summary>
    public string LaunchCommandLine => Kind is ShellKind.Wsl
        ? $"{CommandLine} --cd {QuoteIfNeeded(string.IsNullOrWhiteSpace(Location) ? HomeDirectory : Location.Trim())}"
        : CommandLine;

    /// <summary>
    /// Windows directory the process starts in. <c>%VARIABLES%</c> and a leading <c>~</c> are expanded; a directory that
    /// does not exist (renamed, unplugged drive) falls back to <paramref name="userDirectory"/> rather than failing the tab.
    /// </summary>
    public string WorkingDirectory(string userDirectory)
    {
        if (Kind is ShellKind.Wsl or ShellKind.Ssh)
        {
            return userDirectory;
        }

        var directory = Environment.ExpandEnvironmentVariables(Location.Trim());
        if (directory == HomeDirectory || directory.StartsWith(HomeDirectory + Path.DirectorySeparatorChar) || directory.StartsWith(HomeDirectory + '/'))
        {
            directory = Path.Join(userDirectory, directory[HomeDirectory.Length..]);
        }

        return directory.Length > 0 && Directory.Exists(directory) ? directory : userDirectory;
    }

    private static string QuoteIfNeeded(string argument) => argument.Contains(' ') ? $"\"{argument}\"" : argument;
}
