using System.IO;
using ShellDeck.Pty;

namespace ShellDeck.Tests;

public sealed class PseudoConsoleApiTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string _directory = Directory.CreateTempSubdirectory("shelldeck-conpty-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Select_with_the_shipped_binaries_picks_the_embedded_conpty()
    {
        var api = PseudoConsoleApi.Select(AppContext.BaseDirectory);

        Assert.True(api.IsEmbedded);
        Assert.True(api.BuildNumber >= 21376);
    }

    [Fact]
    public void Select_with_an_empty_directory_falls_back_to_the_inbox_conpty()
    {
        var api = PseudoConsoleApi.Select(_directory);

        Assert.False(api.IsEmbedded);
        Assert.Equal(Environment.OSVersion.Version.Build, api.BuildNumber);
    }

    [Fact]
    public void Select_with_a_corrupt_conpty_dll_falls_back_to_the_inbox_conpty()
    {
        File.WriteAllBytes(Path.Combine(_directory, "conpty.dll"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(_directory, "OpenConsole.exe"), [1, 2, 3, 4]);

        Assert.False(PseudoConsoleApi.Select(_directory).IsEmbedded);
    }

    [Fact]
    public void Select_without_OpenConsole_falls_back_to_the_inbox_conpty()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "conpty.dll"), Path.Combine(_directory, "conpty.dll"));

        Assert.False(PseudoConsoleApi.Select(_directory).IsEmbedded);
    }

    [Fact]
    public async Task A_session_on_the_embedded_conpty_reports_the_exit_code_and_disposes_promptly()
    {
        await AssertExitCodeAndPromptDispose(PseudoConsoleApi.Select(AppContext.BaseDirectory));
    }

    [Fact]
    public async Task A_session_on_the_inbox_conpty_reports_the_exit_code_and_disposes_promptly()
    {
        await AssertExitCodeAndPromptDispose(PseudoConsoleApi.Inbox);
    }

    private static async Task AssertExitCodeAndPromptDispose(PseudoConsoleApi api)
    {
        var session = PtySession.Start("cmd.exe /c exit 3", null, new TerminalSize(80, 24), api);

        var exitCode = await session.WaitForExitAsync().WaitAsync(Timeout);

        var disposal = Task.Run(session.Dispose);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, exitCode);
    }
}
