using Pupitre.Shells;

namespace Pupitre.Tests;

public sealed class SshConfigTests
{
    [Fact]
    public void Each_host_block_becomes_a_connection_with_its_user_and_host_name()
    {
        const string config = """
            Host docker01
                HostName 192.0.2.21
                User deploy

            Host db03
              HostName=db03.example.com
              User=admin
            """;

        var hosts = SshConfig.ParseHosts(config);

        Assert.Equal(
            [new SshHost("docker01", "192.0.2.21", "deploy"), new SshHost("db03", "db03.example.com", "admin")],
            hosts);
    }

    [Fact]
    public void Wildcard_patterns_are_shared_settings_not_connections()
    {
        const string config = """
            Host *
                User everyone
            Host web-* bastion
                HostName bastion.example.com
            """;

        var hosts = SshConfig.ParseHosts(config);

        Assert.Equal([new SshHost("bastion", "bastion.example.com", "everyone")], hosts);
    }

    [Fact]
    public void The_first_value_found_wins_across_all_matching_blocks()
    {
        const string config = """
            Host web
                HostName web.example.com
            Host web-* !web-old
                User deploy
            Host web-01 web-old
                HostName 10.0.0.1
            Host web
                HostName ignored.example.com
                User admin
            """;

        var hosts = SshConfig.ParseHosts(config);

        Assert.Equal(
            [
                new SshHost("web", "web.example.com", "admin"),
                new SshHost("web-01", "10.0.0.1", "deploy"),
                new SshHost("web-old", "10.0.0.1", null),
            ],
            hosts);
    }

    [Fact]
    public void Trailing_comments_and_quotes_are_not_part_of_the_aliases()
    {
        const string config = """
            Host prod "staging" # main servers
                HostName "prod.example.com" # primary
            Host -oProxyCommand=evil
            """;

        var hosts = SshConfig.ParseHosts(config);

        Assert.Equal(
            [new SshHost("prod", "prod.example.com", null), new SshHost("staging", "prod.example.com", null)],
            hosts);
    }

    [Fact]
    public void Settings_after_a_match_block_do_not_leak_into_the_previous_host()
    {
        const string config = """
            Host nginx
                HostName 10.0.0.5
            Match user root
                User root
            """;

        var hosts = SshConfig.ParseHosts(config);

        Assert.Equal([new SshHost("nginx", "10.0.0.5", null)], hosts);
    }

    [Theory]
    [InlineData("deploy", "10.0.0.1", "deploy@10.0.0.1")]
    [InlineData(null, "10.0.0.1", "10.0.0.1")]
    [InlineData("deploy", null, "deploy@agent")]
    public void Target_falls_back_to_the_alias_when_no_host_name_is_set(string? user, string? hostName, string expectedTarget)
    {
        var host = new SshHost("agent", hostName, user);

        Assert.Equal(expectedTarget, host.Target);
    }
}
