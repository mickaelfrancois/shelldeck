namespace ShellDeck.ViewModels;

/// <summary>How a new session is laid out: the panes it opens with, all running the same profile.</summary>
public enum PaneTemplate
{
    /// <summary>One pane.</summary>
    Single,

    /// <summary>Two panes side by side.</summary>
    Columns,

    /// <summary>Two panes stacked.</summary>
    Rows,

    /// <summary>One pane on the left, two stacked on the right.</summary>
    MainAndSide,

    /// <summary>Four panes, two columns of two rows.</summary>
    Grid,
}

public static class PaneTemplates
{
    public static IReadOnlyList<PaneTemplate> All { get; } = Enum.GetValues<PaneTemplate>();

    public static string Label(this PaneTemplate template) => template switch
    {
        PaneTemplate.Single => "1 panneau",
        PaneTemplate.Columns => "2 panneaux côte à côte",
        PaneTemplate.Rows => "2 panneaux empilés",
        PaneTemplate.MainAndSide => "3 panneaux (1 grand + 2)",
        PaneTemplate.Grid => "4 panneaux (2×2)",
        _ => template.ToString(),
    };

    /// <summary>
    /// The splits that build the template from a single pane, each as (index in the tab's panes of the pane to split,
    /// direction). A new pane is appended to the panes, so later steps can split it.
    /// </summary>
    internal static IReadOnlyList<(int Target, SplitDirection Direction)> Steps(this PaneTemplate template) => template switch
    {
        PaneTemplate.Columns => [(0, SplitDirection.Right)],
        PaneTemplate.Rows => [(0, SplitDirection.Down)],
        PaneTemplate.MainAndSide => [(0, SplitDirection.Right), (1, SplitDirection.Down)],
        PaneTemplate.Grid => [(0, SplitDirection.Right), (0, SplitDirection.Down), (1, SplitDirection.Down)],
        _ => [],
    };
}
