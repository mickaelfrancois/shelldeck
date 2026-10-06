namespace Pupitre.Shells;

public enum ShellKind
{
    PowerShell,
    WindowsPowerShell,
    Cmd,
    Wsl,
    Ssh,
}

/// <summary>Something a tab can run: a local shell or an SSH connection.</summary>
/// <param name="Name">Label shown to the user (shell name or SSH alias).</param>
/// <param name="Location">Where the session starts: a directory, or <c>user@host</c> for SSH.</param>
public sealed record ShellProfile(string Name, string CommandLine, ShellKind Kind, string Location)
{
    /// <summary>Short mark that identifies the kind of terminal at a glance.</summary>
    public string Glyph => Kind switch
    {
        ShellKind.PowerShell => ">_",
        ShellKind.WindowsPowerShell => "PS",
        ShellKind.Cmd => "C:",
        ShellKind.Wsl => Name[..1].ToUpperInvariant(),
        ShellKind.Ssh => "ssh",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, null),
    };
}
