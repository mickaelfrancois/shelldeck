using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ShellDeck;

public enum ShortcutAction
{
    NewTab,
    ClosePane,
    NextTab,
    PreviousTab,
    SelectTab,
    SplitRight,
    SplitDown,
    SplitAuto,
    FocusPaneLeft,
    FocusPaneUp,
    FocusPaneRight,
    FocusPaneDown,
    ResizePaneLeft,
    ResizePaneUp,
    ResizePaneRight,
    ResizePaneDown,
}

/// <summary>
/// An application shortcut. <paramref name="KeyCode"/> is the physical key as the web page names it
/// (<c>KeyboardEvent.code</c>), so shortcuts work the same in the terminal and in the rest of the window.
/// </summary>
/// <param name="Character">
/// Set when the shortcut means a character rather than a key: "-" sits on the 6 key in AZERTY, not where QWERTY has it.
/// <paramref name="Key"/> is then the key typing it in the current layout; the web page looks up its own <c>code</c>.
/// </param>
public sealed record Shortcut(ShortcutAction Action, Key Key, string KeyCode, ModifierKeys Modifiers, int TabNumber = 0, char? Character = null);

public static class Shortcuts
{
    private const int TabNumberCount = 9;
    private const ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;
    private const ModifierKeys AltShift = ModifierKeys.Alt | ModifierKeys.Shift;

    /// <summary>
    /// Ctrl+Shift for new/close, as in Windows Terminal: Ctrl+W alone deletes the previous word in bash, zsh and Claude Code.
    /// Panes use Windows Terminal's keys too, plus the numpad +/-.
    /// </summary>
    public static IReadOnlyList<Shortcut> All { get; } =
    [
        new(ShortcutAction.NewTab, Key.T, "KeyT", CtrlShift),
        new(ShortcutAction.ClosePane, Key.W, "KeyW", CtrlShift),
        new(ShortcutAction.NextTab, Key.Tab, "Tab", ModifierKeys.Control),
        new(ShortcutAction.PreviousTab, Key.Tab, "Tab", CtrlShift),
        .. Enumerable.Range(1, TabNumberCount).Select(number =>
            new Shortcut(ShortcutAction.SelectTab, Key.D0 + number, $"Digit{number}", ModifierKeys.Control, number)),
        ForCharacter(ShortcutAction.SplitRight, '=', Key.OemPlus, "Equal", AltShift),
        new(ShortcutAction.SplitRight, Key.Add, "NumpadAdd", AltShift),
        ForCharacter(ShortcutAction.SplitDown, '-', Key.OemMinus, "Minus", AltShift),
        new(ShortcutAction.SplitDown, Key.Subtract, "NumpadSubtract", AltShift),
        new(ShortcutAction.SplitAuto, Key.D, "KeyD", AltShift),
        new(ShortcutAction.FocusPaneLeft, Key.Left, "ArrowLeft", ModifierKeys.Alt),
        new(ShortcutAction.FocusPaneUp, Key.Up, "ArrowUp", ModifierKeys.Alt),
        new(ShortcutAction.FocusPaneRight, Key.Right, "ArrowRight", ModifierKeys.Alt),
        new(ShortcutAction.FocusPaneDown, Key.Down, "ArrowDown", ModifierKeys.Alt),
        new(ShortcutAction.ResizePaneLeft, Key.Left, "ArrowLeft", AltShift),
        new(ShortcutAction.ResizePaneUp, Key.Up, "ArrowUp", AltShift),
        new(ShortcutAction.ResizePaneRight, Key.Right, "ArrowRight", AltShift),
        new(ShortcutAction.ResizePaneDown, Key.Down, "ArrowDown", AltShift),
    ];

    /// <summary>Modifiers must match exactly: AltGr (Ctrl+Alt) types characters and never triggers a shortcut.</summary>
    public static Shortcut? Find(Key key, ModifierKeys modifiers)
    {
        var pressed = modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        return All.FirstOrDefault(shortcut => shortcut.Key == key && shortcut.Modifiers == pressed);
    }

    /// <summary>The key typing <paramref name="character"/> in the current keyboard layout; the US key if none does.</summary>
    private static Shortcut ForCharacter(ShortcutAction action, char character, Key usKey, string usKeyCode, ModifierKeys modifiers)
    {
        const short NoKey = -1;
        var result = VkKeyScanW(character);
        var key = result == NoKey ? usKey : KeyInterop.KeyFromVirtualKey(result & 0xFF);
        return new(action, key, usKeyCode, modifiers, Character: character);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanW(char character);
}
