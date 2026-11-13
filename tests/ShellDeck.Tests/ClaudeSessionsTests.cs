using System.IO;
using ShellDeck.Session;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class ClaudeSessionsTests : IDisposable
{
    private const string FirstId = "3f2c8a4e-1b5d-4c6e-9f70-8a1b2c3d4e5f";
    private const string SecondId = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d";
    private const string IdleTitle = "✳ Fix the bug";
    private const string WorkingTitle = "◐ Fix the bug";

    private static readonly IReadOnlySet<string> NoneClaimed = new HashSet<string>();
    private static readonly ShellProfile PowerShell = new("PowerShell 7", "pwsh.exe", ShellKind.PowerShell, @"C:\Users\me");
    private static readonly ShellProfile Ubuntu = new("Ubuntu-26.04", "wsl.exe -d Ubuntu-26.04", ShellKind.Wsl, "~");
    private static readonly DateTime Detected = new(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "shelldeck-claude-" + Guid.NewGuid().ToString("N"));

    public ClaudeSessionsTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Transcript(string folder, string id, DateTime written, string? content = null)
    {
        var directory = Path.Combine(_root, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id + ".jsonl");
        File.WriteAllText(path, content ?? $"{{\"type\":\"summary\"}}\n{{\"sessionId\":\"{id}\",\"cwd\":\"C:\\\\Users\\\\me\\\\proj\"}}\n");
        File.SetLastWriteTimeUtc(path, written);
        Directory.SetLastWriteTimeUtc(directory, written);
        return path;
    }

    [Fact]
    public void For_a_valid_guid_resumes_that_conversation() =>
        Assert.Equal($"claude --resume {FirstId}\r", ResumeCommand.For(FirstId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x; rm -rf ~")]
    [InlineData("abc\r\nevil")]
    [InlineData("3f2c8a4e1b5d4c6e9f708a1b2c3d4e5f")]
    [InlineData("{3f2c8a4e-1b5d-4c6e-9f70-8a1b2c3d4e5f}")]
    [InlineData("3f2c8a4e-1b5d-4c6e-9f70-8a1b2c3d4e5f; calc")]
    public void For_anything_but_a_strict_guid_continues_the_last_conversation(string? id) =>
        Assert.Equal("claude --continue\r", ResumeCommand.For(id));

    [Fact]
    public void The_project_folder_name_replaces_every_non_alphanumeric_character() =>
        Assert.Equal("C--Users-me-proj", TranscriptSessionLocator.EncodeDirectory(@"C:\Users\me\proj"));

    [Fact]
    public void The_transcript_written_after_the_detection_wins()
    {
        var locator = new TranscriptSessionLocator(_root);
        var folder = TranscriptSessionLocator.EncodeDirectory(@"C:\Users\me\proj");
        Transcript(folder, SecondId, Detected.AddMinutes(-30));
        Transcript(folder, FirstId, Detected.AddMinutes(5));

        var info = locator.Locate(@"C:\Users\me\proj", Detected, NoneClaimed);

        Assert.Equal(new ClaudeSessionInfo(FirstId, @"C:\Users\me\proj"), info);
    }

    [Fact]
    public void A_conversation_claimed_by_another_pane_is_left_out()
    {
        var locator = new TranscriptSessionLocator(_root);
        var folder = TranscriptSessionLocator.EncodeDirectory(@"C:\Users\me\proj");
        Transcript(folder, FirstId, Detected.AddMinutes(9));
        Transcript(folder, SecondId, Detected.AddMinutes(5));

        var info = locator.Locate(@"C:\Users\me\proj", Detected, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FirstId });

        Assert.Equal(SecondId, info?.SessionId);
    }

    [Fact]
    public void Without_a_known_directory_the_recent_projects_are_searched_and_the_cwd_is_read_from_the_transcript()
    {
        var locator = new TranscriptSessionLocator(_root);
        Transcript("C--old-project", SecondId, Detected.AddDays(-3));
        Transcript("C--Users-me-proj", FirstId, Detected.AddMinutes(2));

        var info = locator.Locate(null, Detected, NoneClaimed);

        Assert.Equal(new ClaudeSessionInfo(FirstId, @"C:\Users\me\proj"), info);
    }

    [Fact]
    public void A_missing_projects_folder_gives_nothing()
    {
        var locator = new TranscriptSessionLocator(Path.Combine(_root, "absent"));

        Assert.Null(locator.Locate(@"C:\x", Detected, NoneClaimed));
    }

    [Fact]
    public void A_missing_project_folder_for_a_known_directory_gives_nothing()
    {
        var locator = new TranscriptSessionLocator(_root);

        Assert.Null(locator.Locate(@"C:\x", Detected, NoneClaimed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all\n")]
    [InlineData("{\"type\":\"summary\"}\n")]
    [InlineData("{\"sessionId\":42}\n")]
    public void A_transcript_without_a_readable_session_id_gives_nothing(string content)
    {
        var locator = new TranscriptSessionLocator(_root);
        var folder = TranscriptSessionLocator.EncodeDirectory(@"C:\x");
        Transcript(folder, FirstId, Detected.AddMinutes(1), content);

        Assert.Null(locator.Locate(@"C:\x", Detected, NoneClaimed));
    }

    [Fact]
    public void A_transcript_still_being_written_can_be_read()
    {
        var locator = new TranscriptSessionLocator(_root);
        var folder = TranscriptSessionLocator.EncodeDirectory(@"C:\x");
        var path = Transcript(folder, FirstId, Detected.AddMinutes(1));

        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(FirstId, locator.Locate(@"C:\x", Detected, NoneClaimed)?.SessionId);
    }

    [Fact]
    public void Claude_detected_in_a_side_pane_counts_for_that_pane()
    {
        var tab = new TerminalTab(PowerShell);
        tab.Split(SplitDirection.Right);
        var side = tab.ActivePane;

        side.ApplyTerminalTitle(IdleTitle);

        Assert.Equal((true, false), (side.IsClaudeRunning, tab.MainPane.IsClaudeRunning));
    }

    [Fact]
    public async Task A_finished_turn_stores_the_conversation_and_directory_of_the_pane()
    {
        var viewModel = new MainViewModel([PowerShell], [], new AppSettings(), _ => { }, new FakeLocator(new ClaudeSessionInfo(FirstId, @"C:\Users\me\proj")));
        viewModel.Open(PowerShell);
        var pane = viewModel.SelectedTab!.MainPane;
        pane.ApplyTerminalTitle(IdleTitle);

        await viewModel.ResolveClaudeSessionAsync(pane);

        Assert.Equal((FirstId, @"C:\Users\me\proj"), (pane.ClaudeSessionId, viewModel.SelectedTab!.ToLayout().Root.Pane!.WorkingDirectory));
    }

    [Fact]
    public async Task A_failed_resolution_keeps_the_previous_conversation()
    {
        var viewModel = new MainViewModel([PowerShell], [], new AppSettings(), _ => { }, new FakeLocator(null));
        viewModel.Open(PowerShell);
        var pane = viewModel.SelectedTab!.MainPane;
        pane.ApplyTerminalTitle(IdleTitle);
        pane.ClaudeSessionId = FirstId;

        await viewModel.ResolveClaudeSessionAsync(pane);

        Assert.Equal(FirstId, pane.ClaudeSessionId);
    }

    [Fact]
    public async Task A_conversation_claimed_by_another_pane_is_passed_to_the_locator_as_taken()
    {
        var locator = new FakeLocator(null);
        var viewModel = new MainViewModel([PowerShell], [], new AppSettings(), _ => { }, locator);
        viewModel.Open(PowerShell);
        var first = viewModel.SelectedTab!.MainPane;
        first.ClaudeSessionId = FirstId;
        viewModel.Open(PowerShell);
        var second = viewModel.SelectedTab!.MainPane;
        second.ApplyTerminalTitle(IdleTitle);

        await viewModel.ResolveClaudeSessionAsync(second);

        Assert.Contains(FirstId, locator.LastClaimed!);
    }

    [Fact]
    public async Task A_wsl_pane_is_not_resolved_from_the_windows_transcripts()
    {
        var locator = new FakeLocator(new ClaudeSessionInfo(FirstId, null));
        var viewModel = new MainViewModel([PowerShell, Ubuntu], [], new AppSettings(), _ => { }, locator);
        viewModel.Open(Ubuntu);
        var pane = viewModel.SelectedTab!.MainPane;
        pane.ApplyTerminalTitle(IdleTitle);

        await viewModel.ResolveClaudeSessionAsync(pane);

        Assert.Equal((0, null), (locator.Calls, pane.ClaudeSessionId));
    }

    [Fact]
    public void A_turn_ending_in_a_pane_raises_ClaudeTurnEnded_once()
    {
        var tab = new TerminalTab(PowerShell);
        var raised = new List<TerminalPane>();
        tab.ClaudeTurnEnded += (_, pane) => raised.Add(pane);

        tab.MainPane.ApplyTerminalTitle(IdleTitle);
        tab.MainPane.ApplyTerminalTitle(WorkingTitle);
        tab.MainPane.ApplyTerminalTitle(IdleTitle);

        Assert.Equal([tab.MainPane], raised);
    }

    [Fact]
    public void A_restored_claude_pane_types_the_resume_command_with_its_id_once()
    {
        var pane = RestoredPane(PowerShell, new PaneLayout(PowerShell.Id, @"C:\Users\me\proj", FirstId, ClaudeRunning: true));

        Assert.Equal(($"claude --resume {FirstId}\r", null), (pane.TakeStartupInput(), pane.TakeStartupInput()));
    }

    [Fact]
    public void A_restored_claude_pane_with_a_tampered_id_continues_instead()
    {
        var pane = RestoredPane(PowerShell, new PaneLayout(PowerShell.Id, @"C:\Users\me\proj", "x; rm -rf ~", ClaudeRunning: true));

        Assert.Equal("claude --continue\r", pane.StartupInput);
    }

    [Fact]
    public void A_restored_wsl_claude_pane_continues_without_an_id()
    {
        var pane = RestoredPane(Ubuntu, new PaneLayout(Ubuntu.Id, "/home/me/proj", FirstId, ClaudeRunning: true));

        Assert.Equal(("claude --continue\r", "/home/me/proj"), (pane.StartupInput, pane.RestoredDirectory));
    }

    [Fact]
    public void A_restored_pane_without_claude_types_nothing()
    {
        var pane = RestoredPane(PowerShell, new PaneLayout(PowerShell.Id, @"C:\Users\me\proj", FirstId, ClaudeRunning: false));

        Assert.Null(pane.StartupInput);
    }

    [Fact]
    public void Claude_leaving_a_pane_forgets_its_conversation()
    {
        var tab = new TerminalTab(PowerShell);
        tab.MainPane.ApplyTerminalTitle(IdleTitle);
        tab.MainPane.ClaudeSessionId = FirstId;

        tab.MainPane.ApplyTerminalTitle("PowerShell");

        Assert.Equal((null, false), (tab.MainPane.ClaudeSessionId, tab.ToLayout().Root.Pane!.ClaudeRunning));
    }

    [Fact]
    public void A_restored_claude_pane_stays_a_claude_pane_in_a_save_before_its_title_arrives()
    {
        var tab = RestoredTab(PowerShell, new PaneLayout(PowerShell.Id, @"C:\Users\me\proj", FirstId, ClaudeRunning: true));

        var saved = tab.ToLayout().Root.Pane!;

        Assert.Equal((true, FirstId, @"C:\Users\me\proj"), (saved.ClaudeRunning, saved.ClaudeSessionId, saved.WorkingDirectory));
    }

    [Fact]
    public void A_restored_claude_pane_that_leaves_claude_is_saved_as_a_plain_shell()
    {
        var tab = RestoredTab(PowerShell, new PaneLayout(PowerShell.Id, @"C:\Users\me\proj", FirstId, ClaudeRunning: true));
        tab.MainPane.ApplyTerminalTitle(IdleTitle);

        tab.MainPane.ApplyTerminalTitle("PowerShell");

        var saved = tab.ToLayout().Root.Pane!;
        Assert.Equal((false, null), (saved.ClaudeRunning, saved.ClaudeSessionId));
    }

    [Fact]
    public async Task The_known_conversation_of_the_pane_is_passed_to_the_locator()
    {
        var locator = new FakeLocator(null);
        var viewModel = new MainViewModel([PowerShell], [], new AppSettings(), _ => { }, locator);
        viewModel.Open(PowerShell);
        var pane = viewModel.SelectedTab!.MainPane;
        pane.ApplyTerminalTitle(IdleTitle);
        pane.ClaudeSessionId = FirstId;

        await viewModel.ResolveClaudeSessionAsync(pane);

        Assert.Equal(FirstId, locator.LastKnownId);
    }

    [Fact]
    public void The_known_conversation_wins_over_a_more_recent_transcript_of_the_same_folder()
    {
        var locator = new TranscriptSessionLocator(_root);
        var folder = TranscriptSessionLocator.EncodeDirectory(@"C:\Users\me\proj");
        Transcript(folder, FirstId, Detected.AddMinutes(5));
        Transcript(folder, SecondId, Detected.AddMinutes(9));

        var info = locator.Locate(@"C:\Users\me\proj", Detected, NoneClaimed, FirstId, Detected.AddMinutes(1));

        Assert.Equal(FirstId, info?.SessionId);
    }

    [Fact]
    public void A_known_conversation_no_longer_written_gives_way_to_the_new_one()
    {
        var locator = new TranscriptSessionLocator(_root);
        var folder = TranscriptSessionLocator.EncodeDirectory(@"C:\Users\me\proj");
        Transcript(folder, FirstId, Detected.AddMinutes(2));
        Transcript(folder, SecondId, Detected.AddMinutes(9));

        var info = locator.Locate(@"C:\Users\me\proj", Detected, NoneClaimed, FirstId, Detected.AddMinutes(5));

        Assert.Equal(SecondId, info?.SessionId);
    }

    [Fact]
    public void A_known_conversation_claimed_by_another_pane_is_never_taken()
    {
        var locator = new TranscriptSessionLocator(_root);
        var folder = TranscriptSessionLocator.EncodeDirectory(@"C:\Users\me\proj");
        Transcript(folder, FirstId, Detected.AddMinutes(5));
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FirstId };

        Assert.Null(locator.Locate(@"C:\Users\me\proj", Detected, claimed, FirstId, Detected.AddMinutes(1)));
    }

    [Fact]
    public void The_view_writes_the_startup_input_right_after_the_session_starts()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "ShellDeck", "Terminal", "TerminalView.cs"));

        var start = source.IndexOf("ShellSession.Start(", StringComparison.Ordinal);
        var input = source.IndexOf("TakeStartupInput()", StringComparison.Ordinal);
        Assert.True(start >= 0 && input > start, "StartupInput must be written after ShellSession.Start");
    }

    private static TerminalPane RestoredPane(ShellProfile profile, PaneLayout layout) => RestoredTab(profile, layout).MainPane;

    private static TerminalTab RestoredTab(ShellProfile profile, PaneLayout layout)
    {
        var node = new PaneNodeLayout(Pane: layout);
        return TerminalTab.Restore(new TabLayout(node, null, null, 0, 0), _ => profile, profile)!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "ShellDeck")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("repository root");
    }

    private sealed class FakeLocator(ClaudeSessionInfo? result) : IClaudeSessionLocator
    {
        public int Calls { get; private set; }

        public IReadOnlySet<string>? LastClaimed { get; private set; }

        public string? LastKnownId { get; private set; }

        public ClaudeSessionInfo? Locate(string? cwd, DateTime sinceUtc, IReadOnlySet<string> claimed, string? knownId = null, DateTime? knownSinceUtc = null)
        {
            Calls++;
            LastClaimed = claimed;
            LastKnownId = knownId;
            return result;
        }
    }
}
