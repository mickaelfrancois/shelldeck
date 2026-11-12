using System.IO;

namespace ShellDeck.Tests;

public sealed class TerminalPageTests
{
    [Fact]
    public void The_page_enables_unicode_11_and_reports_the_conpty_build()
    {
        string wwwroot = FindWwwroot();
        string html = File.ReadAllText(Path.Combine(wwwroot, "terminal.html"));

        Assert.Contains("<script src=\"addon-unicode11.js\"", html);
        Assert.True(File.Exists(Path.Combine(wwwroot, "addon-unicode11.js")), "addon-unicode11.js is not vendored");
        Assert.Contains("allowProposedApi: true", html);
        Assert.Contains("unicode.activeVersion = '11'", html);
        Assert.Contains("conptyBuild", html);
    }

    [Fact]
    public void The_page_loads_the_vendored_search_addon()
    {
        string wwwroot = FindWwwroot();
        string html = File.ReadAllText(Path.Combine(wwwroot, "terminal.html"));
        string notices = File.ReadAllText(Path.Combine(wwwroot, "..", "..", "..", "THIRD_PARTY_NOTICES.md"));

        Assert.Contains("<script src=\"addon-search.js\"", html);
        Assert.True(File.Exists(Path.Combine(wwwroot, "addon-search.js")), "addon-search.js is not vendored");
        Assert.Contains("new SearchAddon.SearchAddon()", html);
        Assert.Matches(@"@xterm/addon-search` \d+\.\d+\.\d+", notices);
    }

    [Fact]
    public void The_page_exposes_the_search_overlay()
    {
        string html = File.ReadAllText(Path.Combine(FindWwwroot(), "terminal.html"));

        Assert.Contains("id=\"search\"", html);
        Assert.Contains("window.openSearch", html);
        Assert.Contains("findPrevious", html);
        Assert.Contains("clearDecorations", html);
        Assert.Contains("onDidChangeResults", html);
        Assert.Contains("new RegExp", html);
    }

    [Fact]
    public void The_page_never_lets_an_invalid_regex_escape_the_search()
    {
        string html = File.ReadAllText(Path.Combine(FindWwwroot(), "terminal.html"));

        Assert.Contains("isPressed(searchCase) ? 'g' : 'gi'", html);
        Assert.DoesNotContain("'iu'", html);
        int find = html.IndexOf("found = backwards", StringComparison.Ordinal);
        int tryBlock = html.LastIndexOf("try {", find, StringComparison.Ordinal);
        int catchBlock = html.IndexOf("catch", find, StringComparison.Ordinal);
        Assert.True(find > 0 && tryBlock > html.LastIndexOf("function runSearch", find, StringComparison.Ordinal), "findNext/findPrevious is not wrapped in try");
        Assert.True(catchBlock > find && html.IndexOf("Regex invalide", catchBlock, StringComparison.Ordinal) > catchBlock);
    }

    [Fact]
    public void The_page_tracks_osc_133_prompt_marks()
    {
        string html = File.ReadAllText(Path.Combine(FindWwwroot(), "terminal.html"));

        Assert.Contains("registerOscHandler(133", html);
        Assert.Contains("registerMarker", html);
        Assert.Contains("scrollToLine", html);
        Assert.Contains("type !== 'normal'", html);
        Assert.Contains("'ArrowUp'", html);
        Assert.Contains("if (prompt) commands.push({ prompt })", html);
    }

    [Fact]
    public void The_page_reports_the_working_directory_from_osc_7_and_osc_9_9()
    {
        string html = File.ReadAllText(Path.Combine(FindWwwroot(), "terminal.html"));

        Assert.Contains("registerOscHandler(7", html);
        Assert.Contains("type: 'cwd'", html);
        Assert.Contains("data.startsWith('9;')", html);
        Assert.Contains("type: 'notification', data", html);
        Assert.Contains("!/^\\d+;/.test(data)", html);
    }

    [Fact]
    public void The_page_selects_command_output_and_marks_exit_codes()
    {
        string html = File.ReadAllText(Path.Combine(FindWwwroot(), "terminal.html"));

        Assert.Contains("selectLines", html);
        Assert.Contains("registerDecoration", html);
        Assert.Contains("'KeyO'", html);
    }

    private static string FindWwwroot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            string candidate = Path.Combine(directory, "src", "ShellDeck", "wwwroot");
            if (File.Exists(Path.Combine(candidate, "terminal.html")))
                return candidate;
            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException("src/ShellDeck/wwwroot not found above " + AppContext.BaseDirectory);
    }
}
