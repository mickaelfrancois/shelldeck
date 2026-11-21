namespace ShellDeck.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.4.1", "v1.4.1")]
    [InlineData("1.4.1+3f2a9c0d", "v1.4.1")]
    [InlineData("1.5.0-beta.1+abc", "v1.5.0-beta.1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void The_footer_shows_the_version_without_the_commit_suffix(string? informational, string expected) =>
        Assert.Equal(expected, AppVersion.Format(informational));
}
