using System.IO;
using ShellDeck.Shells;

namespace ShellDeck.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void The_file_reads_naturally_when_opened_in_an_editor()
    {
        var json = new AppSettings(PanelSide.Right, "Ubuntu", new WindowPlacement(10, 20, 800, 600, IsMaximized: false)).ToJson();

        Assert.Contains("\"panelSide\": \"right\"", json);
        Assert.DoesNotContain("isValid", json);
    }

    [Fact]
    public void A_file_written_by_hand_is_read_back()
    {
        const string json = """
            {
              // the side panel on the right
              "panelSide": "right",
              "defaultProfileName": "Ubuntu",
            }
            """;

        Assert.Equal(new AppSettings(PanelSide.Right, "Ubuntu"), AppSettings.Parse(json));
    }

    [Fact]
    public void A_file_without_restoreSession_restores_the_session()
    {
        Assert.Equal(RestoreSession.Always, AppSettings.Parse("""{ "panelSide": "left" }""").RestoreSession);
        Assert.Equal(RestoreSession.Always, new AppSettings().RestoreSession);
    }

    [Theory]
    [InlineData(RestoreSession.Never, "never")]
    [InlineData(RestoreSession.Ask, "ask")]
    [InlineData(RestoreSession.Always, "always")]
    public void The_restoreSession_choice_is_written_in_camel_case_and_read_back(RestoreSession mode, string text)
    {
        var json = new AppSettings { RestoreSession = mode }.ToJson();

        Assert.Contains($"\"restoreSession\": \"{text}\"", json);
        Assert.Equal(mode, AppSettings.Parse(json).RestoreSession);
    }

    [Fact]
    public void A_file_from_a_previous_version_with_numbers_is_still_read()
    {
        Assert.Equal(PanelSide.Right, AppSettings.Parse("""{ "panelSide": 1 }""").PanelSide);
    }

    [Fact]
    public void Launcher_choices_survive_a_round_trip_through_the_file()
    {
        var settings = new AppSettings
        {
            DefaultProfileId = "wsl:Ubuntu",
            Profiles = new ProfileSettings
            {
                Custom = [new CustomProfile("b1", "Build", "pwsh.exe -File build.ps1", @"C:\src")],
                StartingDirectories = new Dictionary<string, string> { ["wsl:Ubuntu"] = "~/projets" },
                Hidden = ["cmd"],
                Order = ["wsl:Ubuntu", "pwsh"],
            },
        };

        Assert.Equal(settings, AppSettings.Parse(settings.ToJson()));
    }

    [Fact]
    public void Profile_ids_keep_their_case_in_the_file()
    {
        var json = new AppSettings { Profiles = new ProfileSettings().WithStartingDirectory("wsl:Ubuntu", "~") }.ToJson();

        Assert.Contains("\"wsl:Ubuntu\": \"~\"", json);
    }

    [Fact]
    public void A_file_without_the_old_default_name_does_not_write_it()
    {
        Assert.DoesNotContain("defaultProfileName", new AppSettings { DefaultProfileId = "pwsh" }.ToJson());
    }

    [Fact]
    public void Nulls_written_by_hand_in_the_profiles_read_as_empty()
    {
        const string json = """
            { "profiles": { "hidden": null, "order": null, "custom": [ { "id": "b1", "name": "Build", "commandLine": "build.cmd" }, { "name": "sans id" } ] } }
            """;

        var profiles = AppSettings.Parse(json).Profiles;

        Assert.Equal(new ProfileSettings { Custom = [new CustomProfile("b1", "Build", "build.cmd")] }, profiles);
    }

    [Fact]
    public void The_file_of_a_previous_version_is_copied_to_the_new_place_once()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var (previous, current) = (Path.Combine(folder, "old", "settings.json"), Path.Combine(folder, ".shelldeck", "settings.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
        File.WriteAllText(previous, "{ \"panelSide\": \"right\" }");

        AppSettings.MoveFromPreviousLocation(previous, current);
        File.WriteAllText(previous, "{ \"panelSide\": \"left\" }");
        AppSettings.MoveFromPreviousLocation(previous, current);

        Assert.Equal((PanelSide.Right, true), (AppSettings.Parse(File.ReadAllText(current)).PanelSide, File.Exists(previous)));
        Directory.Delete(folder, recursive: true);
    }
}
