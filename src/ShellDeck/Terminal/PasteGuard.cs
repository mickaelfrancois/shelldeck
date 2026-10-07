namespace ShellDeck.Terminal;

/// <summary>Decides when a paste must be confirmed, as Windows Terminal does.</summary>
public static class PasteGuard
{
    /// <summary>Above this size a paste is probably a mistake (wrong clipboard content), whatever the shell.</summary>
    public const int LargePasteLength = 5 * 1024;

    /// <summary>
    /// Without bracketed paste mode (cmd, raw SSH targets), every line break typed into the shell runs a command,
    /// so pasting several lines executes them one by one.
    /// </summary>
    /// <summary>
    /// Removes control characters except tab and line breaks. Clipboard text can come from a web page: an embedded
    /// ESC sequence such as the bracketed paste end marker (ESC[201~) would end the protected block early and run
    /// the rest as commands (pastejacking). Done before <see cref="RequiresConfirmation"/>, which judges the result.
    /// </summary>
    public static string Sanitize(string text) =>
        string.Concat(text.Where(character => !char.IsControl(character) || character is '\t' or '\r' or '\n'));

    public static bool RequiresConfirmation(string text, bool bracketedPasteMode) =>
        text.Length > LargePasteLength
        || !bracketedPasteMode && text.AsSpan().IndexOfAny('\r', '\n') >= 0;
}
