using System.Text;

namespace ShellDeck.Terminal;

/// <summary>
/// The latest output of a shell, replayed into a terminal page that starts empty: a view rebuilt by WPF,
/// or a page reloaded after a renderer crash.
/// </summary>
public sealed class OutputHistory(int capacity)
{
    private readonly StringBuilder _text = new();

    public void Append(ReadOnlySpan<char> text)
    {
        _text.Append(text);
        if (_text.Length <= capacity)
        {
            return;
        }

        // Trimmed by a quarter at once, so a flood does not shift the whole text on every read.
        // Cut at a line start rather than mid-line, where an escape sequence would replay as garbage.
        var cut = _text.Length - capacity * 3 / 4;
        while (cut < _text.Length && _text[cut - 1] != '\n')
        {
            cut++;
        }

        _text.Remove(0, cut);
    }

    public override string ToString() => _text.ToString();
}
