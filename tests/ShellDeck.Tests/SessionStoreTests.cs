using System.IO;
using ShellDeck.Session;
using ShellDeck.ViewModels;

namespace ShellDeck.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "shelldeck-session-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private SessionStore NewStore() => new(Path.Combine(_folder, "session.json"));

    private static PaneNodeLayout Leaf(string profile, string? cwd = null, string? claudeId = null, bool claude = false) =>
        new(Pane: new PaneLayout(profile, cwd, claudeId, claude));

    private static PaneNodeLayout Split(SplitDirection direction, double ratio, PaneNodeLayout first, PaneNodeLayout second) =>
        new(Split: new SplitLayout(direction, ratio, first, second));

    private static SessionLayout Sample() => new(
        SessionLayout.CurrentVersion,
        [
            new TabLayout(
                Split(
                    SplitDirection.Right,
                    0.3,
                    Leaf("wsl:Ubuntu", "/home/me/proj"),
                    Split(SplitDirection.Down, 0.7, Leaf("pwsh", @"C:\Users\me\My Dir", "3f2c8a10-1b2c-4d5e-8f90-123456789abc", claude: true), Leaf("cmd"))),
                "Teal",
                "#FF00AA",
                MainPane: 2,
                ActivePane: 1),
            new TabLayout(Leaf("pwsh"), null, null, 0, 0),
        ],
        SelectedTab: 1);

    [Fact]
    public void A_layout_survives_a_save_then_a_load()
    {
        var store = NewStore();
        var layout = Sample();

        store.Save(layout);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(SessionStore.ToJson(layout), SessionStore.ToJson(loaded));
        Assert.Equal(2, loaded.Tabs.Count);
        Assert.Equal(1, loaded.SelectedTab);
        var tab = loaded.Tabs[0];
        Assert.Equal("Teal", tab.ColorName);
        Assert.Equal("#FF00AA", tab.ColorHex);
        Assert.Equal(2, tab.MainPane);
        Assert.Equal(1, tab.ActivePane);
        var outer = tab.Root.Split!;
        Assert.Equal(SplitDirection.Right, outer.Direction);
        Assert.Equal(0.3, outer.Ratio);
        Assert.Equal("/home/me/proj", outer.First.Pane!.WorkingDirectory);
        var inner = outer.Second.Split!;
        Assert.Equal(SplitDirection.Down, inner.Direction);
        Assert.Equal(0.7, inner.Ratio);
        Assert.Equal(new PaneLayout("pwsh", @"C:\Users\me\My Dir", "3f2c8a10-1b2c-4d5e-8f90-123456789abc", true), inner.First.Pane);
    }

    [Fact]
    public void The_file_reads_naturally_when_opened_in_an_editor()
    {
        var json = SessionStore.ToJson(Sample());

        Assert.Contains("\"direction\": \"right\"", json);
        Assert.Contains("\"version\": 1", json);
        Assert.DoesNotContain("null", json.Replace("\"colorName\": null", string.Empty).Replace("\"colorHex\": null", string.Empty)
            .Replace("\"workingDirectory\": null", string.Empty).Replace("\"claudeSessionId\": null", string.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"version\": 1, \"tabs\": [ { \"root\": ")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{ \"version\": 1 }")]
    [InlineData("{ \"version\": 1, \"tabs\": [ { \"root\": {} } ], \"selectedTab\": 0 }")]
    [InlineData("{ \"version\": 1, \"tabs\": [ null ], \"selectedTab\": 0 }")]
    public void A_truncated_or_invalid_file_loads_as_null(string content)
    {
        var store = NewStore();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.FilePath, content);

        Assert.Null(store.Load());
    }

    [Fact]
    public void An_unknown_version_loads_as_null()
    {
        var store = NewStore();
        store.Save(Sample() with { Version = 99 });

        Assert.Null(store.Load());
    }

    [Fact]
    public void A_missing_file_loads_as_null()
    {
        Assert.Null(NewStore().Load());
    }

    [Fact]
    public void Saving_leaves_only_session_json_behind()
    {
        var store = NewStore();

        store.Save(Sample());
        store.Save(Sample());

        Assert.Equal([store.FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Saving_into_an_unusable_location_does_not_throw()
    {
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "blocker");
        File.WriteAllText(blocker, "a file where a folder is expected");

        new SessionStore(Path.Combine(blocker, "session.json")).Save(Sample());
    }

    [Fact]
    public void The_settings_watcher_ignores_the_session_file()
    {
        // AppSettings.Watch filters on the exact file name of settings.json, so neither session.json nor
        // session.json.tmp (written aside by Save) can trigger a settings reload.
        var settingsName = Path.GetFileName(AppSettings.FilePath);

        Assert.Equal("settings.json", settingsName);
        Assert.Equal(Path.GetDirectoryName(AppSettings.FilePath), Path.GetDirectoryName(SessionStore.DefaultPath));
        Assert.NotEqual(settingsName, Path.GetFileName(SessionStore.DefaultPath));
        Assert.NotEqual(settingsName, Path.GetFileName(SessionStore.DefaultPath + ".tmp"));
    }
}
