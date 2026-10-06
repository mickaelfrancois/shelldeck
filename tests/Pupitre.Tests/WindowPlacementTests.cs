using System.Windows;

namespace Pupitre.Tests;

public sealed class WindowPlacementTests
{
    private static readonly Rect TwoMonitors = new(0, 0, 3840, 1080);

    [Fact]
    public void A_window_on_a_connected_monitor_is_restored()
    {
        var placement = new WindowPlacement(2000, 100, 1200, 750, IsMaximized: false);

        Assert.True(placement.IsVisibleOn(TwoMonitors));
    }

    [Fact]
    public void A_window_left_on_an_unplugged_monitor_is_not_restored()
    {
        var placement = new WindowPlacement(4000, 100, 1200, 750, IsMaximized: false);

        Assert.False(placement.IsVisibleOn(TwoMonitors));
    }

    [Fact]
    public void A_window_with_only_a_sliver_on_screen_is_not_restored()
    {
        var placement = new WindowPlacement(3820, 100, 1200, 750, IsMaximized: false);

        Assert.False(placement.IsVisibleOn(TwoMonitors));
    }
}
