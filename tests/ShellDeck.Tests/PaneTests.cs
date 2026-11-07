using System.Windows;
using System.Windows.Input;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class PaneTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly ShellProfile Docker01 = new("docker01", "ssh.exe docker01", ShellKind.Ssh, "me@192.0.2.65");
    private static readonly ShellProfile PowerShell = new("PowerShell", "pwsh.exe", ShellKind.PowerShell, "~");

    private static TerminalTab CreateSplitTab(out TerminalPane main, out TerminalPane side)
    {
        var tab = new TerminalTab(Ubuntu) { IsSelected = true };
        main = tab.MainPane;
        tab.Split(SplitDirection.Right);
        side = tab.ActivePane;
        return tab;
    }

    [Fact]
    public void Splitting_opens_the_same_shell_in_a_new_pane_that_takes_the_keyboard()
    {
        var tab = CreateSplitTab(out var main, out var side);

        Assert.Equal((2, Ubuntu, true, false), (tab.Panes.Count, side.Profile, side.IsActive, main.IsActive));
    }

    [Fact]
    public void Splitting_with_another_profile_opens_it_in_the_new_pane_that_takes_the_keyboard()
    {
        var tab = new TerminalTab(Ubuntu) { IsSelected = true };
        var main = tab.MainPane;

        tab.Split(SplitDirection.Right, Docker01);

        var side = tab.ActivePane;
        Assert.Equal((2, Docker01, true, false), (tab.Panes.Count, side.Profile, side.IsActive, main.IsActive));
    }

    [Fact]
    public void Splitting_without_a_profile_keeps_the_active_pane_profile()
    {
        var tab = new TerminalTab(Ubuntu) { IsSelected = true };
        tab.Split(SplitDirection.Right, Docker01);

        tab.Split(SplitDirection.Down);

        Assert.Equal((Docker01, 3), (tab.ActivePane.Profile, tab.Panes.Count));
    }

    [Fact]
    public void Splitting_with_another_profile_keeps_the_tab_header_of_the_main_pane()
    {
        var tab = new TerminalTab(Ubuntu) { IsSelected = true };
        var before = (tab.Profile, tab.Title, tab.Subtitle);

        tab.Split(SplitDirection.Down, PowerShell);

        Assert.Equal(before, (tab.Profile, tab.Title, tab.Subtitle));
    }

    [Fact]
    public void Splitting_right_shares_the_width_in_two()
    {
        CreateSplitTab(out var main, out var side);

        Assert.Equal((new Rect(0, 0, 0.5, 1), new Rect(0.5, 0, 0.5, 1)), (main.Bounds, side.Bounds));
    }

    [Fact]
    public void A_divider_cannot_hide_a_pane()
    {
        var tab = CreateSplitTab(out _, out _);

        tab.Splits.Single().MoveDivider(new Vector(-2, 0));

        Assert.Equal(0.1, tab.Splits.Single().Ratio, precision: 6);
    }

    [Fact]
    public void Claude_code_waiting_while_the_user_types_in_a_side_pane_asks_for_attention()
    {
        var tab = CreateSplitTab(out var main, out _);
        var attentionRequests = 0;
        tab.AttentionRequested += (_, _) => attentionRequests++;
        main.ApplyTerminalTitle("◑ Refactor du handler");

        main.ApplyTerminalTitle("✳ Refactor du handler");

        Assert.Equal((TerminalStatus.Attention, 1), (tab.Status, attentionRequests));
    }

    [Fact]
    public void Going_back_to_the_main_pane_acknowledges_its_notification()
    {
        var tab = CreateSplitTab(out var main, out _);
        main.ReceiveNotification("Claude a besoin de votre autorisation pour Bash");

        main.RequestActivation();

        Assert.Equal(TerminalStatus.Running, tab.Status);
    }

    [Fact]
    public void A_side_pane_never_notifies()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.Split(SplitDirection.Down);

        tab.ActivePane.ReceiveBell();
        tab.ActivePane.ApplyTerminalTitle("✳ Autre session");

        Assert.Equal((TerminalStatus.Running, false), (tab.Status, tab.IsClaudeSession));
    }

    [Fact]
    public void Exiting_a_side_pane_gives_its_place_and_the_keyboard_back()
    {
        var tab = CreateSplitTab(out var main, out var side);
        var ended = false;
        tab.Ended += (_, _) => ended = true;

        side.MarkExited(exitCode: 0, lifetime: TimeSpan.FromMinutes(5));

        Assert.Equal((false, false, new Rect(0, 0, 1, 1), true), (ended, tab.IsSplit, main.Bounds, main.IsActive));
    }

    [Fact]
    public void When_the_main_shell_exits_a_side_pane_becomes_the_main_pane()
    {
        var tab = CreateSplitTab(out var main, out var side);
        side.ApplyTerminalTitle("dev@poste: ~/projects");
        main.ApplyTerminalTitle("✳ Revue PR");

        main.MarkExited(exitCode: 0, lifetime: TimeSpan.FromMinutes(5));

        Assert.Equal((side, "dev@poste: ~/projects", false, TerminalStatus.Running), (tab.MainPane, tab.Title, tab.IsClaudeSession, tab.Status));
    }

    [Fact]
    public void Closing_a_nested_pane_keeps_the_other_split()
    {
        var tab = CreateSplitTab(out var main, out var side);
        tab.Split(SplitDirection.Down);
        var bottom = tab.ActivePane;

        tab.ClosePane(side);

        Assert.Equal((bottom, new Rect(0.5, 0, 0.5, 1), 1), (tab.ActivePane, bottom.Bounds, tab.Splits.Count));
    }

    [Fact]
    public void Moving_left_from_a_stacked_pane_reaches_the_pane_beside_it()
    {
        var tab = CreateSplitTab(out var main, out _);
        tab.Split(SplitDirection.Down);

        tab.MoveFocus(PaneDirection.Left);

        Assert.Same(main, tab.ActivePane);
    }

    [Fact]
    public void Moving_right_goes_to_the_pane_sharing_the_longest_edge()
    {
        var tab = CreateSplitTab(out var main, out _);
        tab.Split(SplitDirection.Down);
        var bottomRight = tab.ActivePane;
        tab.Splits[1].Ratio = 0.3;
        main.RequestActivation();

        tab.MoveFocus(PaneDirection.Right);

        Assert.Same(bottomRight, tab.ActivePane);
    }

    [Fact]
    public void Moving_past_the_edge_keeps_the_active_pane()
    {
        var tab = CreateSplitTab(out _, out var side);

        tab.MoveFocus(PaneDirection.Right);

        Assert.Same(side, tab.ActivePane);
    }

    [Fact]
    public void Alt_shift_right_moves_the_divider_right()
    {
        var tab = CreateSplitTab(out var main, out _);
        main.RequestActivation();

        tab.MoveDivider(PaneDirection.Right);

        Assert.Equal(0.55, tab.Splits.Single().Ratio, precision: 6);
    }

    [Fact]
    public void Moving_across_the_split_axis_leaves_the_divider_alone()
    {
        var tab = CreateSplitTab(out _, out _);

        tab.MoveDivider(PaneDirection.Up);

        Assert.Equal(0.5, tab.Splits.Single().Ratio, precision: 6);
    }

    [Fact]
    public void The_closest_divider_on_the_axis_moves()
    {
        var tab = CreateSplitTab(out _, out _);
        tab.Split(SplitDirection.Down);

        tab.MoveDivider(PaneDirection.Up);
        tab.MoveDivider(PaneDirection.Left);

        Assert.Equal((0.45, 0.45), (Math.Round(tab.Splits[0].Ratio, 6), Math.Round(tab.Splits[1].Ratio, 6)));
    }

    [Fact]
    public void A_command_running_in_a_side_pane_counts_as_tab_activity()
    {
        var tab = CreateSplitTab(out _, out var side);

        side.TrackRunningCommands(() => true);

        Assert.True(tab.HasActivity);
    }

    [Theory]
    [InlineData(Key.D, ModifierKeys.Alt | ModifierKeys.Shift, ShortcutAction.SplitAuto)]
    [InlineData(Key.Subtract, ModifierKeys.Alt | ModifierKeys.Shift, ShortcutAction.SplitDown)]
    [InlineData(Key.Left, ModifierKeys.Alt, ShortcutAction.FocusPaneLeft)]
    [InlineData(Key.W, ModifierKeys.Control | ModifierKeys.Shift, ShortcutAction.ClosePane)]
    public void Pane_shortcuts_follow_windows_terminal(Key key, ModifierKeys modifiers, ShortcutAction expected)
    {
        Assert.Equal(expected, Shortcuts.Find(key, modifiers)?.Action);
    }

    [Fact]
    public void AltGr_types_characters_instead_of_triggering_a_shortcut()
    {
        Assert.Null(Shortcuts.Find(Key.D, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift));
    }

    [Fact]
    public void A_side_pane_taking_over_keeps_its_own_exit_code()
    {
        var tab = CreateSplitTab(out var main, out var side);
        side.MarkExited(exitCode: 255, lifetime: TimeSpan.FromSeconds(1));

        tab.ClosePane(main);

        Assert.Equal((side, "Processus terminé (code 255)"), (tab.MainPane, tab.Subtitle));
    }

    [Fact]
    public void An_exit_reported_after_the_pane_was_closed_leaves_the_tab_alone()
    {
        var tab = CreateSplitTab(out _, out var side);
        var ended = false;
        tab.Ended += (_, _) => ended = true;
        tab.ClosePane(side);

        side.MarkExited(exitCode: 0, lifetime: TimeSpan.FromMinutes(5));

        Assert.False(ended);
    }

    [Fact]
    public void RequestSearch_raises_SearchRequested()
    {
        var tab = new TerminalTab(Ubuntu) { IsSelected = true };
        var raised = 0;
        tab.MainPane.SearchRequested += (_, _) => raised++;

        tab.MainPane.RequestSearch();

        Assert.Equal(1, raised);
    }
}
