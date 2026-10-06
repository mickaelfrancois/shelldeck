namespace Pupitre.Terminal;

/// <summary>Decides when a paste must be confirmed, as Windows Terminal does.</summary>
public static class PasteGuard
{
    /// <summary>Above this size a paste is probably a mistake (wrong clipboard content), whatever the shell.</summary>
    public const int LargePasteLength = 5 * 1024;

    /// <summary>
    /// Without bracketed paste mode (cmd, raw SSH targets), every line break typed into the shell runs a command,
    /// so pasting several lines executes them one by one.
    /// </summary>
    public static bool RequiresConfirmation(string text, bool bracketedPasteMode) =>
        text.Length > LargePasteLength
        || !bracketedPasteMode && text.AsSpan().IndexOfAny('\r', '\n') >= 0;
}
