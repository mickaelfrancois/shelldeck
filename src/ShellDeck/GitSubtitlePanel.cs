using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShellDeck;

/// <summary>The part of the git subtitle a child shows.</summary>
public enum GitSubtitleRole
{
    Repo,
    Separator,
    Branch,
    Marker,
}

/// <summary>
/// Lays the segments of the git subtitle (repo, separator, branch, markers) on one line. When the line is too long the
/// branch shrinks first, then the repo; the separator and the markers are always measured in full.
/// </summary>
public sealed class GitSubtitlePanel : Panel
{
    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(GitSubtitleRole), typeof(GitSubtitlePanel),
        new FrameworkPropertyMetadata(
            GitSubtitleRole.Marker,
            FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static GitSubtitleRole GetRole(UIElement element) => (GitSubtitleRole)element.GetValue(RoleProperty);

    public static void SetRole(UIElement element, GitSubtitleRole value) => element.SetValue(RoleProperty, value);

    /// <summary>
    /// Splits the width left after the fixed parts between the repo and the branch. The branch shrinks first, down to
    /// <paramref name="branchFloor"/> (the width of an ellipsis); then the repo shrinks. Below that the line is clipped.
    /// </summary>
    public static (double Repo, double Branch) Allocate(
        double available, double fixedWidth, double repoDesired, double branchDesired, double branchFloor)
    {
        var remaining = Math.Max(0, available - fixedWidth);
        if (repoDesired + branchDesired <= remaining)
        {
            return (repoDesired, branchDesired);
        }

        var floor = Math.Min(branchFloor, branchDesired);
        var branch = Math.Min(branchDesired, Math.Max(floor, remaining - repoDesired));
        var repo = Math.Min(repoDesired, Math.Max(0, remaining - branch));
        return (repo, branch);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        double height = 0, fixedWidth = 0, repoDesired = 0, branchDesired = 0;
        UIElement? repo = null, branch = null;

        foreach (UIElement child in InternalChildren)
        {
            child.Measure(infinite);
            height = Math.Max(height, child.DesiredSize.Height);
            switch (GetRole(child))
            {
                case GitSubtitleRole.Repo:
                    repo = child;
                    repoDesired = child.DesiredSize.Width;
                    break;
                case GitSubtitleRole.Branch:
                    branch = child;
                    branchDesired = child.DesiredSize.Width;
                    break;
                default:
                    fixedWidth += child.DesiredSize.Width;
                    break;
            }
        }

        if (double.IsInfinity(availableSize.Width))
        {
            return new Size(fixedWidth + repoDesired + branchDesired, height);
        }

        var floor = branch is TextBlock text ? EllipsisWidth(text) : 0;
        var (repoWidth, branchWidth) = Allocate(availableSize.Width, fixedWidth, repoDesired, branchDesired, floor);
        repo?.Measure(new Size(repoWidth, availableSize.Height));
        branch?.Measure(new Size(branchWidth, availableSize.Height));

        double used = fixedWidth + (repo?.DesiredSize.Width ?? 0) + (branch?.DesiredSize.Width ?? 0);
        return new Size(Math.Min(used, availableSize.Width), height);
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

    private static double EllipsisWidth(TextBlock text)
    {
        var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
        var dpi = VisualTreeHelper.GetDpi(text).PixelsPerDip;
        return new FormattedText(
            "…", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, text.FontSize, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
    }
}
