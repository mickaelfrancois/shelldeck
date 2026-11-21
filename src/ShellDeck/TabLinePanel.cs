using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ShellDeck.TabLines;

namespace ShellDeck;

/// <summary>
/// Lays the segments of one tab line on a single row. When the row is too long, the segments with the highest
/// <see cref="TabSegment.Shrink"/> give way first, each down to an ellipsis before the next one starts; a segment
/// with 0 is always measured in full.
/// </summary>
public sealed class TabLinePanel : Panel
{
    /// <summary>
    /// The width each segment gets. A segment gives up its width from <paramref name="shrink"/> highest to lowest
    /// (0 never), left to right among equals, and stops at its <paramref name="floor"/>. Below the sum of the floors
    /// and the fixed segments the row is clipped.
    /// </summary>
    public static double[] Allocate(double available, double[] desired, int[] shrink, double[] floor)
    {
        var widths = (double[])desired.Clone();
        var excess = desired.Sum() - available;
        foreach (var index in Enumerable.Range(0, desired.Length).Where(i => shrink[i] > 0).OrderByDescending(i => shrink[i]).ThenBy(i => i))
        {
            if (excess <= 0)
            {
                break;
            }

            var give = Math.Min(excess, Math.Max(0, widths[index] - Math.Min(floor[index], desired[index])));
            widths[index] -= give;
            excess -= give;
        }

        return widths;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        var children = InternalChildren.Cast<UIElement>().ToArray();
        foreach (var child in children)
        {
            child.Measure(infinite);
        }

        var desired = children.Select(child => child.DesiredSize.Width).ToArray();
        var height = children.Select(child => child.DesiredSize.Height).DefaultIfEmpty(0).Max();
        if (double.IsInfinity(availableSize.Width))
        {
            return new Size(desired.Sum(), height);
        }

        var ellipsis = EllipsisWidth();
        var widths = Allocate(
            availableSize.Width, desired, [.. children.Select(ShrinkOf)], [.. children.Select(_ => ellipsis)]);
        for (var i = 0; i < children.Length; i++)
        {
            if (widths[i] < desired[i])
            {
                children[i].Measure(new Size(widths[i], availableSize.Height));
            }
        }

        return new Size(Math.Min(children.Sum(child => child.DesiredSize.Width), availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    // The panel's children are the containers of an ItemsControl: the segment is their content.
    private static int ShrinkOf(UIElement child) =>
        child is ContentPresenter { Content: TabSegment segment } ? segment.Shrink : 0;

    private double EllipsisWidth()
    {
        var typeface = new Typeface(
            TextElement.GetFontFamily(this), TextElement.GetFontStyle(this), TextElement.GetFontWeight(this), TextElement.GetFontStretch(this));
        return new FormattedText(
            "…", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, TextElement.GetFontSize(this), Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
    }
}
