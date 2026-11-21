using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class TabLinesSettingsTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");

    [Fact]
    public void The_tab_lines_are_read_from_the_file()
    {
        var settings = AppSettings.Parse("""{ "tabLines": ["{profile}", "{path}"] }""");

        Assert.Equal(["{profile}", "{path}"], settings.TabLines!);
    }

    [Fact]
    public void Without_tab_lines_nothing_is_written_back()
    {
        Assert.DoesNotContain("tabLines", new AppSettings().ToJson());
    }

    [Fact]
    public void A_null_line_in_the_file_is_an_empty_line()
    {
        var settings = AppSettings.Parse("""{ "tabLines": ["{profile}", null, "{title}"] }""");
        var viewModel = new MainViewModel([Ubuntu], [], settings, _ => { });

        viewModel.Open(Ubuntu);

        Assert.Equal(["Ubuntu-26.04", "", "Ubuntu-26.04"], TabText.Of(viewModel.Tabs.Single()));
    }

    [Fact]
    public void A_new_tab_uses_the_configured_lines()
    {
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings { TabLines = ["{profile}", "{path}"] }, _ => { });

        viewModel.Open(Ubuntu);

        // Nothing reported a directory yet: {path} is empty, and the missing third line too.
        Assert.Equal(["Ubuntu-26.04", "", ""], TabText.Of(viewModel.Tabs.Single()));
    }

    [Fact]
    public void Reloading_the_settings_redraws_the_open_tabs()
    {
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings(), _ => { });
        viewModel.Open(Ubuntu);
        var tab = viewModel.Tabs.Single();
        tab.MainPane.WorkingDirectory = "/home/me/proj";

        viewModel.ReloadSettings(new AppSettings { TabLines = ["{path}", "", "{profile}"] });

        Assert.Equal(["/home/me/proj", "", "Ubuntu-26.04"], TabText.Of(tab));
    }

    [Fact]
    public void Removing_the_tab_lines_brings_the_default_back()
    {
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings { TabLines = ["{path}"] }, _ => { });
        viewModel.Open(Ubuntu);

        viewModel.ReloadSettings(new AppSettings());

        Assert.Equal("Ubuntu-26.04", TabText.Of(viewModel.Tabs.Single())[0]);
    }
}
