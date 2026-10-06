using System.Windows.Input;

namespace Pupitre;

public enum ShortcutAction
{
    NewTab,
    CloseTab,
    NextTab,
    PreviousTab,
    SelectTab,
}

/// <summary>
/// An application shortcut, always with Ctrl. <paramref name="KeyCode"/> is the physical key as the web page names it
/// (<c>KeyboardEvent.code</c>), so shortcuts work the same in the terminal and in the rest of the window.
/// </summary>
public sealed record Shortcut(ShortcutAction Action, Key Key, string KeyCode, bool Shift, int TabNumber = 0);

public static class Shortcuts
{
    private const int TabNumberCount = 9;

    /// <summary>
    /// Ctrl+Shift for new/close, as in Windows Terminal: Ctrl+W alone deletes the previous word in bash, zsh and Claude Code.
    /// </summary>
    public static IReadOnlyList<Shortcut> All { get; } =
    [
        new(ShortcutAction.NewTab, Key.T, "KeyT", Shift: true),
        new(ShortcutAction.CloseTab, Key.W, "KeyW", Shift: true),
        new(ShortcutAction.NextTab, Key.Tab, "Tab", Shift: false),
        new(ShortcutAction.PreviousTab, Key.Tab, "Tab", Shift: true),
        .. Enumerable.Range(1, TabNumberCount).Select(number =>
            new Shortcut(ShortcutAction.SelectTab, Key.D0 + number, $"Digit{number}", Shift: false, number)),
    ];

    public static Shortcut? Find(Key key, ModifierKeys modifiers) =>
        modifiers.HasFlag(ModifierKeys.Control) && !modifiers.HasFlag(ModifierKeys.Alt)
            ? All.FirstOrDefault(shortcut => shortcut.Key == key && shortcut.Shift == modifiers.HasFlag(ModifierKeys.Shift))
            : null;
}
