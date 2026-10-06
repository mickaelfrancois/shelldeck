using Pupitre.Terminal;

namespace Pupitre.Tests;

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
}
