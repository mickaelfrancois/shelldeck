using System.Text.Json;
using ShellDeck.Git;
using ShellDeck.Shells;
using ShellDeck.TabLines;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class TabCommandOutputTests
{
    [Fact]
    public void Keys_become_fields_with_text_numbers_and_styled_objects()
    {
        var fields = TabCommandOutput.Parse("""{ "model": "Opus 5.5", "ctx": 42, "legion": { "text": "BUILD", "style": "attention" } }""")!;

        Assert.Equal(
            [("Opus 5.5", SegmentStyle.Muted), ("42", SegmentStyle.Muted), ("BUILD", SegmentStyle.Attention)],
            [(fields["model"].Text, fields["model"].Style), (fields["ctx"].Text, fields["ctx"].Style), (fields["legion"].Text, fields["legion"].Style)]);
    }

    [Theory]
    [InlineData("normal", SegmentStyle.Normal)]
    [InlineData("STRONG", SegmentStyle.Strong)]
    [InlineData("dirty", SegmentStyle.GitDirty)]
    [InlineData("ahead", SegmentStyle.GitAhead)]
    [InlineData("behind", SegmentStyle.GitBehind)]
    [InlineData("whatever", SegmentStyle.Muted)]
    public void A_style_name_is_read_without_case_and_an_unknown_one_is_muted(string style, SegmentStyle expected)
    {
        var fields = TabCommandOutput.Parse($$"""{ "x": { "text": "t", "style": "{{style}}" } }""")!;

        Assert.Equal(expected, fields["x"].Style);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"text\"")]
    [InlineData("{ \"a\": ")]
    public void Anything_but_a_json_object_is_a_failure(string output) =>
        Assert.Null(TabCommandOutput.Parse(output));

    [Fact]
    public void Control_characters_become_spaces()
    {
        var fields = TabCommandOutput.Parse("""{ "x": "a\nb\u001b[31mred\r\tend" }""")!;

        Assert.DoesNotContain(fields["x"].Text, character => char.IsControl(character));
        Assert.Equal("a b [31mred  end", fields["x"].Text);
    }

    [Fact]
    public void A_text_is_capped()
    {
        var fields = TabCommandOutput.Parse($$"""{ "x": "{{new string('a', 500)}}" }""")!;

        Assert.Equal(TabCommandOutput.MaxTextLength, fields["x"].Text.Length);
    }

    [Fact]
    public void Empty_null_and_unsupported_values_are_dropped()
    {
        var fields = TabCommandOutput.Parse("""{ "a": "", "b": null, "c": [1], "d": { "style": "strong" }, "e": "   ", "f": "ok" }""")!;

        Assert.Equal(["f"], fields.Keys);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("1x")]
    [InlineData("a{b}")]
    [InlineData("")]
    public void A_key_that_is_not_a_plain_name_is_dropped(string key)
    {
        var fields = TabCommandOutput.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { [key] = "v", ["ok"] = "v" }))!;

        Assert.Equal(["ok"], fields.Keys);
    }

    [Fact]
    public void Names_are_matched_without_case()
    {
        var fields = TabCommandOutput.Parse("""{ "Model": "x" }""")!;

        Assert.True(fields.ContainsKey("model"));
    }

    [Fact]
    public void Only_the_first_fields_are_kept()
    {
        var json = "{" + string.Join(",", Enumerable.Range(0, 100).Select(i => $"\"f{i}\": \"v\"")) + "}";

        Assert.Equal(TabCommandOutput.MaxFields, TabCommandOutput.Parse(json)!.Count);
    }

    [Fact]
    public void Too_much_output_is_a_failure()
    {
        Assert.Null(TabCommandOutput.Parse("{\"x\": \"" + new string('a', TabCommandOutput.MaxOutputBytes) + "\"}"));
    }

    [Fact]
    public void An_empty_object_is_a_success_with_no_fields()
    {
        Assert.Empty(TabCommandOutput.Parse("{}")!);
    }
}

