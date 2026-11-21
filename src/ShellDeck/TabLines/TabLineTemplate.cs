namespace ShellDeck.TabLines;

/// <summary>
/// A line of a tab as the user writes it: text and <c>{fields}</c>. <c>{{</c> and <c>}}</c> stand for a brace.
/// The file is edited by hand and reloaded at once, so nothing here throws: a field nobody knows is empty and
/// an unclosed brace stays text.
/// </summary>
public sealed class TabLineTemplate
{
    private readonly record struct Token(string? Literal, string? Field);

    private readonly IReadOnlyList<Token> _tokens;

    private TabLineTemplate(IReadOnlyList<Token> tokens) => _tokens = tokens;

    public static TabLineTemplate Empty { get; } = new([]);

    public static TabLineTemplate Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Empty;
        }

        List<Token> tokens = [];
        var literal = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
            {
                literal.Append(c);
                i++;
                continue;
            }

            if (c == '{' && text.IndexOf('}', i + 1) is var close and >= 0 && text[(i + 1)..close].Trim() is { Length: > 0 } name)
            {
                if (literal.Length > 0)
                {
                    tokens.Add(new Token(literal.ToString(), null));
                    literal.Clear();
                }

                tokens.Add(new Token(null, name));
                i = close;
                continue;
            }

            literal.Append(c);
        }

        if (literal.Length > 0)
        {
            tokens.Add(new Token(literal.ToString(), null));
        }

        return new TabLineTemplate(tokens);
    }

    /// <summary>
    /// The segments of the line. A line that uses fields and finds nothing in any of them is empty, so a template like
    /// <c>⎇ {git.branch}</c> leaves no stray symbol outside a repository.
    /// </summary>
    public IReadOnlyList<TabSegment> Render(TabLineContext tab)
    {
        List<TabSegment> segments = [];
        var hasField = false;
        var anyFieldFilled = false;
        foreach (var token in _tokens)
        {
            if (token.Field is { } field)
            {
                hasField = true;
                var resolved = TabFields.Resolve(field, tab);
                anyFieldFilled |= resolved.Count > 0;
                segments.AddRange(resolved);
            }
            else
            {
                segments.Add(new TabSegment(token.Literal!, SegmentStyle.Muted));
            }
        }

        return hasField && !anyFieldFilled ? [] : segments;
    }
}
