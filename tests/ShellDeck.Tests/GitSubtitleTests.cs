using ShellDeck.Git;

namespace ShellDeck.Tests;

public sealed class GitSubtitleTests
{
    [Fact]
    public void The_subtitle_reads_repo_branch_dirty_ahead_behind()
    {
        var s = new GitStatus("shell-deck", "main", false, true, 2, 1);

        Assert.Equal("shell-deck \u2387 main \u25CF \u21912 \u21931", GitSubtitle.Format(s));
    }

    [Theory]
    [InlineData(false, 0, 0, "r \u2387 main")]
    [InlineData(false, 3, 0, "r \u2387 main \u21913")]
    [InlineData(false, 0, 4, "r \u2387 main \u21934")]
    [InlineData(true, 0, 0, "r \u2387 main \u25CF")]
    public void Markers_only_show_when_needed(bool dirty, int ahead, int behind, string expected)
    {
        var s = new GitStatus("r", "main", false, dirty, ahead, behind);

        Assert.Equal(expected, GitSubtitle.Format(s));
    }
}