public sealed class TabCommandInputTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly ShellProfile Docker01 = new("docker01", "ssh.exe docker01", ShellKind.Ssh, "me@192.0.2.65");

    private static JsonElement Build(TerminalTab tab) => JsonDocument.Parse(TabCommandInput.Build(tab)).RootElement;

    [Fact]
    public void The_input_carries_the_tab_data()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.MainPane.WorkingDirectory = "/home/me/proj";
        tab.MainPane.ClaudeSessionId = "abc";
        tab.ApplyGitStatus(new GitStatus("proj", "main", false, true, 2, 1));

        var input = Build(tab);

        Assert.Equal(
            (1, "Ubuntu-26.04", "running", "wsl", "~", "/home/me/proj", "abc", "proj", "main", true, 2, 1),
            (input.GetProperty("version").GetInt32(), input.GetProperty("title").GetString(), input.GetProperty("status").GetString(),
             input.GetProperty("profile").GetProperty("kind").GetString(), input.GetProperty("profile").GetProperty("location").GetString(),
             input.GetProperty("directory").GetString(), input.GetProperty("claudeSessionId").GetString(),
             input.GetProperty("git").GetProperty("repo").GetString(), input.GetProperty("git").GetProperty("branch").GetString(),
             input.GetProperty("git").GetProperty("dirty").GetBoolean(), input.GetProperty("git").GetProperty("ahead").GetInt32(), input.GetProperty("git").GetProperty("behind").GetInt32()));
    }

    [Fact]
    public void Outside_a_repo_git_is_null_and_before_a_report_so_is_the_directory()
    {
        var input = Build(new TerminalTab(Ubuntu));

        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), (input.GetProperty("git").ValueKind, input.GetProperty("directory").ValueKind, input.GetProperty("claudeSessionId").ValueKind));
    }

    [Fact]
    public void An_ssh_tab_has_no_directory_and_no_git()
    {
        var tab = new TerminalTab(Docker01);
        tab.MainPane.WorkingDirectory = "/home/me";

        var input = Build(tab);

        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (input.GetProperty("directory").ValueKind, input.GetProperty("git").ValueKind));
    }

    [Fact]
    public void A_hostile_title_stays_data()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.ApplyTerminalTitle("\"; rm -rf ~ #\n");

        Assert.Equal("\"; rm -rf ~ #\n", Build(tab).GetProperty("title").GetString());
    }
}

public sealed class TabFieldsRefresherTests
{
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly string[] Command = ["script.sh"];

    private sealed class FakeRunner : ITabCommandRunner
    {
        public Func<string, Task<string?>> Respond { get; set; } = _ => Task.FromResult<string?>("""{ "model": "Opus" }""");
        public List<string> Inputs { get; } = [];
        public int Running;
        public int MaxRunning;

        public async Task<string?> RunAsync(IReadOnlyList<string> command, string input, TimeSpan timeout, CancellationToken cancellation)
        {
            lock (Inputs)
            {
                Inputs.Add(input);
            }

            var now = Interlocked.Increment(ref Running);
            int max;
            while (now > (max = MaxRunning))
            {
                Interlocked.CompareExchange(ref MaxRunning, now, max);
            }

            try
            {
                return await Respond(input);
            }
            finally
            {
                Interlocked.Decrement(ref Running);
            }
        }
    }

    private static TabFieldsRefresher Create(FakeRunner runner, Func<IReadOnlyList<string>?>? command = null, Func<bool>? isActive = null, Func<TerminalTab, bool>? isOpen = null) =>
        new(runner, command ?? (() => Command), isActive ?? (() => true), isOpen);

    private static TerminalTab Tab()
    {
        var tab = new TerminalTab(Ubuntu);
        tab.SetLineTemplates(TabLineTemplates.From(["{model}", "{ctx}", ""]));
        return tab;
    }

