using ShellDeck.Git;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class TerminalTabGitTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly ShellProfile Docker01 = new("docker01", "ssh.exe docker01", ShellKind.Ssh, "me@192.0.2.65");
    private static readonly GitStatus Repo = new("shell-deck", "main", false, false, 0, 0);
    private const string RepoSubtitle = "shell-deck ⎇ main";

    [Fact]
    public void Git_info_replaces_the_profile_subtitle_in_a_repo()
    {
        var tab = new TerminalTab(Ubuntu);

        tab.ApplyGitStatus(Repo);

        Assert.Equal((RepoSubtitle, Repo), (tab.Subtitle, tab.ShownGit));
    }

    [Fact]
    public void Git_info_replaces_claude_working_text()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.ApplyTerminalTitle("◐ Refactor du handler");

        tab.ApplyGitStatus(Repo);

        Assert.Equal(RepoSubtitle, tab.Subtitle);
    }

    [Fact]
    public void Claude_working_text_stays_outside_a_repo()
    {
        var tab = new TerminalTab(Ubuntu);

        tab.ApplyTerminalTitle("◐ Refactor du handler");

        Assert.Equal("Claude Code travaille…", tab.Subtitle);
    }

    [Fact]
    public void Attention_wins_over_git_info()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.ApplyGitStatus(Repo);

        tab.ReceiveNotification("Claude attend votre réponse");

        Assert.Equal(("Claude attend votre réponse", (GitStatus?)null), (tab.Subtitle, tab.ShownGit));
    }

    [Fact]
    public void Exited_wins_over_git_info()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.ApplyGitStatus(Repo);

        tab.MarkExited(exitCode: 255, lifetime: TimeSpan.FromSeconds(1));

        Assert.Equal(("Processus terminé (code 255)", (GitStatus?)null), (tab.Subtitle, tab.ShownGit));
    }

    [Fact]
    public void Ssh_tabs_never_show_git_info()
    {
        var tab = new TerminalTab(Docker01);

        tab.ApplyGitStatus(Repo);

        Assert.Equal(("SSH · me@192.0.2.65", (GitStatus?)null), (tab.Subtitle, tab.ShownGit));
    }

    [Fact]
    public void Not_a_repo_falls_back_to_profile_and_location()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.ApplyGitStatus(Repo);

        tab.ApplyGitStatus(null);

        Assert.Equal("Ubuntu-26.04 · ~", tab.Subtitle);
    }

    [Fact]
    public void Applying_git_info_raises_subtitle_and_shown_git_once_and_only_on_change()
    {
        var tab = new TerminalTab(Ubuntu);
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.ApplyGitStatus(Repo);
        tab.ApplyGitStatus(Repo with { });

        Assert.Equal(["Subtitle", "ShownGit"], raised);
    }

    [Fact]
    public void Changing_the_main_pane_directory_or_ending_a_command_requests_a_refresh()
    {
        var tab = new TerminalTab(Ubuntu);
        var requests = 0;
        tab.GitRefreshRequested += (_, _) => requests++;

        tab.MainPane.WorkingDirectory = "/home/u/shell-deck";
        tab.MainPane.ReportCommandEnded();

        Assert.Equal(2, requests);
    }

    [Fact]
    public void A_side_pane_never_requests_a_refresh()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.Split(SplitDirection.Right);
        var side = tab.Panes.Single(pane => pane != tab.MainPane);
        var requests = 0;
        tab.GitRefreshRequested += (_, _) => requests++;

        side.WorkingDirectory = "/home/u/other";
        side.ReportCommandEnded();

        Assert.Equal(0, requests);
    }

    [Fact]
    public void Promoting_a_side_pane_clears_the_git_state_and_requests_a_refresh()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.Split(SplitDirection.Right);
        tab.ApplyGitStatus(Repo);
        var requests = 0;
        tab.GitRefreshRequested += (_, _) => requests++;

        tab.ClosePane(tab.MainPane);

        Assert.Equal(((GitStatus?)null, 1), (tab.ShownGit, requests));
    }
}
