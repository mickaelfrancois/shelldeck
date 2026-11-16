using Xunit;

namespace ShellDeck.Tests;

public class GitSubtitleLayoutTests
{
    [Fact]
    public void Everything_fits_keeps_the_desired_widths()
    {
        var (repo, branch) = GitSubtitlePanel.Allocate(200, 40, 60, 70, 8);

        Assert.Equal(60, repo);
        Assert.Equal(70, branch);
    }

    [Fact]
    public void The_branch_shrinks_before_the_repo()
    {
        var (repo, branch) = GitSubtitlePanel.Allocate(150, 40, 60, 100, 8);

        Assert.Equal(60, repo);
        Assert.Equal(50, branch);
    }

    [Fact]
    public void The_branch_stops_at_its_floor_then_the_repo_shrinks()
    {
        var (repo, branch) = GitSubtitlePanel.Allocate(80, 40, 60, 100, 8);

        Assert.Equal(32, repo);
        Assert.Equal(8, branch);
    }

    [Fact]
    public void The_repo_never_goes_negative_when_the_fixed_parts_do_not_fit()
    {
        var (repo, branch) = GitSubtitlePanel.Allocate(30, 40, 60, 100, 8);

        Assert.Equal(0, repo);
        Assert.Equal(8, branch);
    }

    [Fact]
    public void A_short_branch_is_never_given_more_than_it_wants()
    {
        var (repo, branch) = GitSubtitlePanel.Allocate(100, 40, 80, 5, 8);

        Assert.Equal(55, repo);
        Assert.Equal(5, branch);
    }
}