    [Fact]
    public async Task A_run_gives_the_tab_its_fields()
    {
        var tab = Tab();
        var refresher = Create(new FakeRunner());

        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal("Opus", tab.Lines[0].Text);
    }

    [Fact]
    public async Task The_command_receives_the_tab_data_on_its_input()
    {
        var tab = Tab();
        tab.MainPane.WorkingDirectory = "/home/me/proj";
        var runner = new FakeRunner();
        var refresher = Create(runner);

        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Contains("/home/me/proj", runner.Inputs.Single());
    }

    [Fact]
    public async Task A_request_during_a_run_asks_for_one_more_run_not_one_each()
    {
        var gate = new TaskCompletionSource();
        var runner = new FakeRunner { Respond = async _ => { await gate.Task; return """{ "model": "Opus" }"""; } };
        var tab = Tab();
        var refresher = Create(runner);

        refresher.Request([tab]);
        refresher.Request([tab]);
        refresher.Request([tab]);
        gate.SetResult();
        await refresher.WhenIdle();

        Assert.Equal(2, runner.Inputs.Count);
    }

    [Fact]
    public async Task Nothing_runs_while_the_window_is_not_active()
    {
        var runner = new FakeRunner();
        var refresher = Create(runner, isActive: () => false);

        refresher.Request([Tab()]);
        await refresher.WhenIdle();

        Assert.Empty(runner.Inputs);
    }

    [Fact]
    public async Task Without_a_command_nothing_runs_and_the_fields_are_cleared()
    {
        var runner = new FakeRunner();
        var tab = Tab();
        tab.SetExtraFields(new Dictionary<string, TabSegment> { ["model"] = new("Old") });
        var refresher = Create(runner, command: () => null);

        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal((0, ""), (runner.Inputs.Count, tab.Lines[0].Text));
    }

