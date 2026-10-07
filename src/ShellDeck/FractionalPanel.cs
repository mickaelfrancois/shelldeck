using System.Windows;
using System.Windows.Controls;

namespace ShellDeck;

/// <summary>
/// Places each child at <see cref="BoundsProperty"/>, given in fractions of the panel's size.
/// A zero width (or height) is a line: the child keeps its own thickness, centered on it (e.g. a splitter).
/// </summary>
/// <remarks>
/// Panes move by changing their bounds, never their parent: a <see cref="Terminal.TerminalView"/> removed from
/// the visual tree is unloaded, which ends its shell.
/// </remarks>
public sealed class FractionalPanel : Panel
{
    public static readonly DependencyProperty BoundsProperty = DependencyProperty.RegisterAttached(
        "Bounds", typeof(Rect), typeof(FractionalPanel),
        new FrameworkPropertyMetadata(
            new Rect(0, 0, 1, 1),
            FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static Rect GetBounds(UIElement element) => (Rect)element.GetValue(BoundsProperty);

    public static void SetBounds(UIElement element, Rect value) => element.SetValue(BoundsProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

        foreach (UIElement child in InternalChildren)
        {
            var bounds = GetBounds(child);
            child.Measure(new Size(
                bounds.Width == 0 ? double.PositiveInfinity : bounds.Width * size.Width,
                bounds.Height == 0 ? double.PositiveInfinity : bounds.Height * size.Height));
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            var bounds = GetBounds(child);
            var (x, width) = Scale(bounds.X, bounds.Width, finalSize.Width, child.DesiredSize.Width);
            var (y, height) = Scale(bounds.Y, bounds.Height, finalSize.Height, child.DesiredSize.Height);
            child.Arrange(new Rect(x, y, width, height));
        }

        return finalSize;
    }

    private static (double Start, double Length) Scale(double start, double length, double panelLength, double desiredLength) =>
        length == 0
            ? (start * panelLength - desiredLength / 2, desiredLength)
            : (start * panelLength, length * panelLength);
}
