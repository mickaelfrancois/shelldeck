using Pupitre.Shells;
using Pupitre.ViewModels;

namespace Pupitre.Tests;

public sealed class MainViewModelTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly ShellProfile PowerShell = new("PowerShell 7", "pwsh.exe", ShellKind.PowerShell, @"C:\Users\me");
    private static readonly ShellProfile Docker01 = new("docker01", "ssh.exe docker01", ShellKind.Ssh, "me@192.0.2.65");

    private static MainViewModel CreateViewModel(AppSettings settings, Action<AppSettings>? saveSettings = null) =>
        new([PowerShell, Ubuntu], [Docker01], settings, saveSettings ?? (_ => { }));

    private static MainViewModel CreateViewModelWithOpenTab(out TerminalTab tab)
    {
        var viewModel = CreateViewModel(new AppSettings());
        viewModel.Open(Ubuntu);
        tab = viewModel.Tabs.Single();
        return viewModel;
    }

    [Fact]
    public void A_shell_that_exits_normally_closes_its_tab()
    {
        var viewModel = CreateViewModelWithOpenTab(out var tab);

        tab.MarkExited(exitCode: 0, lifetime: TimeSpan.FromMinutes(5));

        Assert.Empty(viewModel.Tabs);
    }

    [Fact]
    public void Typing_exit_after_a_failed_command_still_closes_the_tab()
    {
        var viewModel = CreateViewModelWithOpenTab(out var tab);

        tab.MarkExited(exitCode: 127, lifetime: TimeSpan.FromMinutes(5));

        Assert.Empty(viewModel.Tabs);
    }

    [Fact]
    public void A_shell_that_fails_right_after_starting_keeps_its_tab_open_with_the_exit_code()
    {
        var viewModel = CreateViewModelWithOpenTab(out var tab);

        tab.MarkExited(exitCode: 255, lifetime: TimeSpan.FromSeconds(1));

        Assert.Equal((1, "Processus terminé (code 255)"), (viewModel.Tabs.Count, tab.Subtitle));
    }

    [Fact]
    public void Without_a_chosen_default_the_first_shell_opens_at_startup()
    {
        var viewModel = CreateViewModel(new AppSettings());

        Assert.Equal(PowerShell, viewModel.DefaultProfile);
    }

    [Fact]
    public void The_chosen_default_opens_at_startup_even_when_it_is_an_ssh_connection()
    {
        var viewModel = CreateViewModel(new AppSettings(DefaultProfileName: "docker01"));

        Assert.Equal(Docker01, viewModel.DefaultProfile);
    }

    [Fact]
    public void A_default_that_no_longer_exists_falls_back_to_the_first_shell()
    {
        var viewModel = CreateViewModel(new AppSettings(DefaultProfileName: "Debian"));

        Assert.Equal(PowerShell, viewModel.DefaultProfile);
    }

    [Fact]
    public void Choosing_a_default_is_saved()
    {
        AppSettings? saved = null;
        var viewModel = CreateViewModel(new AppSettings(), settings => saved = settings);

        viewModel.SetDefaultProfile(Ubuntu);

        Assert.Equal(("wsl:Ubuntu-26.04", null), (saved?.DefaultProfileId, saved?.DefaultProfileName));
    }

    [Fact]
    public void A_hidden_default_falls_back_to_the_first_visible_shell()
    {
        var viewModel = CreateViewModel(new AppSettings
        {
            DefaultProfileId = PowerShell.Id,
            Profiles = new ProfileSettings { Hidden = [PowerShell.Id] },
        });

        Assert.Equal(Ubuntu, viewModel.DefaultProfile);
    }

    [Fact]
    public void Hiding_a_terminal_moves_it_out_of_the_launcher_and_is_saved()
    {
        AppSettings? saved = null;
        var viewModel = CreateViewModel(new AppSettings(), settings => saved = settings);

        viewModel.HideProfile(Docker01);

        Assert.Equal((0, Docker01), (viewModel.SshConnectionCount, viewModel.HiddenProfiles.Single()));
        Assert.Equal([Docker01.Id], saved?.Profiles.Hidden);
    }

    [Fact]
    public void Moving_a_shell_after_its_neighbor_reorders_the_launcher()
    {
        var viewModel = CreateViewModel(new AppSettings());

        viewModel.MoveProfile(PowerShell, 1);

        Assert.Equal([Ubuntu, PowerShell], viewModel.Shells.Cast<ShellProfile>());
    }

    [Fact]
    public void An_added_terminal_joins_the_shells_and_can_be_deleted()
    {
        var viewModel = CreateViewModel(new AppSettings());
        var build = new CustomProfile("b1", "Build", "pwsh.exe -File build.ps1", @"C:\src");

        viewModel.SaveCustomProfile(build);
        var afterAdd = viewModel.Shells.Cast<ShellProfile>().Last();
        viewModel.DeleteProfile(afterAdd);

        Assert.Equal(("b1", 2), (afterAdd.Id, viewModel.Shells.Cast<ShellProfile>().Count()));
    }

    [Fact]
    public void A_discovered_terminal_cannot_be_deleted()
    {
        var viewModel = CreateViewModel(new AppSettings());

        viewModel.DeleteProfile(Ubuntu);

        Assert.Contains(Ubuntu, viewModel.Shells.Cast<ShellProfile>());
    }

    [Fact]
    public void Giving_back_the_discovered_directory_forgets_the_choice()
    {
        AppSettings? saved = null;
        var viewModel = CreateViewModel(new AppSettings(), settings => saved = settings);

        viewModel.SetStartingDirectory(Ubuntu, "~/projets");
        viewModel.SetStartingDirectory(Ubuntu, "~");

        Assert.Empty(saved!.Profiles.StartingDirectories);
    }

    private static MainViewModel CreateViewModelWithTabs(int count)
    {
        var viewModel = CreateViewModel(new AppSettings());
        for (var i = 0; i < count; i++)
        {
            viewModel.Open(PowerShell);
        }

        return viewModel;
    }

    [Fact]
    public void Next_tab_wraps_around_to_the_first()
    {
        var viewModel = CreateViewModelWithTabs(3);

        viewModel.SelectNextTab();

        Assert.Same(viewModel.Tabs[0], viewModel.SelectedTab);
    }

    [Fact]
    public void Previous_tab_wraps_around_to_the_last()
    {
        var viewModel = CreateViewModelWithTabs(3);
        viewModel.SelectedTab = viewModel.Tabs[0];

        viewModel.SelectPreviousTab();

        Assert.Same(viewModel.Tabs[2], viewModel.SelectedTab);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(9, 3)]
    public void Tab_number_selects_by_position_and_nine_means_the_last(int number, int expectedIndex)
    {
        var viewModel = CreateViewModelWithTabs(4);

        viewModel.SelectTabByNumber(number);

        Assert.Same(viewModel.Tabs[expectedIndex], viewModel.SelectedTab);
    }

    [Fact]
    public void A_tab_number_beyond_the_open_tabs_keeps_the_selection()
    {
        var viewModel = CreateViewModelWithTabs(2);
        var selected = viewModel.SelectedTab;

        viewModel.SelectTabByNumber(5);

        Assert.Same(selected, viewModel.SelectedTab);
    }

    [Fact]
    public void Enter_on_an_empty_filter_opens_the_default_profile()
    {
        var viewModel = CreateViewModel(new AppSettings(DefaultProfileName: "Ubuntu-26.04"));

        viewModel.OpenFirstMatch();

        Assert.Equal(Ubuntu, viewModel.SelectedTab?.Profile);
    }

    [Fact]
    public void A_new_tab_opens_the_default_profile_right_away()
    {
        var viewModel = CreateViewModel(new AppSettings(DefaultProfileName: "docker01"));

        viewModel.OpenDefault();

        Assert.Equal(Docker01, viewModel.SelectedTab?.Profile);
    }

    [Fact]
    public void Closing_the_selected_tab_selects_the_one_that_takes_its_place()
    {
        var viewModel = CreateViewModel(new AppSettings());
        viewModel.Open(PowerShell);
        viewModel.Open(Ubuntu);
        viewModel.Open(Docker01);
        var (middle, next) = (viewModel.Tabs[1], viewModel.Tabs[2]);
        viewModel.SelectedTab = middle;

        // The tab list clears its selection as soon as the selected item is removed, before Close goes on.
        viewModel.Tabs.CollectionChanged += (_, e) =>
        {
            if (e.OldItems?.Contains(viewModel.SelectedTab) == true)
            {
                viewModel.SelectedTab = null;
            }
        };
        viewModel.Close(middle);

        Assert.Same(next, viewModel.SelectedTab);
    }

    [Fact]
    public void Next_tab_without_a_selection_starts_from_the_first_tab()
    {
        var viewModel = CreateViewModel(new AppSettings());
        viewModel.Open(PowerShell);
        viewModel.Open(Ubuntu);
        viewModel.SelectedTab = null;

        viewModel.SelectNextTab();

        Assert.Same(viewModel.Tabs[0], viewModel.SelectedTab);
    }

    [Fact]
    public void Settings_edited_by_hand_apply_without_being_written_back()
    {
        var saves = 0;
        var viewModel = CreateViewModel(new AppSettings(), _ => saves++);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.ReloadSettings(new AppSettings(PanelSide.Right, "Ubuntu-26.04"));

        Assert.Equal((PanelSide.Right, Ubuntu, 0), (viewModel.PanelSide, viewModel.DefaultProfile, saves));
        Assert.Contains(nameof(MainViewModel.PanelSide), changed);
    }

    [Fact]
    public void Terminals_hidden_by_hand_in_the_file_leave_the_launcher()
    {
        var viewModel = CreateViewModel(new AppSettings());

        viewModel.ReloadSettings(new AppSettings { Profiles = new ProfileSettings { Hidden = [Ubuntu.Id] } });

        Assert.Equal([PowerShell], viewModel.Shells.Cast<ShellProfile>());
    }

    [Fact]
    public void A_short_launcher_has_no_filter_box()
    {
        Assert.False(CreateViewModel(new AppSettings()).IsFilterVisible);
    }

    [Fact]
    public void Many_ssh_connections_bring_the_filter_box()
    {
        var connections = Enumerable.Range(1, 10)
            .Select(number => new ShellProfile($"srv{number}", $"ssh.exe srv{number}", ShellKind.Ssh, $"me@srv{number}"))
            .ToList();

        var viewModel = new MainViewModel([PowerShell], connections, new AppSettings(), _ => { });

        Assert.True(viewModel.IsFilterVisible);
    }

    [Fact]
    public void Hiding_terminals_until_the_filter_box_goes_away_clears_the_filter()
    {
        var connections = Enumerable.Range(1, 10)
            .Select(number => new ShellProfile($"srv{number}", $"ssh.exe srv{number}", ShellKind.Ssh, $"me@srv{number}"))
            .ToList();
        var viewModel = new MainViewModel([PowerShell], connections, new AppSettings(), _ => { });
        viewModel.FilterText = "srv1";

        viewModel.HideProfile(connections[0]);

        Assert.Equal((false, string.Empty), (viewModel.IsFilterVisible, viewModel.FilterText));
    }
}
