using System.Windows.Input;

namespace Pupitre;

/// <param name="Actions">The application shortcuts the row describes; empty for keys handled by the terminal itself.</param>
public sealed record ShortcutHelpEntry(string Keys, string Description, IReadOnlyList<ShortcutAction> Actions);

public sealed record ShortcutHelpSection(string Title, IReadOnlyList<ShortcutHelpEntry> Entries);

/// <summary>
/// The keyboard reference shown from the side panel. Keys come from <see cref="Shortcuts.All"/>,
/// so the reference cannot drift from what the application actually does.
/// </summary>
public static class ShortcutHelp
{
    private const int LastNumberedTab = 8;
    private const string Separator = " / ";

    public static IReadOnlyList<ShortcutHelpSection> Sections { get; } =
    [
        new("Onglets",
        [
            Row("Nouvel onglet avec le shell par défaut (★)", ShortcutAction.NewTab),
            Row("Onglet suivant", ShortcutAction.NextTab),
            Row("Onglet précédent", ShortcutAction.PreviousTab),
            new($"{TabKeys(1)} … {TabKeys(LastNumberedTab)}", $"Aller à l'onglet 1 à {LastNumberedTab}", [ShortcutAction.SelectTab]),
            new(TabKeys(LastNumberedTab + 1), "Aller au dernier onglet", [ShortcutAction.SelectTab]),
        ]),
        new("Volets",
        [
            Row("Scinder : nouveau volet à droite", ShortcutAction.SplitRight),
            Row("Scinder : nouveau volet en dessous", ShortcutAction.SplitDown),
            Row("Scinder dans le sens le plus long", ShortcutAction.SplitAuto),
            DirectionRow("Passer au volet voisin",
                ShortcutAction.FocusPaneLeft, ShortcutAction.FocusPaneUp, ShortcutAction.FocusPaneRight, ShortcutAction.FocusPaneDown),
            DirectionRow("Déplacer le séparateur du volet",
                ShortcutAction.ResizePaneLeft, ShortcutAction.ResizePaneUp, ShortcutAction.ResizePaneRight, ShortcutAction.ResizePaneDown),
            Row("Fermer le volet (l'onglet s'il est seul)", ShortcutAction.ClosePane),
        ]),
        new("Terminal",
        [
            new("Ctrl+C", "Copier la sélection ; sans sélection, interrompre", []),
            new("Ctrl+Maj+C", "Copier la sélection", []),
            new($"Ctrl+V{Separator}Maj+Inser{Separator}clic droit", "Coller", []),
            new("Sélection à la souris", "Copie automatiquement", []),
            new("Ctrl+clic", "Ouvrir un lien", []),
        ]),
        new("Lanceur",
        [
            new("Entrée", "Ouvrir le premier shell filtré (sinon le shell par défaut)", []),
            new("Échap", "Effacer le filtre", []),
        ]),
    ];

    /// <summary>Every way to trigger <paramref name="action"/>, e.g. "Alt+Maj+- / Alt+Maj+Pavé -".</summary>
    public static string KeysOf(ShortcutAction action) =>
        string.Join(Separator, Shortcuts.All.Where(shortcut => shortcut.Action == action).Select(Format));

    public static string Format(Shortcut shortcut) =>
        ModifierPrefix(shortcut.Modifiers) + (shortcut.Character?.ToString() ?? KeyName(shortcut.Key));

    private static ShortcutHelpEntry Row(string description, ShortcutAction action) =>
        new(KeysOf(action), description, [action]);

    /// <summary>One row for four arrow shortcuts sharing their modifiers: "Alt+←↑→↓".</summary>
    private static ShortcutHelpEntry DirectionRow(string description, params ShortcutAction[] actions)
    {
        var shortcuts = actions.Select(action => Shortcuts.All.First(shortcut => shortcut.Action == action)).ToList();
        var keys = ModifierPrefix(shortcuts[0].Modifiers) + string.Concat(shortcuts.Select(shortcut => KeyName(shortcut.Key)));
        return new(keys, description, actions);
    }

    private static string TabKeys(int number) =>
        Format(Shortcuts.All.First(shortcut => shortcut.Action == ShortcutAction.SelectTab && shortcut.TabNumber == number));

    private static string ModifierPrefix(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : string.Empty)
        + (modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : string.Empty)
        + (modifiers.HasFlag(ModifierKeys.Shift) ? "Maj+" : string.Empty);

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.Add => "Pavé +",
        Key.Subtract => "Pavé -",
        Key.Left => "←",
        Key.Up => "↑",
        Key.Right => "→",
        Key.Down => "↓",
        _ => key.ToString(),
    };
}
