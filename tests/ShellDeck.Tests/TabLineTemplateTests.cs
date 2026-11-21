using ShellDeck.Git;
using ShellDeck.TabLines;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class TabLineTemplateTests
{
    private static readonly TabLineContext Plain = new("Ubuntu-26.04", "Ubuntu-26.04", "~", false, "/home/me/projects/shell-deck", null, TerminalStatus.Running);
    private static readonly TabLineContext InRepo = Plain with { Git = new GitStatus("shell-deck", "main", false, true, 2, 1) };

    private static string Text(string template, TabLineContext tab) =>
        string.Concat(TabLineTemplate.Parse(template).Render(tab).Select(segment => segment.Text));

    [Theory]
    [InlineData("{title}", "Ubuntu-26.04")]
    [InlineData("{parent}{folder}", "projects/shell-deck")]
    [InlineData("{path}", "/home/me/projects/shell-deck")]
    [InlineData("{profile} - {folder}", "Ubuntu-26.04 - shell-deck")]
    [InlineData("{ TITLE }", "Ubuntu-26.04")]
    [InlineData("no field at all", "no field at all")]
    [InlineData("", "")]
    public void Fields_are_replaced_by_what_the_tab_knows(string template, string expected) =>
        Assert.Equal(expected, Text(template, Plain));

    [Theory]
    [InlineData("{git}", "\u2387 main \u25CF \u21912 \u21931")]
    [InlineData("{git.repo}", "shell-deck")]
    [InlineData("{git.branch}", "main")]
    [InlineData("{git.branch}{git.dirty}", "main \u25CF")]
    [InlineData("{git.ahead}{git.behind}", " \u21912 \u21931")]
    public void Git_fields_read_the_repository(string template, string expected) =>
        Assert.Equal(expected, Text(template, InRepo));

    [Theory]
    [InlineData("{git}")]
    [InlineData("\u2387 {git.branch}")]
    [InlineData("{git.repo} {git.branch}")]
    public void A_line_whose_fields_are_all_empty_is_empty(string template) =>
        Assert.Equal("", Text(template, Plain));

    [Fact]
    public void Markers_only_show_when_needed()
    {
        var clean = InRepo with { Git = new GitStatus("r", "main", false, false, 0, 0) };

        Assert.Equal("\u2387 main", Text("{git}", clean));
    }

    [Theory]
    [InlineData("{nobody}", "")]
    [InlineData("a{nobody}b", "")]
    [InlineData("{unclosed", "{unclosed")]
    [InlineData("tail {", "tail {")]
    [InlineData("{}", "{}")]
    [InlineData("{{title}}", "{title}")]
    [InlineData("a }} b", "a } b")]
    [InlineData("{title", "{title")]
    public void A_template_written_by_hand_never_throws(string template, string expected) =>
        Assert.Equal(expected, Text(template, Plain));

    [Fact]
    public void An_unknown_field_next_to_a_known_one_leaves_the_known_one()
    {
        Assert.Equal("shell-deck", Text("{nobody}{folder}", Plain));
    }

    [Fact]
    public void Before_the_shell_reports_a_directory_the_folder_names_the_profile()
    {
        Assert.Equal("Ubuntu-26.04 \u00B7 ~", Text("{parent}{folder}", Plain with { Directory = null }));
    }

    [Fact]
    public void In_ssh_the_folder_is_the_connection_and_there_is_no_parent_or_path()
    {
        var ssh = Plain with { IsSsh = true, ProfileLocation = "me@host", Directory = "/home/me" };

        Assert.Equal(("SSH \u00B7 me@host", ""), (Text("{parent}{folder}", ssh), Text("{path}", ssh)));
    }

    [Fact]
    public void An_exited_title_is_muted()
    {
        var segment = TabLineTemplate.Parse("{title}").Render(Plain with { Status = TerminalStatus.Exited }).Single();

        Assert.Equal(SegmentStyle.Muted, segment.Style);
    }

    [Fact]
    public void Every_documented_field_is_known()
    {
        Assert.All(TabFields.Names, name => Assert.Equal(name, TabFields.Names.Single(known => known == name)));
        Assert.Equal("", Text("{" + TabFields.Names[^1] + "}", Plain)); // no throw for any name
        Assert.All(TabFields.Names, name => TabFields.Resolve(name, InRepo));
    }

    [Fact]
    public void There_are_always_three_templates()
    {
        Assert.Equal(
            (3, 3, 3, 3),
            (TabLineTemplates.From(null).Count, TabLineTemplates.From([]).Count, TabLineTemplates.From(["a"]).Count, TabLineTemplates.From(["a", "b", "c", "d", "e"]).Count));
    }

    [Fact]
    public void Missing_lines_are_empty_and_nothing_set_gives_the_default()
    {
        var shortList = TabLineTemplates.From(["{profile}"]);
        var defaults = TabLineTemplates.From(null);

        Assert.Equal(
            ("Ubuntu-26.04", 0, "Ubuntu-26.04"),
            (string.Concat(shortList[0].Render(Plain).Select(s => s.Text)), shortList[2].Render(Plain).Count, string.Concat(defaults[0].Render(Plain).Select(s => s.Text))));
    }
}

public sealed class TabPathTests
{
    [Theory]
    [InlineData("/home/me/proj", "me/", "proj")]
    [InlineData("/home/me/proj/", "me/", "proj")]
    [InlineData("/a", "/", "a")]
    [InlineData("/", "", "/")]
    [InlineData("~", "", "~")]
    [InlineData("~/x", "~/", "x")]
    [InlineData(@"C:\Users\me\x", @"me\", "x")]
    [InlineData("C:/Users/me/x", @"me\", "x")]
    [InlineData(@"C:\Users", @"C:\", "Users")]
    [InlineData(@"C:\", "", @"C:\")]
    [InlineData(@"C:\Users\", @"C:\", "Users")]
    [InlineData("", "", "")]
    [InlineData("   ", "", "")]
    [InlineData(null, "", "")]
    public void The_directory_is_cut_into_parent_and_folder(string? directory, string parent, string folder) =>
        Assert.Equal((parent, folder), TabPath.Split(directory));
}
