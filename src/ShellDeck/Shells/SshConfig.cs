using System.IO;
using System.Text;

namespace ShellDeck.Shells;

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

    /// <summary>An unreadable config (locked by an editor, denied) means no connections, not a failed startup.</summary>
    public static IReadOnlyList<SshHost> ReadUserHosts()
    {
        try
        {
            return File.Exists(UserConfigPath) ? ParseHosts(File.ReadAllText(UserConfigPath)) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Returns one entry per concrete <c>Host</c> alias. Wildcard patterns (<c>Host *</c>) hold shared settings,
    /// not connections: they are skipped, but their settings apply to the aliases they match, the first value
    /// found winning as in OpenSSH. <c>Include</c> and <c>Match</c> are not followed.
    /// </summary>
    public static IReadOnlyList<SshHost> ParseHosts(string configContent)
    {
        var blocks = new List<HostBlock>();
        HostBlock? current = null;

        foreach (var line in configContent.Split('\n'))
        {
            if (!TryReadDirective(line, out var keyword, out var arguments))
            {
                continue;
            }

            switch (keyword.ToLowerInvariant())
            {
                case "host":
                    current = new HostBlock(arguments);
                    blocks.Add(current);
                    break;
                case "match":
                    current = null;
                    break;
                case "hostname" when current is not null:
                    current.HostName ??= arguments[0];
                    break;
                case "user" when current is not null:
                    current.User ??= arguments[0];
                    break;
            }
        }

        // An alias starting with '-' would reach ssh as an option.
        var aliases = blocks
            .SelectMany(block => block.Patterns)
            .Where(alias => alias.IndexOfAny(PatternCharacters) < 0 && !alias.StartsWith('-'))
            .Distinct(StringComparer.Ordinal);

        return aliases
            .Select(alias =>
            {
                var matching = blocks.Where(block => block.Matches(alias)).ToList();
                return new SshHost(
                    alias,
                    matching.Select(block => block.HostName).FirstOrDefault(value => value is not null),
                    matching.Select(block => block.User).FirstOrDefault(value => value is not null));
            })
            .ToList();
    }

    // OpenSSH accepts "Keyword value" and "Keyword=value"; arguments may be double-quoted, and a '#'
    // starting an argument comments out the rest of the line.
    private static bool TryReadDirective(string line, out string keyword, out IReadOnlyList<string> arguments)
    {
        var trimmed = line.Trim();
        keyword = string.Empty;
        arguments = [];
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
        arguments = SplitArguments(trimmed[(separator + 1)..].Trim().TrimStart('=').Trim());
        return arguments.Count > 0;
    }

    private static List<string> SplitArguments(string value)
    {
        var arguments = new List<string>();
        var argument = new StringBuilder();
        var isQuoted = false;
        var hasArgument = false;

        foreach (var character in value)
        {
            if (character == '"')
            {
                isQuoted = !isQuoted;
                hasArgument = true;
            }
            else if (!isQuoted && character is ' ' or '\t')
            {
                Flush();
            }
            else if (!isQuoted && character == '#' && !hasArgument)
            {
                break;
            }
            else
            {
                argument.Append(character);
                hasArgument = true;
            }
        }

        Flush();
        return arguments;

        void Flush()
        {
            if (hasArgument && argument.Length > 0)
            {
                arguments.Add(argument.ToString());
            }

            argument.Clear();
            hasArgument = false;
        }
    }

    private sealed class HostBlock(IReadOnlyList<string> patterns)
    {
        public IReadOnlyList<string> Patterns { get; } = patterns;

        public string? HostName { get; set; }

        public string? User { get; set; }

        /// <summary>A negated pattern (<c>!name</c>) that matches excludes the alias, whatever the other patterns say.</summary>
        public bool Matches(string alias)
        {
            var matched = false;
            foreach (var pattern in Patterns)
            {
                if (pattern.StartsWith('!'))
                {
                    if (Glob(pattern[1..], alias))
                    {
                        return false;
                    }
                }
                else if (Glob(pattern, alias))
                {
                    matched = true;
                }
            }

            return matched;
        }

        private static bool Glob(string pattern, string text) =>
            System.Text.RegularExpressions.Regex.IsMatch(
                text,
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }
}
