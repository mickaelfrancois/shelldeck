using ShellDeck.Terminal;

namespace ShellDeck.Tests;

public sealed class WebLinkTests
{
    [Theory]
    [InlineData("https://claude.ai/code/session_01")]
    [InlineData("http://localhost:5021/healthz")]
    public void Web_addresses_open_in_the_browser(string text)
    {
        var isWebLink = WebLink.TryParse(text, out _);

        Assert.True(isWebLink);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a link")]
    public void Other_schemes_are_refused_because_terminal_output_is_untrusted(string text)
    {
        var isWebLink = WebLink.TryParse(text, out _);

        Assert.False(isWebLink);
    }
}
