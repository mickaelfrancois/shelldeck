using System.Windows;

namespace Pupitre.Tests;

public sealed class WindowPlacementTests
{
    // Main monitor with a 40 px taskbar, a second one on its right.
    private static readonly Rect Main = new(0, 0, 1920, 1040);
    private static readonly Rect Right = new(1920, 0, 1920, 1040);

    [Fact]
    public void A_window_on_a_connected_monitor_keeps_its_place()
    {
        var placement = new WindowPlacement(2000, 100, 1200, 750, IsMaximized: false);

        Assert.Same(placement, placement.KeepOnScreen([Main, Right]));
    }

    [Fact]
    public void A_window_left_on_an_unplugged_monitor_moves_to_the_remaining_one()
    {
        var placement = new WindowPlacement(2000, 100, 1200, 750, IsMaximized: false);

        Assert.Equal(new WindowPlacement(720, 100, 1200, 750, IsMaximized: false), placement.KeepOnScreen([Main]));
    }

    [Fact]
    public void A_maximized_window_on_an_unplugged_monitor_maximizes_on_the_remaining_one()
    {
        var placement = new WindowPlacement(2000, 100, 1200, 750, IsMaximized: true);

        Assert.Equal(new WindowPlacement(720, 100, 1200, 750, IsMaximized: true), placement.KeepOnScreen([Main]));
    }

    [Fact]
    public void A_window_larger_than_the_remaining_monitor_is_shrunk_to_fit()
    {
        var placement = new WindowPlacement(1920, 0, 2560, 1400, IsMaximized: false);

        Assert.Equal(new WindowPlacement(0, 0, 1920, 1040, IsMaximized: false), placement.KeepOnScreen([Main]));
    }

    [Fact]
    public void A_window_with_its_title_bar_above_the_screens_is_brought_back()
    {
        // A monitor above the main one was unplugged: the bottom of the window shows, its title bar does not.
        var placement = new WindowPlacement(300, -900, 1200, 1000, IsMaximized: false);

        Assert.Equal(new WindowPlacement(300, 0, 1200, 1000, IsMaximized: false), placement.KeepOnScreen([Main, Right]));
    }

    [Fact]
    public void A_window_with_only_a_sliver_of_title_bar_on_screen_is_brought_back()
    {
        var placement = new WindowPlacement(3820, 100, 1200, 750, IsMaximized: false);

        Assert.Equal(new WindowPlacement(2640, 100, 1200, 750, IsMaximized: false), placement.KeepOnScreen([Main, Right]));
    }

    [Theory]
    [InlineData(double.NaN, 0, 800, 600)]
    [InlineData(0, 0, -1, 600)]
    [InlineData(0, 0, 800, 0)]
    public void A_hand_edited_placement_with_nonsense_is_ignored(double left, double top, double width, double height)
    {
        Assert.False(new WindowPlacement(left, top, width, height, IsMaximized: false).IsValid);
    }
}
