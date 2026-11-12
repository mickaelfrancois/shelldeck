using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using ShellDeck.Session;
using ShellDeck.Shells;

namespace ShellDeck.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int LastTabNumber = 9;

    /// <summary>Below this many terminals the whole launcher is seen at a glance: no filter box.</summary>
    private const int FilterThreshold = 10;

    private readonly IReadOnlyList<ShellProfile> _discoveredShells;
    private readonly IReadOnlyList<ShellProfile> _discoveredSshConnections;
    private readonly ObservableCollection<ShellProfile> _shellItems = [];
    private readonly ObservableCollection<ShellProfile> _sshItems = [];
    private readonly ObservableCollection<TerminalTab> _terminals = [];
    private readonly ListCollectionView _shells;
    private readonly ListCollectionView _sshConnections;
    private readonly Action<AppSettings> _saveSettings;
    private readonly IClaudeSessionLocator _claudeSessions;
    private AppSettings _settings;
    private LauncherProfiles _launcher = new([], [], []);
    private TerminalTab? _selectedTab;
    private string _filterText = string.Empty;
    private bool _isSshExpanded;
    private bool _isHiddenExpanded;

    public MainViewModel(
        IReadOnlyList<ShellProfile> shells,
        IReadOnlyList<ShellProfile> sshConnections,
        AppSettings settings,
        Action<AppSettings> saveSettings,
        IClaudeSessionLocator? claudeSessions = null)
    {
        _claudeSessions = claudeSessions ?? TranscriptSessionLocator.ForCurrentUser();
        _settings = settings;
        _saveSettings = saveSettings;
        _discoveredShells = shells;
        _discoveredSshConnections = sshConnections;
        _shells = new ListCollectionView(_shellItems) { Filter = MatchesFilter };
        _sshConnections = new ListCollectionView(_sshItems) { Filter = MatchesFilter };
        Terminals = new ReadOnlyObservableCollection<TerminalTab>(_terminals);
        Tabs.CollectionChanged += (_, _) => LayoutChanged?.Invoke(this, EventArgs.Empty);
        ArrangeProfiles();
    }

    /// <summary>Raised when a tab the user is not looking at needs them.</summary>
    public event EventHandler<TerminalTab>? AttentionRequested;

    /// <summary>Raised when something session.json keeps changes: tabs opened, closed or moved, the selected tab, any tab's layout.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>The tab list, in the order the user arranged it.</summary>
    public ObservableCollection<TerminalTab> Tabs { get; } = [];

    /// <summary>
    /// The same tabs in opening order, for the terminal area. Reordering <see cref="Tabs"/> there would move the
    /// terminals in the visual tree, and a terminal view unloaded on the way ends its session.
    /// </summary>
    public ReadOnlyObservableCollection<TerminalTab> Terminals { get; }

    public ICollectionView Shells => _shells;

    public ICollectionView SshConnections => _sshConnections;

    public int SshConnectionCount => _launcher.SshConnections.Count;

    /// <summary>Shells a pane can be split with: not filtered, without the hidden ones, in launcher order.</summary>
    public IReadOnlyList<ShellProfile> SplitShells => _launcher.Shells;

    /// <summary>SSH connections a pane can be split with: not filtered, without the hidden ones, in launcher order.</summary>
    public IReadOnlyList<ShellProfile> SplitSshConnections => _launcher.SshConnections;

    /// <summary>Not filtered: hidden terminals are out of the way until the user looks for them here.</summary>
    public ObservableCollection<ShellProfile> HiddenProfiles { get; } = [];

    public string HiddenHeader => $"Masqués · {HiddenProfiles.Count}";

    public bool IsHiddenExpanded
    {
        get => _isHiddenExpanded;
        set
        {
            if (SetField(ref _isHiddenExpanded, value))
            {
                OnPropertyChanged(nameof(IsHiddenListVisible));
            }
        }
    }

    public bool IsHiddenSectionVisible => HiddenProfiles.Count > 0 && !IsFiltering;

    public bool IsHiddenListVisible => IsHiddenSectionVisible && IsHiddenExpanded;

    public string SshHeader => IsFiltering
        ? $"SSH · {_sshConnections.Count}/{SshConnectionCount}"
        : $"SSH · {SshConnectionCount}";

    public bool IsSshExpanded
    {
        get => _isSshExpanded;
        set
        {
            if (SetField(ref _isSshExpanded, value))
            {
                OnPropertyChanged(nameof(IsSshListVisible));
            }
        }
    }

    /// <summary>Typing a filter reveals the matching connections even when the section is collapsed.</summary>
    public bool IsSshListVisible => IsSshExpanded || IsFiltering;

    public bool HasNoSshMatch => IsSshListVisible && _sshConnections.IsEmpty;

    public bool IsFilterVisible => _launcher.Visible.Count() > FilterThreshold;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetField(ref _filterText, value))
            {
                return;
            }

            _shells.Refresh();
            _sshConnections.Refresh();
            OnPropertyChanged(nameof(SshHeader));
            OnPropertyChanged(nameof(IsSshListVisible));
            OnPropertyChanged(nameof(HasNoSshMatch));
            OnPropertyChanged(nameof(IsHiddenSectionVisible));
            OnPropertyChanged(nameof(IsHiddenListVisible));
        }
    }

    public PanelSide PanelSide
    {
        get => _settings.PanelSide;
        set
        {
            if (value == _settings.PanelSide)
            {
                return;
            }

            UpdateSettings(_settings with { PanelSide = value });
            OnPropertyChanged();
        }
    }

    public TerminalTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            var previous = _selectedTab;
            if (!SetField(ref _selectedTab, value))
            {
                return;
            }

            previous?.IsSelected = false;
            value?.IsSelected = true;
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Opened at startup: the chosen profile while it exists and is shown, else the first shell of the launcher.</summary>
    public ShellProfile? DefaultProfile =>
        _launcher.Visible.FirstOrDefault(profile => IsChosenDefault(profile))
        ?? _launcher.Visible.FirstOrDefault(profile => profile.Kind is not ShellKind.Ssh);

    public string? DefaultProfileId => DefaultProfile?.Id;

    private bool IsFiltering => FilterText.Trim().Length > 0;

    public RestoreSession RestoreSession => _settings.RestoreSession;

    public WindowPlacement? WindowPlacement => _settings.Window;

    public void SaveWindowPlacement(WindowPlacement placement) =>
        UpdateSettings(_settings with { Window = placement });

    public void SetDefaultProfile(ShellProfile profile)
    {
        UpdateSettings(_settings with { DefaultProfileId = profile.Id, DefaultProfileName = null });
        OnPropertyChanged(nameof(DefaultProfile));
        OnPropertyChanged(nameof(DefaultProfileId));
    }

    public void HideProfile(ShellProfile profile) => UpdateProfiles(_settings.Profiles.Hide(profile.Id));

    public void ShowProfile(ShellProfile profile) => UpdateProfiles(_settings.Profiles.Show(profile.Id));

    /// <summary>Moves the terminal before (-1) or after (1) its neighbor in its section of the launcher.</summary>
    public void MoveProfile(ShellProfile profile, int offset) =>
        UpdateProfiles(ProfileCatalog.Move(_discoveredShells, _discoveredSshConnections, _settings.Profiles, profile.Id, offset));

    /// <summary>For a discovered terminal: its own starting directory, the one discovery gives meaning "no choice".</summary>
    public void SetStartingDirectory(ShellProfile profile, string directory)
    {
        var discovered = _discoveredShells.FirstOrDefault(shell => shell.Id == profile.Id);
        var chosen = discovered is not null && directory.Trim() == discovered.Location ? string.Empty : directory;
        UpdateProfiles(_settings.Profiles.WithStartingDirectory(profile.Id, chosen));
    }

    public void SaveCustomProfile(CustomProfile profile) => UpdateProfiles(_settings.Profiles.SaveCustom(profile));

    /// <summary>Only a terminal the user added can be deleted: a discovered one would come back, it is hidden instead.</summary>
    public void DeleteProfile(ShellProfile profile)
    {
        if (profile.IsCustom)
        {
            UpdateProfiles(_settings.Profiles.Remove(profile.Id));
        }
    }

    public void Open(ShellProfile profile) => Open(profile, Tabs.Count);

    /// <summary>A new tab right after <paramref name="tab"/>, with its profile and color, as Windows Terminal's "Duplicate tab".</summary>
    public void Duplicate(TerminalTab tab) =>
        Open(tab.Profile, Tabs.IndexOf(tab) + 1).Color = tab.Color;

    /// <summary>
    /// Brings <paramref name="tab"/> forward and splits its active pane with <paramref name="profile"/> (the active
    /// pane's own profile when null); the new pane takes the keyboard. The launcher filter is left as typed.
    /// </summary>
    public void Split(TerminalTab tab, SplitDirection direction, ShellProfile? profile = null)
    {
        SelectedTab = tab;
        tab.Split(direction, profile);
    }

    private TerminalTab Open(ShellProfile profile, int index) => Add(new TerminalTab(profile), index);

    private TerminalTab Add(TerminalTab tab, int index)
    {
        tab.AttentionRequested += (_, _) => AttentionRequested?.Invoke(this, tab);
        tab.Ended += (_, _) => Close(tab);
        tab.LayoutChanged += OnTabLayoutChanged;
        tab.ClaudeTurnEnded += OnClaudeTurnEnded;
        Tabs.Insert(index, tab);
        _terminals.Add(tab);
        SelectedTab = tab;
        FilterText = string.Empty;
        return tab;
    }

    private void OnClaudeTurnEnded(object? sender, TerminalPane pane) => _ = ResolveClaudeSessionAsync(pane);

    /// <summary>
    /// Finds which conversation the pane's Claude Code is on: the pane's known conversation while its transcript is still being written, else the transcript written since it was seen, that no other pane
    /// claims. The disk is read off the UI thread; the result is applied back on it. A failure keeps the previous id.
    /// </summary>
    public async Task ResolveClaudeSessionAsync(TerminalPane pane)
    {
        // SSH runs Claude elsewhere and WSL keeps its transcripts in the distro: neither is readable here.
        if (!pane.Profile.HasStartingDirectory || pane.Profile.Kind is ShellKind.Wsl || pane.ClaudeDetectedUtc is not { } since)
        {
            return;
        }

        var claimed = Tabs.SelectMany(tab => tab.Panes)
            .Where(other => other != pane && other.ClaudeSessionId is not null)
            .Select(other => other.ClaudeSessionId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cwd = pane.WorkingDirectory;
        var knownId = pane.ClaudeSessionId;
        var knownSince = pane.ClaudeResolvedUtc;
        var startedUtc = DateTime.UtcNow;

        ClaudeSessionInfo? info;
        try
        {
            info = await Task.Run(() => _claudeSessions.Locate(cwd, since, claimed, knownId, knownSince));
        }
        catch (Exception)
        {
            return;
        }

        if (info is null || !pane.IsClaudeRunning)
        {
            return;
        }

        pane.ClaudeResolvedUtc = startedUtc;
        pane.ClaudeDirectory = info.Cwd;
        pane.ClaudeSessionId = info.SessionId;
    }

    private void OnTabLayoutChanged(object? sender, EventArgs e) => LayoutChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>The tabs, their panes and the selected tab, as session.json keeps them.</summary>
    public SessionLayout Snapshot() =>
        new(SessionLayout.CurrentVersion, [.. Tabs.Select(tab => tab.ToLayout())], Math.Max(0, Tabs.IndexOf(SelectedTab!)));

    /// <summary>
    /// Opens the tabs of a saved layout and returns how many were opened. A profile that is gone gives way to the default
    /// profile, a hidden one still counts; with nothing to start a pane the pane is left out. Nothing is opened for an empty
    /// layout: the caller opens the default terminal.
    /// </summary>
    public int Restore(SessionLayout layout)
    {
        var opened = new List<TerminalTab>();
        foreach (var tabLayout in layout.Tabs ?? [])
        {
            if (tabLayout is not null && TerminalTab.Restore(tabLayout, ResolveProfile, DefaultProfile) is { } tab)
            {
                Add(tab, Tabs.Count);
                opened.Add(tab);
            }
        }

        if (opened.Count > 0)
        {
            SelectedTab = opened.ElementAtOrDefault(layout.SelectedTab) ?? opened[0];
        }

        return opened.Count;
    }

    /// <summary>By id among every known terminal, the hidden ones included.</summary>
    private ShellProfile? ResolveProfile(string id) =>
        _launcher.Shells.Concat(_launcher.SshConnections).Concat(_launcher.Hidden).FirstOrDefault(profile => profile.Id == id);

    /// <summary>A new tab without choosing: the default profile, as Windows Terminal's Ctrl+Shift+T.</summary>
    public void OpenDefault()
    {
        if (DefaultProfile is { } defaultProfile)
        {
            Open(defaultProfile);
        }
    }

    /// <summary>Opens the first shell, else the first SSH connection, matching the filter; the default profile when there is no filter.</summary>
    public void OpenFirstMatch()
    {
        if (!IsFiltering)
        {
            OpenDefault();
            return;
        }

        var firstMatch = _shells.Cast<ShellProfile>().FirstOrDefault() ?? _sshConnections.Cast<ShellProfile>().FirstOrDefault();
        if (firstMatch is not null)
        {
            Open(firstMatch);
        }
    }

    public void Close(TerminalTab tab)
    {
        // Captured first: removing the selected item makes the tab list push a null selection back to us.
        var wasSelected = SelectedTab == tab || SelectedTab is null;
        var closedIndex = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        _terminals.Remove(tab);
        tab.LayoutChanged -= OnTabLayoutChanged;
        tab.ClaudeTurnEnded -= OnClaudeTurnEnded;
        tab.EndSessions();

        if (wasSelected)
        {
            SelectedTab = Tabs.ElementAtOrDefault(Math.Min(closedIndex, Tabs.Count - 1));
        }
    }

    /// <summary>The tabs listed above <paramref name="tab"/>, top first.</summary>
    public IReadOnlyList<TerminalTab> TabsAbove(TerminalTab tab) => Tabs.TakeWhile(other => other != tab).ToList();

    /// <summary>The tabs listed below <paramref name="tab"/>, top first.</summary>
    public IReadOnlyList<TerminalTab> TabsBelow(TerminalTab tab) => Tabs.SkipWhile(other => other != tab).Skip(1).ToList();

    public IReadOnlyList<TerminalTab> OtherTabs(TerminalTab tab) => Tabs.Where(other => other != tab).ToList();

    /// <summary>The app is closing: every shell ends with it.</summary>
    public void EndAllSessions()
    {
        foreach (var tab in Tabs)
        {
            tab.EndSessions();
        }
    }

    /// <summary>Puts the tab at <paramref name="index"/> in the tab list, e.g. while it is dragged there.</summary>
    public void MoveTab(TerminalTab tab, int index)
    {
        var currentIndex = Tabs.IndexOf(tab);
        if (currentIndex >= 0 && index >= 0 && index < Tabs.Count && index != currentIndex)
        {
            Tabs.Move(currentIndex, index);
        }
    }

    public void SelectNextTab() => SelectTabAtOffset(1);

    public void SelectPreviousTab() => SelectTabAtOffset(-1);

    /// <summary>Selects tab 1 to 8 by position; 9 always means the last tab, as in browsers.</summary>
    public void SelectTabByNumber(int number)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        if (number == LastTabNumber)
        {
            SelectedTab = Tabs[^1];
            return;
        }

        if (number >= 1 && number <= Tabs.Count)
        {
            SelectedTab = Tabs[number - 1];
        }
    }

    public void TogglePanelSide() =>
        PanelSide = PanelSide is PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    private void SelectTabAtOffset(int offset)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedTab is null ? -1 : Tabs.IndexOf(SelectedTab);
        if (currentIndex < 0)
        {
            SelectedTab = offset > 0 ? Tabs[0] : Tabs[^1];
            return;
        }

        SelectedTab = Tabs[(currentIndex + offset + Tabs.Count) % Tabs.Count];
    }

    /// <summary>Writes the settings as they are, e.g. to create the file before opening it in an editor.</summary>
    public void SaveCurrentSettings() => _saveSettings(_settings);

    /// <summary>The settings file was edited by hand: take its values, without writing them back.</summary>
    public void ReloadSettings(AppSettings settings)
    {
        if (settings == _settings)
        {
            return;
        }

        var previous = _settings;
        _settings = settings;
        if (settings.PanelSide != previous.PanelSide)
        {
            OnPropertyChanged(nameof(PanelSide));
        }

        if (settings.Profiles != previous.Profiles)
        {
            ArrangeProfiles();
        }

        OnPropertyChanged(nameof(DefaultProfile));
        OnPropertyChanged(nameof(DefaultProfileId));
    }

    private void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _saveSettings(settings);
    }

    private void UpdateProfiles(ProfileSettings profiles)
    {
        if (profiles == _settings.Profiles)
        {
            return;
        }

        UpdateSettings(_settings with { Profiles = profiles });
        ArrangeProfiles();
        OnPropertyChanged(nameof(DefaultProfile));
        OnPropertyChanged(nameof(DefaultProfileId));
    }

    /// <summary>Rebuilds the launcher lists; open tabs keep the profile they started with.</summary>
    private void ArrangeProfiles()
    {
        _launcher = ProfileCatalog.Arrange(_discoveredShells, _discoveredSshConnections, _settings.Profiles);
        Fill(_shellItems, _launcher.Shells);
        Fill(_sshItems, _launcher.SshConnections);
        Fill(HiddenProfiles, _launcher.Hidden);

        // Hiding terminals can take the filter box away: a filter left in it would hide terminals with no way to clear it.
        if (!IsFilterVisible)
        {
            FilterText = string.Empty;
        }

        OnPropertyChanged(nameof(IsFilterVisible));
        OnPropertyChanged(nameof(SshConnectionCount));
        OnPropertyChanged(nameof(SshHeader));
        OnPropertyChanged(nameof(HasNoSshMatch));
        OnPropertyChanged(nameof(HiddenHeader));
        OnPropertyChanged(nameof(IsHiddenSectionVisible));
        OnPropertyChanged(nameof(IsHiddenListVisible));
    }

    private static void Fill(ObservableCollection<ShellProfile> items, IReadOnlyList<ShellProfile> profiles)
    {
        items.Clear();
        foreach (var profile in profiles)
        {
            items.Add(profile);
        }
    }

    /// <summary>By id; by name for a file written before ids existed.</summary>
    private bool IsChosenDefault(ShellProfile profile) =>
        _settings.DefaultProfileId is { } id ? profile.Id == id : profile.Name == _settings.DefaultProfileName;

    private bool MatchesFilter(object item)
    {
        var filter = FilterText.Trim();
        return filter.Length == 0
            || item is ShellProfile profile
            && (profile.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || profile.Kind is ShellKind.Ssh && profile.Location.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }
}
