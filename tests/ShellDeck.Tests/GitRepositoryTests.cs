using ShellDeck.Git;

namespace ShellDeck.Tests;

public sealed class GitRepositoryTests
{
    [Theory]
    [InlineData(@"C:\src\repo\a\b", @"C:\src\repo")]
    [InlineData(@"C:\src\repo", @"C:\src\repo")]
    public void The_root_is_the_nearest_folder_holding_dot_git(string directory, string expected)
    {
        string? root = GitRepository.FindRoot(directory, p => p == @"C:\src\repo");

        Assert.Equal(expected, root);
    }

    [Fact]
    public void The_nearest_dot_git_wins_over_an_outer_one()
    {
        string? root = GitRepository.FindRoot(@"C:\src\repo\sub\deep", p => p is @"C:\src\repo" or @"C:\src\repo\sub");

        Assert.Equal(@"C:\src\repo\sub", root);
    }

    [Fact]
    public void No_dot_git_up_to_the_drive_root_gives_null()
    {
        Assert.Null(GitRepository.FindRoot(@"C:\src\repo\a", _ => false));
    }
}
