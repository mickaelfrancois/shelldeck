using System.IO;
using Pupitre.Shells;

namespace Pupitre.Tests;

public sealed class ProfileCatalogTests
{
    private static readonly ShellProfile PowerShell = new("PowerShell 7", "pwsh.exe", ShellKind.PowerShell, @"C:\Users\me");
    private static readonly ShellProfile Cmd = new("Invite de commandes", "cmd.exe", ShellKind.Cmd, @"C:\Users\me");
    private static readonly ShellProfile Ubuntu = new("Ubuntu", "wsl.exe -d Ubuntu", ShellKind.Wsl, "~");
    private static readonly ShellProfile Docker01 = new("docker01", "ssh.exe docker01", ShellKind.Ssh, "me@192.0.2.65");
    private static readonly ShellProfile Docker02 = new("docker02", "ssh.exe docker02", ShellKind.Ssh, "me@192.0.2.66");

    private static readonly ShellProfile[] Shells = [PowerShell, Cmd, Ubuntu];
    private static readonly ShellProfile[] Ssh = [Docker01, Docker02];

    private static LauncherProfiles Arrange(ProfileSettings settings) => ProfileCatalog.Arrange(Shells, Ssh, settings);

    [Fact]
    public void Without_settings_the_launcher_shows_what_was_discovered()
    {
        var launcher = Arrange(new ProfileSettings());

        Assert.Equal(Shells, launcher.Shells);
        Assert.Equal(Ssh, launcher.SshConnections);
        Assert.Empty(launcher.Hidden);
    }

    [Fact]
    public void Terminals_missing_from_the_saved_order_come_last_in_discovery_order()
    {
        var launcher = Arrange(new ProfileSettings { Order = ["wsl:Ubuntu", "gone", "ssh:docker02"] });

        Assert.Equal([Ubuntu, PowerShell, Cmd], launcher.Shells);
        Assert.Equal([Docker02, Docker01], launcher.SshConnections);
    }

    [Fact]
    public void Hidden_terminals_are_listed_apart()
    {
        var launcher = Arrange(new ProfileSettings { Hidden = ["cmd", "ssh:docker01"] });

        Assert.Equal([PowerShell, Ubuntu], launcher.Shells);
        Assert.Equal([Docker02], launcher.SshConnections);
        Assert.Equal([Cmd, Docker01], launcher.Hidden);
    }

    [Fact]
    public void Added_terminals_join_the_shells()
    {
        var launcher = Arrange(new ProfileSettings { Custom = [new CustomProfile("b1", "Build", "pwsh.exe -File build.ps1")] });

        Assert.Equal(("b1", ShellKind.Custom, "B"), (launcher.Shells[^1].Id, launcher.Shells[^1].Kind, launcher.Shells[^1].Glyph));
    }

    [Fact]
    public void A_chosen_starting_directory_replaces_the_discovered_one()
    {
        var launcher = Arrange(new ProfileSettings().WithStartingDirectory("wsl:Ubuntu", "~/projets"));

        Assert.Equal(("~/projets", "wsl:Ubuntu"), (launcher.Shells[2].Location, launcher.Shells[2].Id));
    }

    [Fact]
    public void Moving_skips_over_hidden_neighbors()
    {
        var settings = new ProfileSettings { Hidden = ["cmd"] };

        var moved = ProfileCatalog.Move(Shells, Ssh, settings, "pwsh", 1);

        Assert.Equal([Ubuntu, PowerShell], Arrange(moved).Shells);
        Assert.Equal([Cmd], Arrange(moved).Hidden);
    }

    [Fact]
    public void Moving_stays_within_its_section()
    {
        var settings = new ProfileSettings();

        var moved = ProfileCatalog.Move(Shells, Ssh, settings, "ssh:docker01", -1);

        Assert.Same(settings, moved);
    }

    [Fact]
    public void Removing_an_added_terminal_forgets_every_trace_of_it()
    {
        var settings = new ProfileSettings
        {
            Custom = [new CustomProfile("b1", "Build", "build.cmd")],
            Hidden = ["b1"],
            Order = ["b1", "pwsh"],
        }.WithStartingDirectory("b1", @"C:\src");

        Assert.Equal(new ProfileSettings { Order = ["pwsh"] }, settings.Remove("b1"));
    }

    [Theory]
    [InlineData("~", "wsl.exe -d Ubuntu --cd ~")]
    [InlineData("", "wsl.exe -d Ubuntu --cd ~")]
    [InlineData("/mnt/c/mes projets", "wsl.exe -d Ubuntu --cd \"/mnt/c/mes projets\"")]
    public void Wsl_is_told_where_to_start(string location, string expected)
    {
        Assert.Equal(expected, (Ubuntu with { Location = location }).LaunchCommandLine);
    }

    [Fact]
    public void A_starting_directory_that_does_not_exist_falls_back_to_the_user_folder()
    {
        var profile = new CustomProfile("b1", "Build", "build.cmd", @"Z:\nowhere\at\all").ToProfile();

        Assert.Equal(@"C:\Users\me", profile.WorkingDirectory(@"C:\Users\me"));
    }

    [Fact]
    public void A_starting_directory_that_exists_is_used()
    {
        var directory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var profile = new CustomProfile("b1", "Build", "build.cmd", directory).ToProfile();

        Assert.Equal(directory, profile.WorkingDirectory(@"C:\Users\me"));
    }
}
