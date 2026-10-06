namespace Pupitre.Tests;

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
    public void A_file_from_a_previous_version_with_numbers_is_still_read()
    {
        Assert.Equal(PanelSide.Right, AppSettings.Parse("""{ "panelSide": 1 }""").PanelSide);
    }
}
