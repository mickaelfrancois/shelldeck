using ShellDeck.Session;

namespace ShellDeck.Tests;

public sealed class WorkingDirectoryReportTests
{
    [Theory]
    [InlineData("file://host/C:/Users/me/My%20Dir", @"C:\Users\me\My Dir")]
    [InlineData("file:///C:/work", @"C:\work")]
    [InlineData("file://host/home/me/proj", "/home/me/proj")]
    [InlineData("file://localhost/home/me/My%20Proj", "/home/me/My Proj")]
    [InlineData(@"9;""C:\work""", @"C:\work")]
    [InlineData(@"9;C:\work", @"C:\work")]
    public void TryParse_accepts_a_reported_directory(string data, string expected)
    {
        Assert.True(WorkingDirectoryReport.TryParse(data, out string path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("9;4;1;50")]
    [InlineData("9;texte")]
    [InlineData("4;1;50")]
    [InlineData("texte")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_refuses_progress_and_plain_text(string? data)
    {
        Assert.False(WorkingDirectoryReport.TryParse(data, out string path));
        Assert.Equal(string.Empty, path);
    }

    [Theory]
    [InlineData("file://host/C:/a%22b")]
    [InlineData("file://host/home/me%0Aevil")]
    [InlineData("file://host/home/me%1B[0m")]
    [InlineData("file://host")]
    [InlineData("file://host/home/me?x=1")]
    [InlineData("http://host/home/me")]
    [InlineData("ftp://host/home/me")]
    [InlineData("9;\"C:\\a\"b\"")]
    [InlineData("9;C:\\a\tb")]
    [InlineData(@"9;relative\dir")]
    [InlineData(@"9;..\up")]
    [InlineData("9;\"\"")]
    public void TryParse_refuses_unsafe_or_relative_paths(string data)
    {
        Assert.False(WorkingDirectoryReport.TryParse(data, out _));
    }
}
