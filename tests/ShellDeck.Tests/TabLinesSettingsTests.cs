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

public sealed class TabCommandSettingsTests
{
    [Fact]
    public void The_command_is_a_program_and_its_arguments()
    {
        var settings = AppSettings.Parse("""{ "tabCommand": ["wsl.exe", "-d", "Ubuntu", "--exec", "/home/me/tab.sh"] }""");

        Assert.Equal(["wsl.exe", "-d", "Ubuntu", "--exec", "/home/me/tab.sh"], settings.TabCommand!);
    }

    [Fact]
    public void Without_a_command_nothing_is_written_back()
    {
        Assert.DoesNotContain("tabCommand", new AppSettings().ToJson());
    }
}

public sealed class TabCommandWiringTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");

    private sealed class Runner(string output) : ShellDeck.TabLines.ITabCommandRunner
    {
        public int Calls;

        public Task<ShellDeck.TabLines.TabCommandRun> RunAsync(IReadOnlyList<string> command, string input, TimeSpan timeout, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(ShellDeck.TabLines.TabCommandRun.Succeeded(output));
        }
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    [Fact]
    public async Task A_new_tab_gets_the_fields_of_the_command()
    {
        var runner = new Runner("""{ "model": "Opus 5.5" }""");
        var settings = new AppSettings { TabCommand = ["script.sh"], TabLines = ["{model}"] };
        var viewModel = new MainViewModel([Ubuntu], [], settings, _ => { }, tabCommandRunner: runner) { IsWindowActive = true };

        viewModel.Open(Ubuntu);

        Assert.True(await Eventually(() => viewModel.Tabs.Single().Lines[0].Text == "Opus 5.5"));
    }

    [Fact]
    public async Task Nothing_runs_without_a_command()
    {
        var runner = new Runner("{}");
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings(), _ => { }, tabCommandRunner: runner) { IsWindowActive = true };

        viewModel.Open(Ubuntu);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Setting_a_command_in_the_file_runs_it_for_the_open_tabs_and_removing_it_clears_the_fields()
    {
        var runner = new Runner("""{ "model": "Opus" }""");
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings { TabLines = ["{model}"] }, _ => { }, tabCommandRunner: runner) { IsWindowActive = true };
        viewModel.Open(Ubuntu);
        var tab = viewModel.Tabs.Single();

        viewModel.ReloadSettings(new AppSettings { TabLines = ["{model}"], TabCommand = ["script.sh"] });
        Assert.True(await Eventually(() => tab.Lines[0].Text == "Opus"));

        viewModel.ReloadSettings(new AppSettings { TabLines = ["{model}"] });
        Assert.True(await Eventually(() => tab.Lines[0].Text == ""));
    }

    [Fact]
    public async Task The_periodic_refresh_runs_the_command_again()
    {
        var runner = new Runner("{}");
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings { TabCommand = ["script.sh"] }, _ => { }, tabCommandRunner: runner) { IsWindowActive = true };
        viewModel.Open(Ubuntu);
        Assert.True(await Eventually(() => runner.Calls == 1));

        viewModel.RefreshGit();

        Assert.True(await Eventually(() => runner.Calls == 2));
    }

    [Fact]
    public void Without_a_command_the_diagnostic_says_how_to_set_one()
    {
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings(), _ => { });
        viewModel.Open(Ubuntu);

        Assert.Contains("tabCommand", viewModel.DescribeTabCommand(viewModel.Tabs.Single()));
    }

    [Fact]
    public void Before_the_first_run_the_diagnostic_says_it_has_not_run()
    {
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings { TabCommand = ["script.sh"] }, _ => { });
        viewModel.Open(Ubuntu);

        Assert.Contains("pas encore", viewModel.DescribeTabCommand(viewModel.Tabs.Single()));
    }

    [Fact]
    public async Task After_a_run_the_diagnostic_is_the_report()
    {
        var viewModel = new MainViewModel([Ubuntu], [], new AppSettings { TabCommand = ["script.sh"] }, _ => { }, tabCommandRunner: new Runner("""{ "model": "Opus" }""")) { IsWindowActive = true };
        viewModel.Open(Ubuntu);
        var tab = viewModel.Tabs.Single();

        Assert.True(await Eventually(() => tab.CommandDiagnostic is not null));
        Assert.Contains("1 champ(s)", viewModel.DescribeTabCommand(tab));
    }
}
