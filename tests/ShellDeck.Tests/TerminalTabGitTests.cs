using ShellDeck.Git;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

/// <summary>What a tab's three lines say, one string per line.</summary>
internal static class TabText
{
    public static string[] Of(TerminalTab tab) => [.. tab.Lines.Select(line => line.Text)];
}

public sealed class TerminalTabGitTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly ShellProfile Docker01 = new("docker01", "ssh.exe docker01", ShellKind.Ssh, "me@192.0.2.65");
    private static readonly GitStatus Repo = new("shell-deck", "main", false, false, 0, 0);
    private const string RepoLine = "\u2387 main";

    private static TerminalTab TabIn(ShellProfile profile, string? directory)
    {
        var tab = new TerminalTab(profile);
        tab.MainPane.WorkingDirectory = directory;
        return tab;
    }

    [Fact]
    public void A_tab_always_has_three_lines_even_when_empty()
    {
        var tab = new TerminalTab(Ubuntu);

        Assert.Equal(["Ubuntu-26.04", "Ubuntu-26.04 \u00B7 ~", ""], TabText.Of(tab));
    }

    [Fact]
    public void The_lines_are_title_then_folder_with_its_parent_then_git()
    {
        var tab = TabIn(Ubuntu, "/home/me/projects/shell-deck");

        tab.ApplyGitStatus(Repo);

        Assert.Equal(["Ubuntu-26.04", "projects/shell-deck", RepoLine], TabText.Of(tab));
    }

    [Fact]
    public void The_folder_is_strong_and_its_parent_gives_way_first()
    {
        var tab = TabIn(Ubuntu, "/home/me/projects/shell-deck");

        var segments = tab.Lines[1].Segments;

        Assert.Equal(
            [("projects/", TabLines.SegmentStyle.Muted, 2), ("shell-deck", TabLines.SegmentStyle.Strong, 1)],
            segments.Select(segment => (segment.Text, segment.Style, segment.Shrink)));
    }

    [Fact]
    public void Only_the_first_line_is_primary()
    {
        var tab = new TerminalTab(Ubuntu);

        Assert.Equal([true, false, false], tab.Lines.Select(line => line.IsPrimary));
    }

    [Fact]
    public void Outside_a_repo_the_git_line_is_empty()
    {
        var tab = TabIn(Ubuntu, "/home/me/docs");

        Assert.Equal(["Ubuntu-26.04", "me/docs", ""], TabText.Of(tab));
    }

    [Fact]
    public void The_subtitle_follows_the_directory_of_the_main_pane_only()
    {
        var tab = new TerminalTab(Ubuntu);
        var changes = new List<string?>();
        tab.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        tab.Split(SplitDirection.Right);

        tab.ActivePane.WorkingDirectory = "/tmp";
        Assert.Equal("Ubuntu-26.04 \u00B7 ~", TabText.Of(tab)[1]);

        changes.Clear();
        tab.MainPane.WorkingDirectory = "/home/me";
        Assert.Contains(nameof(TerminalTab.Lines), changes);
    }

    [Fact]
    public void An_ssh_tab_shows_its_connection_and_never_a_directory()
    {
        var tab = TabIn(Docker01, "/home/me");

        Assert.Equal(["docker01", "SSH \u00B7 me@192.0.2.65", ""], TabText.Of(tab));
    }

    [Fact]
    public void Ssh_tabs_never_show_git_info()
    {
        var tab = new TerminalTab(Docker01);

        tab.ApplyGitStatus(Repo);

        Assert.Equal(("", (GitStatus?)null), (TabText.Of(tab)[2], tab.ShownGit));
    }

    [Fact]
    public void A_title_change_raises_the_lines()
    {
        var tab = new TerminalTab(Ubuntu);
        var changes = new List<string?>();
        tab.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        tab.ApplyTerminalTitle("Refactor du handler");

        Assert.Equal(("Refactor du handler", true), (TabText.Of(tab)[0], changes.Contains(nameof(TerminalTab.Lines))));
    }

    [Fact]
    public void Claude_working_text_is_not_shown_on_any_line()
    {
        var tab = new TerminalTab(Ubuntu);

        tab.ApplyTerminalTitle("\u25D0 Refactor du handler");

        Assert.Equal(["\u25D0 Refactor du handler", "Ubuntu-26.04 \u00B7 ~", ""], TabText.Of(tab));
    }

    [Fact]
    public void Attention_takes_the_second_line_and_keeps_the_git_line()
    {
        var tab = TabIn(Ubuntu, "/home/me/shell-deck");
        tab.ApplyGitStatus(Repo);

        tab.ReceiveNotification("Claude attend votre r\u00E9ponse");

        Assert.Equal(["Ubuntu-26.04", "Claude attend votre r\u00E9ponse", RepoLine], TabText.Of(tab));
        Assert.Equal(TabLines.SegmentStyle.Attention, tab.Lines[1].Segments.Single().Style);
    }

    [Fact]
    public void Exited_takes_the_second_line_and_hides_git()
    {
        var tab = TabIn(Ubuntu, "/home/me/shell-deck");
        tab.ApplyGitStatus(Repo);

        tab.MarkExited(exitCode: 255, lifetime: TimeSpan.FromSeconds(1));

        Assert.Equal(["Ubuntu-26.04", "Processus termin\u00E9 (code 255)", ""], TabText.Of(tab));
        Assert.Null(tab.ShownGit);
    }

    [Fact]
    public void Not_a_repo_empties_the_git_line()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.ApplyGitStatus(Repo);

        tab.ApplyGitStatus(null);

        Assert.Equal("", TabText.Of(tab)[2]);
    }

    [Fact]
    public void Custom_templates_replace_the_default_lines()
    {
        var tab = TabIn(Ubuntu, "/home/me/shell-deck");
        tab.ApplyGitStatus(Repo with { IsDirty = true, Ahead = 2 });

        tab.SetLineTemplates(TabLines.TabLineTemplates.From(["{profile}", "{path}", "{git.branch}{git.dirty}{git.ahead}"]));

        Assert.Equal(["Ubuntu-26.04", "/home/me/shell-deck", "main \u25CF \u21912"], TabText.Of(tab));
    }

    [Fact]
    public void Setting_the_templates_raises_the_lines()
    {
        var tab = new TerminalTab(Ubuntu);
        var changes = new List<string?>();
        tab.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        tab.SetLineTemplates(TabLines.TabLineTemplates.From(["{profile}"]));

        Assert.Equal(["Lines"], changes);
    }

    [Fact]
    public void Applying_git_info_raises_lines_and_shown_git_once_and_only_on_change()
    {
        var tab = new TerminalTab(Ubuntu);
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        tab.ApplyGitStatus(Repo);
        tab.ApplyGitStatus(Repo with { });

        Assert.Equal(["Lines", "ShownGit"], raised);
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
