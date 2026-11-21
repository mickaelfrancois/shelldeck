using ShellDeck.Git;

namespace ShellDeck.Tests;

public sealed class GitStatusParserTests
{
    private const string Oid = "0123456789abcdef0123456789abcdef01234567";

    private static string Header(string head, string? upstream = null, string? ab = null, string oid = Oid)
    {
        string text = $"# branch.oid {oid}\n# branch.head {head}\n";
        if (upstream is not null) text += $"# branch.upstream {upstream}\n";
        if (ab is not null) text += $"# branch.ab {ab}\n";
        return text;
    }

    private static GitStatus Parse(string topLevel, string porcelain)
    {
        Assert.True(GitStatusParser.TryParse(topLevel, porcelain, out GitStatus status));
        return status;
    }

    [Fact]
    public void Ahead_and_behind_are_read_from_branch_ab()
    {
        GitStatus s = Parse("/home/u/shell-deck", Header("main", "origin/main", "+2 -1"));

        Assert.Equal(2, s.Ahead);
        Assert.Equal(1, s.Behind);
        Assert.Equal("main", s.Branch);
    }

    [Fact]
    public void A_branch_up_to_date_has_no_arrows()
    {
        GitStatus s = Parse("/home/u/shell-deck", Header("main", "origin/main", "+0 -0"));

        Assert.Equal(0, s.Ahead);
        Assert.Equal(0, s.Behind);
        Assert.Equal(("shell-deck", "main"), (s.RepoName, s.Branch));
    }

    [Theory]
    [InlineData("1 .M N... 100644 100644 100644 aaa bbb file.txt")]
    [InlineData("2 R. N... 100644 100644 100644 aaa bbb R100 new.txt\told.txt")]
    [InlineData("u UU N... 100644 100644 100644 100644 aaa bbb ccc file.txt")]
    [InlineData("? new.txt")]
    public void Any_changed_or_untracked_entry_makes_the_repo_dirty(string entry)
    {
        GitStatus s = Parse("/home/u/r", Header("main") + entry + "\n");

        Assert.True(s.IsDirty);
    }

    [Fact]
    public void A_clean_repo_is_not_dirty()
    {
        Assert.False(Parse("/home/u/r", Header("main")).IsDirty);
    }

    [Fact]
    public void A_branch_without_upstream_has_no_arrows()
    {
        GitStatus s = Parse("/home/u/r", Header("feature"));

        Assert.Equal("feature", s.Branch);
        Assert.Equal(0, s.Ahead);
        Assert.Equal(0, s.Behind);
    }

    [Fact]
    public void A_gone_upstream_has_no_arrows()
    {
        GitStatus s = Parse("/home/u/r", Header("feature", "origin/feature"));

        Assert.Equal(0, s.Ahead);
        Assert.Equal(0, s.Behind);
    }

    [Fact]
    public void A_detached_head_shows_the_short_sha()
    {
        GitStatus s = Parse("/home/u/r", Header("(detached)"));

        Assert.True(s.IsDetached);
        Assert.Equal(Oid[..7], s.Branch);
        Assert.Equal(0, s.Ahead);
        Assert.Equal(0, s.Behind);
    }

    [Fact]
    public void A_repo_without_commit_keeps_its_branch()
    {
        GitStatus s = Parse("/home/u/r", Header("main", oid: "(initial)"));

        Assert.Equal("main", s.Branch);
        Assert.False(s.IsDetached);
        Assert.Equal(0, s.Ahead);
        Assert.Equal(0, s.Behind);
    }

    [Theory]
    [InlineData("/home/u/shell-deck")]
    [InlineData("/home/u/shell-deck/")]
    [InlineData(@"C:\src\shell-deck\")]
    [InlineData(@"C:\src\shell-deck")]
    public void The_repo_name_is_the_last_folder_of_the_top_level(string topLevel)
    {
        Assert.Equal("shell-deck", Parse(topLevel, Header("main")).RepoName);
    }

    [Theory]
    [InlineData("/home/u/r", "")]
    [InlineData("/home/u/r", null)]
    [InlineData("/home/u/r", "# branch.oid abc\n")]
    [InlineData("/home/u/r", "# branch.oid abc\n# branch.head main\n# branch.ab +x -1\n")]
    [InlineData("/home/u/r", "# branch.oid abc\n# branch.head main\n# branch.ab +1\n")]
    [InlineData("", "# branch.oid abc\n# branch.head main\n")]
    [InlineData(null, "# branch.oid abc\n# branch.head main\n")]
    public void Empty_or_truncated_output_is_refused_without_exception(string? topLevel, string? porcelain)
    {
        Assert.False(GitStatusParser.TryParse(topLevel, porcelain, out _));
    }
}
