namespace ShellDeck.ViewModels;

/// <summary>A color the user gives an open tab to tell it apart: a stripe on its left in the tab list.</summary>
/// <param name="Hex"><c>#RRGGBB</c>, or null for no stripe.</param>
public sealed record TabColor(string Name, string? Hex)
{
    public static TabColor None { get; } = new("Aucune couleur", null);

    /// <summary>Windows Terminal's tab colors, in its 4×4 grid order.</summary>
    public static IReadOnlyList<TabColor> Palette { get; } =
    [
        new("Cramoisi", "#DC143C"),
        new("Bleu acier", "#4682B4"),
        new("Vert marin", "#3CB371"),
        new("Orange foncé", "#FF8C00"),
        new("Violet rouge", "#C71585"),
        new("Bleu dodger", "#1E90FF"),
        new("Vert citron", "#32CD32"),
        new("Jaune", "#FFFF00"),
        new("Bleu violet", "#8A2BE2"),
        new("Bleu ardoise", "#6A5ACD"),
        new("Lime", "#00FF00"),
        new("Tan", "#D2B48C"),
        new("Magenta", "#FF00FF"),
        new("Cyan", "#00FFFF"),
        new("Bleu ciel", "#87CEEB"),
        new("Gris foncé", "#A9A9A9"),
    ];

    /// <summary>A color picked in the Windows color dialog.</summary>
    public static TabColor Custom(string hex) => new("Personnalisé", hex);
}
