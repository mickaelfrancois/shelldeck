using System.Text.Json.Serialization;
using System.Windows;

namespace Pupitre;

/// <summary>Size and position of the main window, restored at the next launch.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized)
{
    // Enough of the title bar to grab the window with the mouse.
    private const double MinimumVisibleWidth = 100;
    private const double TitleBarHeight = 30;

    /// <summary>False when the settings file holds nonsense, e.g. after a hand edit.</summary>
    [JsonIgnore]
    public bool IsValid =>
        double.IsFinite(Left) && double.IsFinite(Top)
        && double.IsFinite(Width) && double.IsFinite(Height)
        && Width > 0 && Height > 0;

    private Rect Bounds => new(Left, Top, Width, Height);

    private Rect TitleBar => new(Left, Top, Width, Math.Min(TitleBarHeight, Height));

    /// <summary>
    /// The placement itself when its title bar can be grabbed on one of the monitors; otherwise (monitor unplugged,
    /// layout changed) the same window moved, and shrunk if needed, into the work area of the nearest monitor.
    /// A maximized window then maximizes on that monitor.
    /// </summary>
    /// <param name="workAreas">The monitors' work areas (without the taskbar), in the window's coordinates.</param>
    public WindowPlacement KeepOnScreen(IReadOnlyList<Rect> workAreas)
    {
        if (workAreas.Count == 0 || workAreas.Any(IsTitleBarReachableOn))
        {
            return this;
        }

        var target = workAreas.MinBy(area => DistanceBetween(TitleBar, area));
        var width = Math.Min(Width, target.Width);
        var height = Math.Min(Height, target.Height);
        return this with
        {
            Left = Math.Clamp(Left, target.Left, target.Right - width),
            Top = Math.Clamp(Top, target.Top, target.Bottom - height),
            Width = width,
            Height = height,
        };
    }

    private bool IsTitleBarReachableOn(Rect workArea)
    {
        var visiblePart = Rect.Intersect(TitleBar, workArea);
        return !visiblePart.IsEmpty
            && visiblePart.Width >= Math.Min(MinimumVisibleWidth, Width)
            && visiblePart.Height >= TitleBar.Height;
    }

    private static double DistanceBetween(Rect a, Rect b)
    {
        var dx = Math.Max(0, Math.Max(b.Left - a.Right, a.Left - b.Right));
        var dy = Math.Max(0, Math.Max(b.Top - a.Bottom, a.Top - b.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
