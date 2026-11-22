namespace ShellDeck.TabLines;

/// <summary>How a piece of a tab line is drawn.</summary>
public enum SegmentStyle
{
    Normal,
    Muted,
    Strong,
    GitDirty,
    GitAhead,
    GitBehind,
    Attention,
}

/// <summary>
/// A piece of a tab line. <paramref name="Shrink"/> says when the text gives way if the line is too long: 0 never,
/// then the highest number first. A <paramref name="Spacer"/> draws nothing: it pushes the segments after it to the right edge.
/// </summary>
public readonly record struct TabSegment(string Text, SegmentStyle Style = SegmentStyle.Muted, int Shrink = 0, bool Spacer = false);

/// <summary>One of the lines of a tab. The first one is the title and is drawn larger.</summary>
public sealed record TabLine(IReadOnlyList<TabSegment> Segments, bool IsPrimary)
{
    public string Text => string.Concat(Segments.Select(segment => segment.Text));
}
