using System.Globalization;

namespace ShellDeck.Git;

/// <summary>Parses <c>git status --porcelain=v2 --branch</c> output. Never throws.</summary>
public static class GitStatusParser
{
    private const int ShortShaLength = 7;

    public static bool TryParse(string? topLevel, string? porcelain, out GitStatus status)
    {
        status = null!;
        if (string.IsNullOrWhiteSpace(topLevel) || string.IsNullOrWhiteSpace(porcelain))
            return false;

        string repoName = LastSegment(topLevel);
        if (repoName.Length == 0)
            return false;

        string? head = null;
        string? oid = null;
        int ahead = 0, behind = 0;
        bool dirty = false;

        foreach (string raw in porcelain.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                head = line["# branch.head ".Length..].Trim();
            else if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
                oid = line["# branch.oid ".Length..].Trim();
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                if (!TryParseAheadBehind(line["# branch.ab ".Length..], out ahead, out behind))
                    return false;
            }
            else if (line.StartsWith("1 ", StringComparison.Ordinal) || line.StartsWith("2 ", StringComparison.Ordinal)
                  || line.StartsWith("u ", StringComparison.Ordinal) || line.StartsWith("? ", StringComparison.Ordinal))
                dirty = true;
        }

        if (string.IsNullOrEmpty(head))
            return false;

        if (head == "(detached)")
        {
            if (string.IsNullOrEmpty(oid) || oid == "(initial)")
                return false;
            string sha = oid.Length > ShortShaLength ? oid[..ShortShaLength] : oid;
            status = new GitStatus(repoName, sha, true, dirty, 0, 0);
            return true;
        }

        status = new GitStatus(repoName, head, false, dirty, ahead, behind);
        return true;
    }

    private static bool TryParseAheadBehind(string text, out int ahead, out int behind)
    {
        ahead = 0;
        behind = 0;
        string[] parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0][0] != '+' || parts[1][0] != '-')
            return false;
        return int.TryParse(parts[0].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out ahead)
            && int.TryParse(parts[1].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out behind);
    }

    private static string LastSegment(string path)
    {
        string trimmed = path.Trim().TrimEnd('/', '\\');
        int i = trimmed.LastIndexOfAny(['/', '\\']);
        return i < 0 ? trimmed : trimmed[(i + 1)..];
    }
}
