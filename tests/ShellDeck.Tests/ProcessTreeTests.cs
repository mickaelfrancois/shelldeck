using ShellDeck.Pty;

namespace ShellDeck.Tests;

public sealed class ProcessTreeTests
{
    private const int Shell = 100;

    [Fact]
    public void A_shell_alone_with_its_console_host_runs_no_command()
    {
        ProcessTree.ProcessInfo[] processes = [new(Shell, 1, "pwsh.exe"), new(101, Shell, "conhost.exe")];

        Assert.False(ProcessTree.HasCommand(processes, Shell));
    }

    [Fact]
    public void A_child_process_is_a_running_command()
    {
        ProcessTree.ProcessInfo[] processes = [new(Shell, 1, "pwsh.exe"), new(101, Shell, "claude.exe")];

        Assert.True(ProcessTree.HasCommand(processes, Shell));
    }

    [Fact]
    public void Wsl_relaunching_itself_is_not_a_running_command()
    {
        ProcessTree.ProcessInfo[] processes =
            [new(Shell, 1, "wsl.exe"), new(101, Shell, "wsl.exe"), new(102, 101, "wslhost.exe"), new(103, 102, "conhost.exe")];

        Assert.False(ProcessTree.HasCommand(processes, Shell));
    }

    [Fact]
    public void A_command_started_by_the_relaunched_shell_still_counts()
    {
        ProcessTree.ProcessInfo[] processes = [new(Shell, 1, "cmd.exe"), new(101, Shell, "CMD.EXE"), new(102, 101, "ping.exe")];

        Assert.True(ProcessTree.HasCommand(processes, Shell));
    }

    [Fact]
    public void A_windows_program_run_from_wsl_is_a_running_command()
    {
        ProcessTree.ProcessInfo[] processes =
            [new(Shell, 1, "wsl.exe"), new(101, Shell, "wsl.exe"), new(102, 101, "wslhost.exe"), new(103, 101, "dotnet.exe")];

        Assert.True(ProcessTree.HasCommand(processes, Shell));
    }
}
