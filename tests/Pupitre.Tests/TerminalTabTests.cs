using Pupitre.Shells;
using Pupitre.ViewModels;

namespace Pupitre.Tests;

public sealed class TerminalTabTests
{
    private static TerminalTab CreateWslTab() =>
        new(new ShellProfile("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~"));

    [Fact]
    public void A_half_circle_title_means_claude_code_is_working()
    {
        var tab = CreateWslTab();

        tab.ApplyTerminalTitle("◐ Refactor du handler");

        Assert.Equal((TerminalStatus.Working, true), (tab.Status, tab.IsClaudeSession));
    }

    [Fact]
    public void Claude_code_going_idle_in_a_background_tab_asks_for_attention()
    {
        var tab = CreateWslTab();
        var attentionRequests = 0;
        tab.AttentionRequested += (_, _) => attentionRequests++;
        tab.ApplyTerminalTitle("◑ Refactor du handler");

        tab.ApplyTerminalTitle("✳ Refactor du handler");

        Assert.Equal((TerminalStatus.Attention, 1), (tab.Status, attentionRequests));
    }

    [Fact]
    public void Claude_code_going_idle_in_the_selected_tab_needs_no_attention()
    {
        var tab = CreateWslTab();
        tab.IsSelected = true;
        tab.ApplyTerminalTitle("◑ Refactor du handler");

        tab.ApplyTerminalTitle("✳ Refactor du handler");

        Assert.Equal(TerminalStatus.Running, tab.Status);
    }

    [Fact]
    public void Selecting_a_tab_acknowledges_its_notification()
    {
        var tab = CreateWslTab();
        tab.ReceiveNotification("Claude a besoin de votre autorisation pour Bash");

        tab.IsSelected = true;

        Assert.Equal((TerminalStatus.Running, (string?)null), (tab.Status, tab.Notification));
    }

    [Fact]
    public void The_executable_path_set_by_conpty_is_replaced_by_the_profile_name()
    {
        var tab = CreateWslTab();

        tab.ApplyTerminalTitle(@"C:\WINDOWS\system32\wsl.exe");

        Assert.Equal("Ubuntu-26.04", tab.Title);
    }

    [Fact]
    public void Leaving_claude_code_removes_the_claude_marker()
    {
        var tab = CreateWslTab();
        tab.ApplyTerminalTitle("✳ Revue PR");

        tab.ApplyTerminalTitle("~/projects/term");

        Assert.False(tab.IsClaudeSession);
    }

    [Fact]
    public void A_title_without_claude_marker_is_a_plain_shell()
    {
        var tab = CreateWslTab();

        tab.ApplyTerminalTitle("dev@poste: ~/projects");

        Assert.Equal((TerminalStatus.Running, false), (tab.Status, tab.IsClaudeSession));
    }

    [Fact]
    public void An_idle_shell_can_be_closed_without_confirmation()
    {
        var tab = CreateWslTab();
        tab.TrackRunningCommands(() => false);

        Assert.False(tab.HasActivity);
    }

    [Fact]
    public void A_shell_running_a_command_has_activity()
    {
        var tab = CreateWslTab();
        tab.TrackRunningCommands(() => true);

        Assert.True(tab.HasActivity);
    }

    [Fact]
    public void A_claude_code_session_has_activity_even_when_idle()
    {
        var tab = CreateWslTab();

        tab.ApplyTerminalTitle("✳ Revue PR");

        Assert.True(tab.HasActivity);
    }

    [Fact]
    public void An_exited_shell_has_no_activity()
    {
        var tab = CreateWslTab();
        tab.TrackRunningCommands(() => true);

        tab.MarkExited(exitCode: 1, lifetime: TimeSpan.FromSeconds(1));

        Assert.False(tab.HasActivity);
    }
}
