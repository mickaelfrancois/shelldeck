using System.Windows;

namespace ShellDeck.ViewModels;

/// <summary>Where a split puts the new pane, relative to the pane being split.</summary>
public enum SplitDirection
{
    Right,
    Down,
}

public enum PaneDirection
{
    Left,
    Up,
    Right,
    Down,
}

/// <summary>A node of a tab's layout: a pane, or a split holding two nodes.</summary>
public interface IPaneNode
{
    /// <summary>Lays the node out in <paramref name="area"/>, given in fractions of the terminal area.</summary>
    void Arrange(Rect area);
}

/// <summary>Two nodes side by side (<see cref="SplitDirection.Right"/>) or stacked (<see cref="SplitDirection.Down"/>).</summary>
public sealed class PaneSplit(SplitDirection direction, IPaneNode first, IPaneNode second) : ObservableObject, IPaneNode
{
    private const double MinRatio = 0.1;
    private const double MaxRatio = 1 - MinRatio;

    private double _ratio = 0.5;
    private Rect _area;
    private Rect _bounds;

    public SplitDirection Direction { get; } = direction;

    public IPaneNode First { get; internal set; } = first;

    public IPaneNode Second { get; internal set; } = second;

    /// <summary>Share of the split given to <see cref="First"/>; neither side can shrink to nothing.</summary>
    public double Ratio
    {
        get => _ratio;
        set => SetField(ref _ratio, Math.Clamp(value, MinRatio, MaxRatio));
    }

    /// <summary>The divider: a zero-width (or zero-height) line, in fractions of the terminal area.</summary>
    public Rect Bounds
    {
        get => _bounds;
        private set => SetField(ref _bounds, value);
    }

    /// <summary>Moves the divider by <paramref name="delta"/>, in fractions of the terminal area.</summary>
    public void MoveDivider(Vector delta) =>
        Ratio += Direction is SplitDirection.Right ? delta.X / _area.Width : delta.Y / _area.Height;

    public void Arrange(Rect area)
    {
        _area = area;
        if (Direction is SplitDirection.Right)
        {
            var firstWidth = area.Width * Ratio;
            Bounds = new Rect(area.X + firstWidth, area.Y, 0, area.Height);
            First.Arrange(new Rect(area.X, area.Y, firstWidth, area.Height));
            Second.Arrange(new Rect(area.X + firstWidth, area.Y, area.Width - firstWidth, area.Height));
        }
        else
        {
            var firstHeight = area.Height * Ratio;
            Bounds = new Rect(area.X, area.Y + firstHeight, area.Width, 0);
            First.Arrange(new Rect(area.X, area.Y, area.Width, firstHeight));
            Second.Arrange(new Rect(area.X, area.Y + firstHeight, area.Width, area.Height - firstHeight));
        }
    }
}
