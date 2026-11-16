using ShellDeck.Shells;

namespace ShellDeck.Git;

/// <summary>An executable and its arguments, one token per argument: nothing goes through a shell parser on the Windows side.</summary>
public sealed record GitCommandLine(string FileName, IReadOnlyList<string> Arguments);

/// <summary>Builds the command that reads the git state of a directory. It never talks to a remote.</summary>
public static class GitCommand
{
    // Constant: the directory never appears in it. WSL receives the directory through --cd, so a path holding
    // $(...), ; or a backtick is never interpreted. Exit code 3 means "not a repository".
    private const string WslScript =
        "top=$(git rev-parse --show-toplevel 2>/dev/null) || exit 3\n" +
        "printf '%s\\n' \"$top\"\n" +
        "exec git --no-optional-locks -c core.fsmonitor=false status --porcelain=v2 --branch";

    /// <summary>The command for a profile and a directory, or <c>null</c> when the profile has no git state (SSH).</summary>
    /// <param name="directory">For WSL, the pane directory as reported (<c>~/x</c> included). Otherwise the repository root, already resolved.</param>
    public static GitCommandLine? For(ShellProfile profile, string directory)
    {
        switch (profile.Kind)
        {
            case ShellKind.Ssh:
                return null;
            case ShellKind.Wsl:
                return new GitCommandLine("wsl.exe",
                [
                    "-d", profile.Name,
                    "--cd", directory,
                    "--exec", "sh", "-c", WslScript,
                ]);
            default:
                return new GitCommandLine("git.exe",
                [
                    "--no-optional-locks",
                    "-c", "core.fsmonitor=false",
                    "-C", directory,
                    "status", "--porcelain=v2", "--branch",
                ]);
        }
    }
}
