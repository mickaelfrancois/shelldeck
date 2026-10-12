using ShellDeck.Terminal;

namespace ShellDeck.Tests;

public sealed class OutputHistoryTests
{
    [Fact]
    public void Output_under_the_capacity_is_kept_whole()
    {
        var history = new OutputHistory(capacity: 100);

        history.Append("PS C:\\> ");
        history.Append("dir\r\n");

        Assert.Equal("PS C:\\> dir\r\n", history.ToString());
    }

    [Fact]
    public void Past_the_capacity_the_oldest_lines_are_dropped_at_a_line_start()
    {
        var history = new OutputHistory(capacity: 20);

        history.Append("line one\r\nline two\r\nline three\r\n");

        Assert.Equal("line three\r\n", history.ToString());
    }

    [Fact]
    public void A_line_longer_than_the_capacity_is_dropped_rather_than_cut()
    {
        var history = new OutputHistory(capacity: 8);

        history.Append("\x1b[31mno line break here");

        Assert.Equal(string.Empty, history.ToString());
    }
}
