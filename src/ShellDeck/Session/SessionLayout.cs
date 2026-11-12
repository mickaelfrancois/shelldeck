using System.Text.Json.Serialization;
using ShellDeck.ViewModels;

namespace ShellDeck.Session;

/// <summary>
/// What is written in session.json: the tabs and panes to reopen. Plain data, never the view models themselves.
/// <see cref="MainPane"/>/<see cref="ActivePane"/> indices count the leaves of a tab left to right, depth first.
/// </summary>
public sealed record SessionLayout(int Version, IReadOnlyList<TabLayout> Tabs, int SelectedTab)
{
    public const int CurrentVersion = 1;
}

public sealed record TabLayout(PaneNodeLayout Root, string? ColorName, string? ColorHex, int MainPane, int ActivePane);

/// <summary>Exactly one of <see cref="Pane"/> and <see cref="Split"/>: no type discriminator, so the file stays readable by hand.</summary>
public sealed record PaneNodeLayout(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PaneLayout? Pane = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SplitLayout? Split = null)
{
    public bool IsWellFormed => (Pane is null) != (Split is null)
        && (Split is null || (Split.First is not null && Split.Second is not null && Split.First.IsWellFormed && Split.Second.IsWellFormed))
        && (Pane is null || Pane.ProfileId is not null);
}

public sealed record PaneLayout(string ProfileId, string? WorkingDirectory, string? ClaudeSessionId, bool ClaudeRunning);

public sealed record SplitLayout(SplitDirection Direction, double Ratio, PaneNodeLayout First, PaneNodeLayout Second);
