namespace Pupitre.Terminal;

/// <summary>
/// Reads Claude Code's state from the terminal title it sets: <c>"✳ topic"</c> when idle,
/// a rotating half-circle (<c>"◐ topic"</c>) while working. Observed behavior, not documented by Claude Code.
/// </summary>
public static class ClaudeCodeTitle
{
    private const char IdleMarker = '✳';
    private const string WorkingMarkers = "◐◓◑◒";

    public static bool IsClaudeCode(string title) =>
        HasMarker(title) && (title[0] == IdleMarker || WorkingMarkers.Contains(title[0]));

    public static bool IsWorking(string title) =>
        HasMarker(title) && WorkingMarkers.Contains(title[0]);

    private static bool HasMarker(string title) => title.Length >= 2 && title[1] == ' ';
}
