namespace ShellDeck.Git;

/// <summary>Plain-text rendering of a <see cref="GitStatus"/> for the tab subtitle.</summary>
public static class GitSubtitle
{
    public static string Format(GitStatus s)
    {
        string text = $"{s.RepoName} \u2387 {s.Branch}";
        if (s.IsDirty)
            text += " \u25CF";
        if (s.Ahead > 0)
            text += $" \u2191{s.Ahead}";
        if (s.Behind > 0)
            text += $" \u2193{s.Behind}";
        return text;
    }
}
