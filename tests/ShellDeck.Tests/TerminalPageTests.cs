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
