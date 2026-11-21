namespace ShellDeck.TabLines;

/// <summary>The three lines of a tab, from the <c>tabLines</c> setting.</summary>
public static class TabLineTemplates
{
    public const int LineCount = 3;

    public static IReadOnlyList<string> Default { get; } = ["{title}", "{parent}{folder}", "{git}"];

    /// <summary>
    /// Always <see cref="LineCount"/> templates, so tabs keep one height: missing lines are empty, extra ones ignored.
    /// Nothing set (null) gives the default lines.
    /// </summary>
    public static IReadOnlyList<TabLineTemplate> From(IReadOnlyList<string>? lines)
    {
        var source = lines ?? Default;
        return [.. Enumerable.Range(0, LineCount).Select(index => TabLineTemplate.Parse(index < source.Count ? source[index] : null))];
    }
}
