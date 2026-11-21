namespace ShellDeck.Tests;

public sealed class TabLinePanelTests
{
    // A fixed part, then a segment that shrinks last, then one that shrinks first.
    private static readonly int[] Shrink = [0, 1, 2];

    private static double[] Allocate(double available, double fixedWidth, double last, double first, double floor) =>
        ShellDeck.TabLinePanel.Allocate(available, [fixedWidth, last, first], Shrink, [0, 0, floor]);

    [Fact]
    public void Everything_fits_keeps_the_desired_widths() =>
        Assert.Equal([40, 60, 70], Allocate(200, 40, 60, 70, 8));

    [Fact]
    public void The_highest_shrink_gives_way_first() =>
        Assert.Equal([40, 60, 50], Allocate(150, 40, 60, 100, 8));

    [Fact]
    public void A_segment_stops_at_its_floor_then_the_next_shrinks() =>
        Assert.Equal([40, 32, 8], Allocate(80, 40, 60, 100, 8));

    [Fact]
    public void A_segment_never_goes_negative_when_the_fixed_parts_do_not_fit() =>
        Assert.Equal([40, 0, 8], Allocate(30, 40, 60, 100, 8));

    [Fact]
    public void A_short_segment_is_never_given_more_than_it_wants() =>
        Assert.Equal([40, 55, 5], Allocate(100, 40, 80, 5, 8));

    [Fact]
    public void A_segment_that_never_shrinks_keeps_its_width_whatever_the_room() =>
        Assert.Equal([40, 0, 0], ShellDeck.TabLinePanel.Allocate(10, [40, 20, 20], [0, 1, 2], [0, 0, 0]));
}
