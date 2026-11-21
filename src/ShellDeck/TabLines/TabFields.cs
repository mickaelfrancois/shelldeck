namespace ShellDeck.TabLines;

/// <summary>The fields a line template can use, between braces. A name nobody knows gives nothing.</summary>
public static class TabFields
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "title", "profile", "folder", "parent", "path",
        "git", "git.repo", "git.branch", "git.dirty", "git.ahead", "git.behind",
    ];

    private static readonly IReadOnlyList<TabSegment> None = [];

    /// <summary>The segments of <paramref name="field"/> for <paramref name="tab"/>; empty when there is nothing to show.</summary>
    public static IReadOnlyList<TabSegment> Resolve(string field, TabLineContext tab) => field.ToLowerInvariant() switch
    {
        "title" => Text(tab.Title, tab.Status is ViewModels.TerminalStatus.Exited ? SegmentStyle.Muted : SegmentStyle.Normal, 1),
        "profile" => Text(tab.ProfileName, SegmentStyle.Muted, 1),
        "folder" => Folder(tab),
        "parent" => Parent(tab),
        "path" => tab.IsSsh ? None : Text(tab.Directory, SegmentStyle.Muted, 1),
        "git" => Git(tab),
        "git.repo" => tab.Git is { } git ? Text(git.RepoName, SegmentStyle.Muted, 1) : None,
        "git.branch" => tab.Git is { } git ? Text(git.Branch, SegmentStyle.Muted, 2) : None,
        "git.dirty" => tab.Git is { IsDirty: true } ? Text(" ●", SegmentStyle.GitDirty, 0) : None,
        "git.ahead" => tab.Git is { Ahead: > 0 } git ? Text($" ↑{git.Ahead}", SegmentStyle.GitAhead, 0) : None,
        "git.behind" => tab.Git is { Behind: > 0 } git ? Text($" ↓{git.Behind}", SegmentStyle.GitBehind, 0) : None,
        var name => tab.Extra is not null && tab.Extra.TryGetValue(name, out var extra) ? [extra] : None,
    };

    /// <summary>
    /// The folder the shell is in. Before it reported one: the connection for SSH, else the profile and its starting
    /// place, so the line is never empty.
    /// </summary>
    private static IReadOnlyList<TabSegment> Folder(TabLineContext tab)
    {
        if (tab.IsSsh)
        {
            return Text($"SSH · {tab.ProfileLocation}", SegmentStyle.Muted, 1);
        }

        var (_, current) = TabPath.Split(tab.Directory);
        return current.Length > 0
            ? Text(current, SegmentStyle.Strong, 1)
            : Text($"{tab.ProfileName} · {tab.ProfileLocation}", SegmentStyle.Muted, 1);
    }

    private static IReadOnlyList<TabSegment> Parent(TabLineContext tab)
    {
        if (tab.IsSsh)
        {
            return None;
        }

        var (parent, _) = TabPath.Split(tab.Directory);
        return Text(parent, SegmentStyle.Muted, 2);
    }

    private static IReadOnlyList<TabSegment> Git(TabLineContext tab)
    {
        if (tab.Git is not { } git)
        {
            return None;
        }

        List<TabSegment> segments = [new("⎇ ", SegmentStyle.Muted), new(git.Branch, SegmentStyle.Muted, 2)];
        segments.AddRange(Resolve("git.dirty", tab));
        segments.AddRange(Resolve("git.ahead", tab));
        segments.AddRange(Resolve("git.behind", tab));
        return segments;
    }

    private static IReadOnlyList<TabSegment> Text(string? text, SegmentStyle style, int shrink) =>
        string.IsNullOrEmpty(text) ? None : [new TabSegment(text, style, shrink)];
}
