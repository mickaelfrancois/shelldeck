using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using Pupitre.Shells;

namespace Pupitre.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int LastTabNumber = 9;

    /// <summary>Below this many terminals the whole launcher is seen at a glance: no filter box.</summary>
    private const int FilterThreshold = 10;

    private readonly IReadOnlyList<ShellProfile> _discoveredShells;
    private readonly IReadOnlyList<ShellProfile> _discoveredSshConnections;
    private readonly ObservableCollection<ShellProfile> _shellItems = [];
    private readonly ObservableCollection<ShellProfile> _sshItems = [];
    private readonly ListCollectionView _shells;
    private readonly ListCollectionView _sshConnections;
    private readonly Action<AppSettings> _saveSettings;
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
        Action<AppSettings> saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _discoveredShells = shells;
        _discoveredSshConnections = sshConnections;
        _shells = new ListCollectionView(_shellItems) { Filter = MatchesFilter };
        _sshConnections = new ListCollectionView(_sshItems) { Filter = MatchesFilter };
        ArrangeProfiles();
    }

    /// <summary>Raised when a tab the user is not looking at needs them.</summary>
    public event EventHandler<TerminalTab>? AttentionRequested;

    public ObservableCollection<TerminalTab> Tabs { get; } = [];

    public ICollectionView Shells => _shells;

    public ICollectionView SshConnections => _sshConnections;

    public int SshConnectionCount => _launcher.SshConnections.Count;

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
        }
    }

    /// <summary>Opened at startup: the chosen profile while it exists and is shown, else the first shell of the launcher.</summary>
    public ShellProfile? DefaultProfile =>
        _launcher.Visible.FirstOrDefault(profile => IsChosenDefault(profile))
        ?? _launcher.Visible.FirstOrDefault(profile => profile.Kind is not ShellKind.Ssh);

    public string? DefaultProfileId => DefaultProfile?.Id;

    private bool IsFiltering => FilterText.Trim().Length > 0;

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

    public void Open(ShellProfile profile)
    {
        var tab = new TerminalTab(profile);
        tab.AttentionRequested += (_, _) => AttentionRequested?.Invoke(this, tab);
        tab.Ended += (_, _) => Close(tab);
        Tabs.Add(tab);
        SelectedTab = tab;
        FilterText = string.Empty;
    }

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

        if (wasSelected)
        {
            SelectedTab = Tabs.ElementAtOrDefault(Math.Min(closedIndex, Tabs.Count - 1));
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
