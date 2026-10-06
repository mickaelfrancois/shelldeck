using System.Windows;

namespace Pupitre;

/// <summary>Size and position of the main window, restored at the next launch.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized)
{
    // Enough of the title bar to grab the window with the mouse.
    private const double MinimumVisibleWidth = 100;
    private const double MinimumVisibleHeight = 30;

    /// <summary>False when the window would open off-screen, e.g. after unplugging the monitor it was on.</summary>
    public bool IsVisibleOn(Rect virtualScreen)
    {
        var visiblePart = Rect.Intersect(new Rect(Left, Top, Width, Height), virtualScreen);
        return !visiblePart.IsEmpty
            && visiblePart.Width >= MinimumVisibleWidth
            && visiblePart.Height >= MinimumVisibleHeight;
    }
}
