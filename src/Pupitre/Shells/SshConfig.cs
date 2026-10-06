using System.IO;

namespace Pupitre.Shells;

public sealed record SshHost(string Alias, string? HostName, string? User)
{
    /// <summary><c>user@host</c> as the connection resolves it; the alias stands for the host when no HostName is set.</summary>
    public string Target => User is null ? HostName ?? Alias : $"{User}@{HostName ?? Alias}";
}

/// <summary>Reads the connections declared in an OpenSSH client configuration file.</summary>
public static class SshConfig
{
    private static readonly char[] PatternCharacters = ['*', '?', '!'];

    public static string UserConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    public static IReadOnlyList<SshHost> ReadUserHosts() =>
        File.Exists(UserConfigPath) ? ParseHosts(File.ReadAllText(UserConfigPath)) : [];

    /// <summary>
    /// Returns one entry per concrete <c>Host</c> alias. Wildcard patterns (<c>Host *</c>) hold shared settings,
    /// not connections, so they are skipped; <c>Include</c> and <c>Match</c> are not followed.
    /// </summary>
    public static IReadOnlyList<SshHost> ParseHosts(string configContent)
    {
        var hosts = new List<SshHost>();
        List<string> currentAliases = [];
        string? hostName = null;
        string? user = null;

        void FlushCurrentBlock() =>
            hosts.AddRange(currentAliases.Select(alias => new SshHost(alias, hostName, user)));

        foreach (var line in configContent.Split('\n'))
        {
            if (!TryReadDirective(line, out var keyword, out var value))
            {
                continue;
            }

            switch (keyword.ToLowerInvariant())
            {
                case "host":
                    FlushCurrentBlock();
                    currentAliases = value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                        .Where(alias => alias.IndexOfAny(PatternCharacters) < 0)
                        .ToList();
                    hostName = null;
                    user = null;
                    break;
                case "match":
                    FlushCurrentBlock();
                    currentAliases = [];
                    break;
                case "hostname":
                    hostName ??= value;
                    break;
                case "user":
                    user ??= value;
                    break;
            }
        }

        FlushCurrentBlock();
        return hosts;
    }

    // OpenSSH accepts "Keyword value" and "Keyword=value"; the first occurrence of a keyword wins.
    private static bool TryReadDirective(string line, out string keyword, out string value)
    {
        var trimmed = line.Trim();
        keyword = value = string.Empty;
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return false;
        }

        var separator = trimmed.IndexOfAny([' ', '\t', '=']);
        if (separator < 0)
        {
            return false;
        }

        keyword = trimmed[..separator];
        value = trimmed[(separator + 1)..].Trim().TrimStart('=').Trim().Trim('"');
        return value.Length > 0;
    }
}
