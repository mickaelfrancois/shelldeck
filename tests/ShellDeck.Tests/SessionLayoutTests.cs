using ShellDeck.Session;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class SessionLayoutTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly ShellProfile PowerShell = new("PowerShell 7", "pwsh.exe", ShellKind.PowerShell, @"C:\Users\me");
    private static readonly ShellProfile Cmd = new("Command Prompt", "cmd.exe", ShellKind.Cmd, @"C:\Users\me");
    private static readonly ShellProfile Docker01 = new("docker01", "ssh.exe docker01", ShellKind.Ssh, "me@192.0.2.65");

    private static readonly IReadOnlyList<ShellProfile> Known = [PowerShell, Ubuntu, Cmd, Docker01];

    private static ShellProfile? Resolve(string id) => Known.FirstOrDefault(profile => profile.Id == id);

    private static MainViewModel CreateViewModel(AppSettings? settings = null) =>
        new([PowerShell, Ubuntu, Cmd], [Docker01], settings ?? new AppSettings(), _ => { });

    private static string Json(TabLayout tab) => SessionStore.ToJson(new SessionLayout(1, [tab], 0));

    private static PaneNodeLayout Leaf(string profileId, string? directory = null, string? claudeSessionId = null) =>
        new(Pane: new PaneLayout(profileId, directory, claudeSessionId, false));

    /// <summary>A | (B / C): pwsh left, ubuntu top right, cmd bottom right; ratios 0.3 and 0.7, B active, A main.</summary>
    private static TerminalTab CreateThreePaneTab()
    {
        var tab = new TerminalTab(PowerShell);
        tab.Split(SplitDirection.Right, Ubuntu);
        tab.Split(SplitDirection.Down, Cmd);
        tab.Splits[0].Ratio = 0.3;
        tab.Splits[1].Ratio = 0.7;
        tab.Activate(tab.Panes[1]);
        tab.Panes[0].WorkingDirectory = @"C:\work";
        tab.Panes[1].ClaudeSessionId = "3f2c8a4e-1b5d-4c6e-9f70-8a1b2c3d4e5f";
        return tab;
    }

    [Fact]
    public void A_tab_saved_then_restored_gives_the_same_tree()
    {
        var tab = CreateThreePaneTab();
        var saved = tab.ToLayout();

        var restored = TerminalTab.Restore(saved, Resolve, null)!;

        Assert.Equal(Json(saved), Json(restored.ToLayout()));
    }

    [Fact]
    public void A_restored_tab_has_the_saved_profiles_directions_ratios_and_panes()
    {
        var restored = TerminalTab.Restore(CreateThreePaneTab().ToLayout(), Resolve, null)!;

        Assert.Equal(
            (3, PowerShell, Ubuntu, 0.3, 0.7, SplitDirection.Right, SplitDirection.Down),
            (restored.Panes.Count, restored.MainPane.Profile, restored.ActivePane.Profile, restored.Splits[0].Ratio, restored.Splits[1].Ratio,
                restored.Splits[0].Direction, restored.Splits[1].Direction));
        Assert.Equal(
            (@"C:\work", "3f2c8a4e-1b5d-4c6e-9f70-8a1b2c3d4e5f", true, false),
            (restored.MainPane.WorkingDirectory, restored.ActivePane.ClaudeSessionId, restored.ActivePane.IsActive, restored.MainPane.IsActive));
    }

    [Fact]
    public void A_restored_pane_is_laid_out_like_the_original()
    {
        var original = CreateThreePaneTab();

        var restored = TerminalTab.Restore(original.ToLayout(), Resolve, null)!;

        Assert.Equal(original.Panes.Select(pane => pane.Bounds), restored.Panes.Select(pane => pane.Bounds));
    }

    [Fact]
    public void A_side_pane_saved_as_main_is_main_again()
    {
        var tab = CreateThreePaneTab();
        tab.ClosePane(tab.Panes[0]);
        var saved = tab.ToLayout();

        var restored = TerminalTab.Restore(saved, Resolve, null)!;

        Assert.Equal((saved.MainPane, 2), (restored.ToLayout().MainPane, restored.Panes.Count));
    }

    [Fact]
    public void A_custom_color_survives_and_a_palette_color_keeps_its_name()
    {
        var custom = new TerminalTab(PowerShell) { Color = TabColor.Custom("#123456") };
        var palette = new TerminalTab(PowerShell) { Color = TabColor.Palette[0] };

        var restoredCustom = TerminalTab.Restore(custom.ToLayout(), Resolve, null)!;
        var restoredPalette = TerminalTab.Restore(palette.ToLayout(), Resolve, null)!;

        Assert.Equal((TabColor.Custom("#123456"), TabColor.Palette[0]), (restoredCustom.Color, restoredPalette.Color));
    }

    [Fact]
    public void A_color_that_is_not_a_hex_value_is_dropped()
    {
        var layout = new TabLayout(Leaf("pwsh"), "Evil", "red; drop", 0, 0);

        var restored = TerminalTab.Restore(layout, Resolve, null)!;

        Assert.Equal(TabColor.None, restored.Color);
    }

    [Fact]
    public void The_view_model_restores_the_same_tab_order_colors_and_selection()
    {
        var source = CreateViewModel();
        source.Open(PowerShell);
        source.Open(Ubuntu);
        source.Open(Cmd);
        source.Tabs[0].Color = TabColor.Palette[3];
        source.Tabs[2].Color = TabColor.Custom("#ABCDEF");
        source.MoveTab(source.Tabs[2], 0);
        source.SelectedTab = source.Tabs[1];
        var snapshot = source.Snapshot();

        var target = CreateViewModel();
        var opened = target.Restore(snapshot);

        Assert.Equal(3, opened);
        Assert.Equal(source.Tabs.Select(tab => tab.Profile), target.Tabs.Select(tab => tab.Profile));
        Assert.Equal(source.Tabs.Select(tab => tab.Color), target.Tabs.Select(tab => tab.Color));
        Assert.Equal(source.Tabs.IndexOf(source.SelectedTab!), target.Tabs.IndexOf(target.SelectedTab!));
        Assert.Equal(Json(source.Tabs[0].ToLayout()), Json(target.Tabs[0].ToLayout()));
    }

    [Fact]
    public void A_profile_that_was_deleted_gives_way_to_the_default_profile()
    {
        var viewModel = CreateViewModel();

        viewModel.Restore(new SessionLayout(1, [new TabLayout(Leaf("custom-gone"), null, null, 0, 0)], 0));

        Assert.Equal(viewModel.DefaultProfile, viewModel.Tabs.Single().Profile);
    }

    [Fact]
    public void A_hidden_profile_is_restored_with_its_own_profile()
    {
        var viewModel = CreateViewModel(new AppSettings { Profiles = new ProfileSettings { Hidden = [Ubuntu.Id] } });
        Assert.DoesNotContain(Ubuntu, viewModel.SplitShells);

        viewModel.Restore(new SessionLayout(1, [new TabLayout(Leaf(Ubuntu.Id), null, null, 0, 0)], 0));

        Assert.Equal(Ubuntu, viewModel.Tabs.Single().Profile);
    }

    [Fact]
    public void A_pane_with_no_profile_to_start_is_left_out_without_exception()
    {
        var layout = new TabLayout(
            new PaneNodeLayout(Split: new SplitLayout(SplitDirection.Right, 0.5, Leaf("gone-1"), Leaf("pwsh"))), null, null, 1, 1);

        var restored = TerminalTab.Restore(layout, id => Resolve(id), fallback: null)!;

        Assert.Equal((1, PowerShell, false), (restored.Panes.Count, restored.MainPane.Profile, restored.IsSplit));
    }

    [Fact]
    public void A_tab_with_no_pane_to_start_is_not_opened()
    {
        var viewModel = new MainViewModel([], [], new AppSettings(), _ => { });

        var opened = viewModel.Restore(new SessionLayout(1, [new TabLayout(Leaf("gone"), null, null, 0, 0)], 0));

        Assert.Equal((0, 0), (opened, viewModel.Tabs.Count));
    }

    [Theory]
    [InlineData(-1, 99, 0.0)]
    [InlineData(7, -3, 5.0)]
    [InlineData(1, 1, double.NaN)]
    public void Indices_and_ratios_out_of_range_are_brought_back_to_something_valid(int main, int active, double ratio)
    {
        var layout = new TabLayout(
            new PaneNodeLayout(Split: new SplitLayout(SplitDirection.Right, ratio, Leaf("pwsh"), Leaf("cmd"))), null, null, main, active);

        var restored = TerminalTab.Restore(layout, Resolve, null)!;

        var expectedMain = main == 1 ? Cmd : PowerShell;
        var expectedActive = active == 1 ? Cmd : PowerShell;
        Assert.Equal((expectedMain, expectedActive), (restored.MainPane.Profile, restored.ActivePane.Profile));
        Assert.InRange(restored.Splits[0].Ratio, 0.1, 0.9);
    }

    [Fact]
    public void An_empty_layout_opens_nothing()
    {
        var viewModel = CreateViewModel();

        var opened = viewModel.Restore(new SessionLayout(1, [], 0));

        Assert.Equal((0, 0), (opened, viewModel.Tabs.Count));
    }

    [Fact]
    public void A_selected_tab_index_out_of_range_selects_the_first_tab()
    {
        var viewModel = CreateViewModel();

        viewModel.Restore(new SessionLayout(1, [new TabLayout(Leaf("pwsh"), null, null, 0, 0), new TabLayout(Leaf("cmd"), null, null, 0, 0)], 42));

        Assert.Same(viewModel.Tabs[0], viewModel.SelectedTab);
    }

    [Fact]
    public void A_restored_directory_that_vanished_falls_back_to_the_user_folder()
    {
        var restored = TerminalTab.Restore(
            new TabLayout(Leaf("pwsh", @"C:\no\such\directory\for\shelldeck"), null, null, 0, 0), Resolve, null)!;

        var launch = restored.MainPane.LaunchProfile;

        Assert.Equal(
            (@"C:\no\such\directory\for\shelldeck", @"C:\Users\fallback"),
            (restored.MainPane.RestoredDirectory, launch.WorkingDirectory(@"C:\Users\fallback")));
    }

    [Fact]
    public void A_wsl_pane_restarts_in_its_restored_linux_directory()
    {
        var restored = TerminalTab.Restore(new TabLayout(Leaf(Ubuntu.Id, "/home/me/my proj"), null, null, 0, 0), Resolve, null)!;

        Assert.Equal("wsl.exe -d Ubuntu-26.04 --cd \"/home/me/my proj\"", restored.MainPane.LaunchProfile.LaunchCommandLine);
    }

    [Theory]
    [InlineData("pwsh", "relative\\dir")]
    [InlineData("pwsh", "C:\\a\"b")]
    [InlineData("pwsh", "C:\\a\nb")]
    [InlineData("ubuntu", "no/leading/slash")]
    [InlineData("ubuntu", "/home/me\" --exec evil")]
    [InlineData("pwsh", @"\\attacker\share")]
    [InlineData("pwsh", "//attacker/share")]
    [InlineData("pwsh", @"/\attacker\share")]
    [InlineData("pwsh", @"\\?\C:\work")]
    [InlineData("pwsh", @"\\.\pipe\x")]
    [InlineData("ubuntu", @"\\attacker\share")]
    [InlineData("ubuntu", "//attacker/share")]
    [InlineData("ubuntu", @"/\attacker\share")]
    public void An_unusable_saved_directory_is_ignored(string profile, string directory)
    {
        var id = profile == "pwsh" ? PowerShell.Id : Ubuntu.Id;

        var restored = TerminalTab.Restore(new TabLayout(Leaf(id, directory), null, null, 0, 0), Resolve, null)!;

        Assert.Equal((null, null), (restored.MainPane.RestoredDirectory, restored.MainPane.WorkingDirectory));
        Assert.Same(restored.MainPane.Profile, restored.MainPane.LaunchProfile);
    }

    [Fact]
    public void An_ssh_pane_ignores_a_saved_directory()
    {
        var restored = TerminalTab.Restore(new TabLayout(Leaf(Docker01.Id, @"C:\work"), null, null, 0, 0), Resolve, null)!;

        Assert.Null(restored.MainPane.RestoredDirectory);
    }

    [Fact]
    public void A_pane_without_a_restored_directory_launches_with_its_own_profile()
    {
        var tab = new TerminalTab(PowerShell);

        Assert.Same(PowerShell, tab.MainPane.LaunchProfile);
    }

    private static int CountEvents(TerminalTab tab, Action action)
    {
        var count = 0;
        tab.LayoutChanged += (_, _) => count++;
        action();
        return count;
    }

    [Fact]
    public void Splitting_a_pane_raises_one_event()
    {
        var tab = new TerminalTab(PowerShell);

        Assert.Equal(1, CountEvents(tab, () => tab.Split(SplitDirection.Right)));
    }

    [Fact]
    public void Closing_the_main_active_pane_raises_one_event()
    {
        var tab = new TerminalTab(PowerShell);
        tab.Split(SplitDirection.Right);

        Assert.Equal(1, CountEvents(tab, () => tab.ClosePane(tab.MainPane)));
    }

    [Fact]
    public void Moving_a_divider_raises_one_event()
    {
        var tab = new TerminalTab(PowerShell);
        tab.Split(SplitDirection.Right);

        Assert.Equal(1, CountEvents(tab, () => tab.Splits[0].Ratio = 0.7));
    }

    [Fact]
    public void Coloring_the_tab_raises_one_event()
    {
        var tab = new TerminalTab(PowerShell);

        Assert.Equal(1, CountEvents(tab, () => tab.Color = TabColor.Palette[0]));
    }

    [Fact]
    public void Activating_another_pane_raises_one_event_and_the_same_pane_none()
    {
        var tab = new TerminalTab(PowerShell);
        tab.Split(SplitDirection.Right);

        Assert.Equal((1, 0), (CountEvents(tab, () => tab.Activate(tab.MainPane)), CountEvents(tab, () => tab.Activate(tab.MainPane))));
    }

    [Fact]
    public void A_pane_reporting_its_directory_or_claude_session_raises_an_event_only_when_it_changes()
    {
        var tab = new TerminalTab(PowerShell);

        var events = CountEvents(tab, () =>
        {
            tab.MainPane.WorkingDirectory = @"C:\x";
            tab.MainPane.WorkingDirectory = @"C:\x";
            tab.MainPane.ClaudeSessionId = "3f2c8a4e-1b5d-4c6e-9f70-8a1b2c3d4e5f";
        });

        Assert.Equal(2, events);
    }

    private static int CountEvents(MainViewModel viewModel, Action action)
    {
        var count = 0;
        viewModel.LayoutChanged += (_, _) => count++;
        action();
        return count;
    }

    [Fact]
    public void Opening_closing_and_moving_tabs_raise_the_view_model_event()
    {
        var viewModel = CreateViewModel();

        var opened = CountEvents(viewModel, () => viewModel.Open(PowerShell));
        viewModel.Open(Cmd);
        var moved = CountEvents(viewModel, () => viewModel.MoveTab(viewModel.Tabs[1], 0));
        var closed = CountEvents(viewModel, () => viewModel.Close(viewModel.Tabs[0]));

        Assert.True(opened > 0 && moved > 0 && closed > 0, $"opened {opened}, moved {moved}, closed {closed}");
    }

    [Fact]
    public void Selecting_a_tab_raises_the_view_model_event()
    {
        var viewModel = CreateViewModel();
        viewModel.Open(PowerShell);
        viewModel.Open(Cmd);

        Assert.True(CountEvents(viewModel, () => viewModel.SelectedTab = viewModel.Tabs[0]) > 0);
    }

    [Fact]
    public void A_tab_event_reaches_the_view_model_once_and_stops_after_the_tab_is_closed()
    {
        var viewModel = CreateViewModel();
        viewModel.Open(PowerShell);
        var tab = viewModel.Tabs[0];

        var whileOpen = CountEvents(viewModel, () => tab.Split(SplitDirection.Down));
        viewModel.Close(tab);
        var afterClose = CountEvents(viewModel, () => tab.Color = TabColor.Palette[1]);

        Assert.Equal((1, 0), (whileOpen, afterClose));
    }

    [Fact]
    public void A_tab_restored_into_the_view_model_reports_its_changes()
    {
        var viewModel = CreateViewModel();
        viewModel.Restore(new SessionLayout(1, [new TabLayout(Leaf("pwsh"), null, null, 0, 0)], 0));

        Assert.Equal(1, CountEvents(viewModel, () => viewModel.Tabs[0].Color = TabColor.Palette[2]));
    }
}
