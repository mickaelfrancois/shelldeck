using System.IO;
using System.Text.Json;
using ShellDeck.Shells;
using ShellDeck.TabLines;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

/// <summary>The files an agent or a user reads to configure ShellDeck must keep up with the code.</summary>
public sealed class DocumentationTests
{
    private static string Repo
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ShellDeck.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static JsonElement Json(string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo, relativePath))).RootElement;

    [Fact]
    public void The_schema_describes_every_setting()
    {
        var schemaKeys = Json("docs/settings.schema.json").GetProperty("properties").EnumerateObject().Select(property => property.Name).ToHashSet();
        var settingsKeys = typeof(AppSettings).GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name));

        var missing = settingsKeys.Where(key => !schemaKeys.Contains(key)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void The_schema_enums_match_the_code()
    {
        var properties = Json("docs/settings.schema.json").GetProperty("properties");
        string[] Enum(string name) => [.. properties.GetProperty(name).GetProperty("enum").EnumerateArray().Select(value => value.GetString()!)];

        Assert.Equal(Names<PanelSide>(), Enum("panelSide"));
        Assert.Equal(Names<RestoreSession>(), Enum("restoreSession"));
        Assert.Equal(Names<PaneTemplate>(), Enum("defaultTemplate"));
    }

    private static string[] Names<T>() where T : struct, Enum => [.. Enum.GetNames<T>().Select(JsonNamingPolicy.CamelCase.ConvertName)];

    [Fact]
    public void The_example_input_has_what_the_tab_command_really_receives()
    {
        var shell = new ShellProfile("Ubuntu-26.04", "wsl.exe", ShellKind.Wsl, "~");
        var tab = new TerminalTab(shell);
        tab.MainPane.WorkingDirectory = "/home/me";
        tab.ApplyGitStatus(new ShellDeck.Git.GitStatus("r", "main", false, false, 0, 0));
        var real = JsonDocument.Parse(TabCommandInput.Build(tab)).RootElement;
        var example = Json("examples/tab-input.json");

        Assert.Equal(Keys(real), Keys(example));
        Assert.Equal(Keys(real.GetProperty("profile")), Keys(example.GetProperty("profile")));
        Assert.Equal(Keys(real.GetProperty("git")), Keys(example.GetProperty("git")));
    }

    private static string[] Keys(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name).Order()];

    [Fact]
    public void The_guide_and_the_readme_name_every_field()
    {
        var guide = File.ReadAllText(Path.Combine(Repo, "docs/configurer-les-onglets.md"));
        var readme = File.ReadAllText(Path.Combine(Repo, "README.md"));

        var missingFromGuide = TabFields.Names.Where(name => !guide.Contains("{" + name + "}")).ToList();
        var missingFromReadme = TabFields.Names.Where(name => !readme.Contains("{" + name + "}")).ToList();
        Assert.Empty(missingFromGuide);
        Assert.Empty(missingFromReadme);
    }

    [Fact]
    public void The_guide_names_every_style_and_every_status()
    {
        var guide = File.ReadAllText(Path.Combine(Repo, "docs/configurer-les-onglets.md"));
        var styles = new[] { "normal", "muted", "strong", "attention", "dirty", "ahead", "behind" };
        var statuses = Enum.GetNames<TerminalStatus>().Select(name => name.ToLowerInvariant());

        var missing = styles.Concat(statuses).Where(word => !guide.Contains($"`{word}`")).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void The_example_script_is_in_the_repository()
    {
        Assert.True(File.Exists(Path.Combine(Repo, "examples/claude-tab.py")));
    }
}