    [Fact]
    public async Task A_tab_that_closed_meanwhile_is_left_alone()
    {
        var open = true;
        var gate = new TaskCompletionSource();
        var runner = new FakeRunner { Respond = async _ => { await gate.Task; return """{ "model": "Opus" }"""; } };
        var tab = Tab();
        var refresher = Create(runner, isOpen: _ => open);

        refresher.Request([tab]);
        open = false;
        gate.SetResult();
        await refresher.WhenIdle();

        Assert.Equal("", tab.Lines[0].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    public async Task A_failed_run_keeps_what_the_tab_showed(string? output)
    {
        var runner = new FakeRunner();
        var tab = Tab();
        var refresher = Create(runner);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        runner.Respond = _ => Task.FromResult(output);
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal("Opus", tab.Lines[0].Text);
    }

    [Fact]
    public async Task A_result_from_a_command_that_was_replaced_is_dropped()
    {
        IReadOnlyList<string>? command = Command;
        var gate = new TaskCompletionSource();
        var runner = new FakeRunner { Respond = async _ => { await gate.Task; return """{ "model": "Old" }"""; } };
        var tab = Tab();
        var refresher = Create(runner, command: () => command);

        refresher.Request([tab]);
        command = ["other.sh"];
        runner.Respond = _ => Task.FromResult<string?>("""{ "model": "New" }""");
        gate.SetResult();
        await refresher.WhenIdle();
        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal("New", tab.Lines[0].Text);
    }

    [Fact]
    public async Task Only_a_few_commands_run_together()
    {
        var gate = new TaskCompletionSource();
        var runner = new FakeRunner { Respond = async _ => { await gate.Task; return "{}"; } };
        var refresher = Create(runner);
        var tabs = Enumerable.Range(0, 10).Select(_ => Tab()).ToList();

        refresher.Request(tabs);
        await Task.Delay(200);
        var during = runner.MaxRunning;
        gate.SetResult();
        await refresher.WhenIdle();

        Assert.Equal((3, 10), (during, runner.Inputs.Count));
    }

    [Fact]
    public async Task An_unchanged_result_does_not_redraw_the_lines()
    {
        var tab = Tab();
        var refresher = Create(new FakeRunner());
        refresher.Request([tab]);
        await refresher.WhenIdle();
        var raised = 0;
        tab.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(TerminalTab.Lines) ? 1 : 0;

        refresher.Request([tab]);
        await refresher.WhenIdle();

        Assert.Equal(0, raised);
    }
}

public sealed class TabFieldsInTemplatesTests
{
    private static readonly TabLineContext Base = new("Ubuntu-26.04", "Ubuntu-26.04", "~", false, "/home/me/proj", null, TerminalStatus.Running);

    private static string Text(string template, TabLineContext tab) =>
        string.Concat(TabLineTemplate.Parse(template).Render(tab).Select(segment => segment.Text));

    [Fact]
    public void A_field_of_the_command_fills_the_template()
    {
        var tab = Base with { Extra = new Dictionary<string, TabSegment>(StringComparer.OrdinalIgnoreCase) { ["model"] = new("Opus", SegmentStyle.Strong, 1) } };

        Assert.Equal("Opus | proj", Text("{model} | {folder}", tab));
    }

    [Fact]
    public void A_built_in_field_wins_over_a_command_field_of_the_same_name()
    {
        var tab = Base with { Extra = new Dictionary<string, TabSegment>(StringComparer.OrdinalIgnoreCase) { ["title"] = new("Hijacked") } };

        Assert.Equal("Ubuntu-26.04", Text("{title}", tab));
    }

    [Fact]
    public void A_field_the_command_never_provided_is_empty()
    {
        Assert.Equal("", Text("{model}", Base));
    }

    [Fact]
    public void The_style_of_a_command_field_is_kept()
    {
        var tab = Base with { Extra = new Dictionary<string, TabSegment> { ["legion"] = new("BUILD", SegmentStyle.Attention, 1) } };

        Assert.Equal(SegmentStyle.Attention, TabLineTemplate.Parse("{legion}").Render(tab).Single().Style);
    }
}

public sealed class ProcessTabCommandRunnerTests
{
    private static Task<string?> Run(TimeSpan timeout, string input, params string[] command) =>
        new ProcessTabCommandRunner().RunAsync(command, input, timeout, CancellationToken.None);

    [Fact]
    public async Task The_output_of_the_command_is_returned() =>
        Assert.Equal("{}", (await Run(TimeSpan.FromSeconds(10), "", "cmd.exe", "/c", "echo", "{}"))?.Trim());

    [Fact]
    public async Task The_input_reaches_the_command_on_its_standard_input() =>
        Assert.Equal("""{"a":1}""", (await Run(TimeSpan.FromSeconds(10), """{"a":1}""" + "\r\n", "cmd.exe", "/c", "findstr", "."))?.Trim());

    [Fact]
    public async Task A_command_that_fails_gives_nothing() =>
        Assert.Null(await Run(TimeSpan.FromSeconds(10), "", "cmd.exe", "/c", "exit", "3"));

    [Fact]
    public async Task A_command_that_does_not_exist_gives_nothing() =>
        Assert.Null(await Run(TimeSpan.FromSeconds(10), "", "no-such-program-shelldeck.exe"));

    [Fact]
    public async Task An_empty_command_gives_nothing() =>
        Assert.Null(await Run(TimeSpan.FromSeconds(10), ""));

    [Fact]
    public async Task A_command_that_is_too_slow_is_killed_and_gives_nothing()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var output = await Run(TimeSpan.FromMilliseconds(500), "", "cmd.exe", "/c", "ping", "-n", "30", "127.0.0.1");

        Assert.Equal((null, true), (output, watch.Elapsed < TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Metacharacters_in_the_input_are_never_run()
    {
        var output = await Run(TimeSpan.FromSeconds(10), "& echo pwned\r\n", "cmd.exe", "/c", "findstr", ".");

        Assert.Equal("& echo pwned", output?.Trim());
    }
}
