using ShellDeck.Git;
using ShellDeck.Shells;

namespace ShellDeck.Tests;

public sealed class GitCommandTests
{
    private static ShellProfile Profile(ShellKind kind, string name = "Ubuntu") => new(name, "x", kind, "");

    [Fact]
    public void A_wsl_command_runs_git_through_exec_with_the_directory_apart()
    {
        GitCommandLine? command = GitCommand.For(Profile(ShellKind.Wsl), "/home/u/repo");

        Assert.NotNull(command);
        Assert.Equal("wsl.exe", command.FileName);
        Assert.Equal(["-d", "Ubuntu", "--cd", "/home/u/repo", "--exec", "sh", "-c"], command.Arguments.Take(7));
        Assert.Equal(1, command.Arguments.Count(a => a == "/home/u/repo"));
        Assert.DoesNotContain("/home/u/repo", command.Arguments[^1]);
    }

    [Fact]
    public void A_hostile_directory_never_reaches_the_script()
    {
        const string hostile = "/tmp/$(rm -rf ~);x";

        GitCommandLine? command = GitCommand.For(Profile(ShellKind.Wsl), hostile);

        Assert.NotNull(command);
        Assert.Single(command.Arguments, a => a.Contains("rm -rf"));
        Assert.Contains(hostile, command.Arguments);
        string script = command.Arguments[^1];
        Assert.DoesNotContain("rm -rf", script);
        Assert.DoesNotContain("$(rm", script);
    }

    [Fact]
    public void A_home_relative_wsl_directory_is_passed_unchanged()
    {
        GitCommandLine? command = GitCommand.For(Profile(ShellKind.Wsl), "~/x");

        Assert.NotNull(command);
        Assert.Equal(1, command.Arguments.Count(a => a == "~/x"));
        Assert.Equal("~/x", command.Arguments[command.Arguments.ToList().IndexOf("--cd") + 1]);
    }

    [Fact]
    public void A_windows_command_uses_git_exe_with_dash_c()
    {
        GitCommandLine? command = GitCommand.For(Profile(ShellKind.PowerShell, "pwsh"), @"C:\src\repo");

        Assert.NotNull(command);
        Assert.Equal("git.exe", command.FileName);
        Assert.Equal(
            ["--no-optional-locks", "-c", "core.fsmonitor=false", "-C", @"C:\src\repo", "status", "--porcelain=v2", "--branch"],
            command.Arguments);
    }

    [Theory]
    [InlineData(ShellKind.PowerShell)]
    [InlineData(ShellKind.WindowsPowerShell)]
    [InlineData(ShellKind.Cmd)]
    [InlineData(ShellKind.Wsl)]
    [InlineData(ShellKind.Custom)]
    public void No_command_ever_fetches(ShellKind kind)
    {
        GitCommandLine? command = GitCommand.For(Profile(kind), @"C:\src\repo");

        Assert.NotNull(command);
        Assert.DoesNotContain(command.Arguments, a => a.Contains("fetch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("--no-optional-locks", string.Join(' ', command.Arguments));
        Assert.Contains("core.fsmonitor=false", string.Join(' ', command.Arguments));
    }

    [Fact]
    public void An_ssh_profile_has_no_git_command()
    {
        Assert.Null(GitCommand.For(Profile(ShellKind.Ssh, "host"), "/home/u"));
    }
}
