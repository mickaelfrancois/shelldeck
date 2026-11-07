namespace ShellDeck.Tests;

public sealed class ShortcutHelpTests
{
    [Fact]
    public void Every_application_shortcut_is_described()
    {
        var described = ShortcutHelp.Sections.SelectMany(section => section.Entries).SelectMany(entry => entry.Actions).ToHashSet();

        Assert.All(Enum.GetValues<ShortcutAction>(), action => Assert.Contains(action, described));
    }

    [Fact]
    public void A_shortcut_with_several_keys_lists_them_all()
    {
        Assert.Equal("Alt+Maj+- / Alt+Maj+Pavé -", ShortcutHelp.KeysOf(ShortcutAction.SplitDown));
    }

    [Fact]
    public void Modifiers_use_the_french_keyboard_names()
    {
        Assert.Equal("Ctrl+Maj+T", ShortcutHelp.KeysOf(ShortcutAction.NewTab));
    }

    [Fact]
    public void The_four_pane_moves_share_one_row()
    {
        var row = ShortcutHelp.Sections.SelectMany(section => section.Entries).Single(entry => entry.Actions.Contains(ShortcutAction.FocusPaneUp));

        Assert.Equal("Alt+←↑→↓", row.Keys);
    }

    [Fact]
    public void Search_uses_ctrl_shift_f()
    {
        Assert.Equal("Ctrl+Maj+F", ShortcutHelp.KeysOf(ShortcutAction.OpenSearch));
    }

    [Fact]
    public void Shortcuts_are_unique()
    {
        var duplicates = Shortcuts.All.GroupBy(shortcut => (shortcut.Key, shortcut.Modifiers)).Where(group => group.Count() > 1);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Prompt_navigation_is_described()
    {
        var entries = ShortcutHelp.Sections.SelectMany(section => section.Entries);

        Assert.Contains(entries, entry => entry.Keys.Contains("Ctrl+↑"));
    }

    [Fact]
    public void Output_selection_is_described()
    {
        var entries = ShortcutHelp.Sections.SelectMany(section => section.Entries);

        Assert.Contains(entries, entry => entry.Keys.Contains("Ctrl+Maj+O"));
    }
}
