using System.IO;
using ShellDeck.Session;
using ShellDeck.Shells;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class LayoutSaverTests
{
    private sealed class FakeTimer(Action tick) : IDebounceTimer
    {
        public int Restarts { get; private set; }
        public bool Running { get; private set; }

        public void Restart()
        {
            Restarts++;
            Running = true;
        }

        public void Stop() => Running = false;

        public void Elapse()
        {
            if (Running)
            {
                tick();
            }
        }
    }

    private static (LayoutSaver Saver, FakeTimer Timer, List<int> Saves) Create()
    {
        FakeTimer? timer = null;
        var saves = new List<int>();
        var saver = new LayoutSaver(() => saves.Add(saves.Count + 1), tick => timer = new FakeTimer(tick));
        return (saver, timer!, saves);
    }

    private static SessionLayout Layout(int tabs) => new(
        SessionLayout.CurrentVersion,
        Enumerable.Range(0, tabs)
            .Select(_ => new TabLayout(new PaneNodeLayout { Pane = new PaneLayout("pwsh", null, null, false) }, null, null, 0, 0))
            .ToList(),
        0);

    [Fact]
    public void Ten_requests_in_a_row_give_one_save_after_the_delay()
    {
        var (saver, timer, saves) = Create();

        for (var i = 0; i < 10; i++)
        {
            saver.Request();
        }

        Assert.Empty(saves);
        timer.Elapse();
        timer.Elapse();

        Assert.Equal((1, 10), (saves.Count, timer.Restarts));
    }

    [Fact]
    public void Flush_saves_at_once_and_cancels_the_pending_save()
    {
        var (saver, timer, saves) = Create();
        saver.Request();

        saver.Flush();
        timer.Elapse();

        Assert.Single(saves);
    }

    [Fact]
    public void Nothing_is_saved_after_Stop_even_when_requests_keep_coming()
    {
        var (saver, timer, saves) = Create();
        saver.Request();
        saver.Flush();
        saver.Stop();

        saver.Request();
        saver.Flush();
        timer.Elapse();

        Assert.Single(saves);
    }

    [Fact]
    public void Stop_drops_a_pending_save()
    {
        var (saver, timer, saves) = Create();
        saver.Request();

        saver.Stop();
        timer.Elapse();

        Assert.Empty(saves);
    }

    [Fact]
    public void Never_restores_nothing_and_does_not_ask()
    {
        var asked = false;

        var plan = StartupPlan.Decide(RestoreSession.Never, Layout(2), () => asked = true);

        Assert.Equal((null, false), (plan, asked));
    }

    [Theory]
    [InlineData(RestoreSession.Always, 1)]
    [InlineData(RestoreSession.Ask, 1)]
    [InlineData(RestoreSession.Never, 0)]
    public void A_save_is_skipped_when_the_mode_is_never_at_the_time_of_the_save(RestoreSession mode, int expectedSaves)
    {
        var saves = 0;

        StartupPlan.SaveUnlessNever(() => mode, () => saves++)();

        Assert.Equal(expectedSaves, saves);
    }

    [Fact]
    public void Switching_the_mode_to_never_after_the_saver_is_wired_stops_the_writes()
    {
        var mode = RestoreSession.Always;
        var saves = 0;
        var save = StartupPlan.SaveUnlessNever(() => mode, () => saves++);

        save();
        mode = RestoreSession.Never;
        save();

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Always_restores_without_asking()
    {
        var saved = Layout(2);
        var asked = false;

        var plan = StartupPlan.Decide(RestoreSession.Always, saved, () => asked = true);

        Assert.Equal((saved, false), (plan, asked));
    }

    [Fact]
    public void Ask_restores_when_the_user_accepts()
    {
        var saved = Layout(2);

        Assert.Same(saved, StartupPlan.Decide(RestoreSession.Ask, saved, () => true));
    }

    [Fact]
    public void Ask_opens_the_default_terminal_when_the_user_declines()
    {
        Assert.Null(StartupPlan.Decide(RestoreSession.Ask, Layout(2), () => false));
    }

    [Theory]
    [InlineData(RestoreSession.Ask)]
    [InlineData(RestoreSession.Always)]
    public void Nothing_to_restore_gives_the_default_terminal_without_a_question(RestoreSession mode)
    {
        var asked = false;

        var emptyPlan = StartupPlan.Decide(mode, Layout(0), () => asked = true);
        var missingPlan = StartupPlan.Decide(mode, null, () => asked = true);

        Assert.Equal((null, null, false), (emptyPlan, missingPlan, asked));
    }

    private static readonly ShellProfile Ubuntu = new("Ubuntu", "wsl.exe -d Ubuntu", ShellKind.Wsl, "~");
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(200);

    private static TerminalPane RestoredPane(string? directory)
    {
        var tab = new TerminalTab(Ubuntu);
        tab.MainPane.RestoredDirectory = directory;
        return tab.MainPane;
    }

    [Fact]
    public void A_restored_pane_that_fails_at_startup_restarts_once_in_the_profile_directory()
    {
        var pane = RestoredPane("/home/me/gone");

        var first = pane.TryRestartWithoutRestoredDirectory(1, Fast);
        var second = pane.TryRestartWithoutRestoredDirectory(1, Fast);

        Assert.Equal((true, false, null), (first, second, pane.RestoredDirectory));
    }

    [Fact]
    public void A_pane_without_a_restored_directory_does_not_restart()
    {
        Assert.False(RestoredPane(null).TryRestartWithoutRestoredDirectory(1, Fast));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 60_000)]
    public void A_normal_exit_does_not_restart_a_restored_pane(int exitCode, int lifetimeMs)
    {
        var pane = RestoredPane("/home/me/proj");

        Assert.False(pane.TryRestartWithoutRestoredDirectory(exitCode, TimeSpan.FromMilliseconds(lifetimeMs)));
    }

    [Fact]
    public void The_window_is_wired_to_save_on_layout_changes_and_to_flush_then_stop_on_close()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ShellDeck", "MainWindow.xaml.cs"));

        Assert.Contains("_viewModel.LayoutChanged += (_, _) => _layoutSaver.Request();", source);
        Assert.True(
            source.IndexOf("SaveWindowPlacement();", source.IndexOf("OnClosing(object", StringComparison.Ordinal), StringComparison.Ordinal)
                < source.IndexOf("_layoutSaver?.Flush();", StringComparison.Ordinal)
            && source.IndexOf("_layoutSaver?.Flush();", StringComparison.Ordinal)
                < source.IndexOf("_layoutSaver?.Stop();", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "ShellDeck", "MainWindow.xaml.cs")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
