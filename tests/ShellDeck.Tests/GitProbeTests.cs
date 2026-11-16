using ShellDeck.Git;

namespace ShellDeck.Tests;

public sealed class GitProbeTests
{
    private const string Porcelain = "# branch.oid 0123456789abcdef0123456789abcdef01234567\n# branch.head main\n";

    [Fact]
    public void A_windows_result_takes_its_root_from_the_known_top_level()
    {
        var result = GitProbe.Interpret(0, Porcelain, @"C:\src\shell-deck");

        var ok = Assert.IsType<GitProbeResult.Ok>(result);
        Assert.Equal(@"C:\src\shell-deck", ok.TopLevel);
        Assert.Equal("shell-deck", ok.Status.RepoName);
        Assert.Equal("main", ok.Status.Branch);
    }

    [Fact]
    public void A_wsl_result_takes_its_root_from_the_first_line()
    {
        var result = GitProbe.Interpret(0, "/home/u/shell-deck\n" + Porcelain, null);

        var ok = Assert.IsType<GitProbeResult.Ok>(result);
        Assert.Equal("/home/u/shell-deck", ok.TopLevel);
        Assert.Equal("shell-deck", ok.Status.RepoName);
    }

    [Theory]
    [InlineData(3, "")]
    [InlineData(128, "")]
    public void Exit_codes_3_and_128_mean_not_a_repo(int exitCode, string stdout)
    {
        Assert.IsType<GitProbeResult.NotARepo>(GitProbe.Interpret(exitCode, stdout, null));
    }

    [Theory]
    [InlineData(1, "/r\n" + Porcelain)]
    [InlineData(127, "")]
    [InlineData(0, "")]
    [InlineData(0, "/r\nnot porcelain")]
    [InlineData(0, "/r")]
    public void Any_other_code_or_unreadable_output_is_a_failure(int exitCode, string stdout)
    {
        Assert.IsType<GitProbeResult.Failed>(GitProbe.Interpret(exitCode, stdout, null));
    }

    [Fact]
    public void A_windows_failure_with_unreadable_output_is_a_failure()
    {
        Assert.IsType<GitProbeResult.Failed>(GitProbe.Interpret(0, "garbage", @"C:\r"));
    }
}
